using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Common;

namespace VehiclePermitSystemWeb.Services.Notifications
{
    public interface IEmailNotificationDispatcher
    {
        Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default);
    }

    public sealed class EmailNotificationDispatcher : IEmailNotificationDispatcher
    {
        private const int BatchSize = 20;
        private const int MaxAttempts = 5;
        private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(10);
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemEmailSender _emailSender;
        private readonly ISystemClock _systemClock;
        private readonly ILogger<EmailNotificationDispatcher> _logger;

        public EmailNotificationDispatcher(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemEmailSender emailSender,
            ISystemClock systemClock,
            ILogger<EmailNotificationDispatcher> logger
        )
        {
            _dbContextFactory = dbContextFactory;
            _emailSender = emailSender;
            _systemClock = systemClock;
            _logger = logger;
        }

        public async Task<int> ProcessPendingAsync(
            CancellationToken cancellationToken = default
        )
        {
            if (!_emailSender.IsConfigured)
            {
                return 0;
            }

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var now = _systemClock.UtcNow;
            var staleLockBefore = now.Subtract(ProcessingLease);
            var messageIds = await db
                .EmailNotificationOutbox.IgnoreQueryFilters()
                .Where(message =>
                    (
                        message.Status == EmailNotificationOutbox.StatusPending
                        && message.NextAttemptAtUtc <= now
                    )
                    || (
                        message.Status == EmailNotificationOutbox.StatusProcessing
                        && message.LockedAtUtc < staleLockBefore
                    )
                )
                .OrderBy(message => message.NextAttemptAtUtc)
                .ThenBy(message => message.Id)
                .Select(message => message.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            var processed = 0;
            foreach (var messageId in messageIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lockToken = Guid.NewGuid().ToString("N");
                var claimedAtUtc = _systemClock.UtcNow;
                var claimed = await db
                    .EmailNotificationOutbox.IgnoreQueryFilters()
                    .Where(message =>
                        message.Id == messageId
                        && (
                            (
                                message.Status == EmailNotificationOutbox.StatusPending
                                && message.NextAttemptAtUtc <= claimedAtUtc
                            )
                            || (
                                message.Status == EmailNotificationOutbox.StatusProcessing
                                && message.LockedAtUtc < staleLockBefore
                            )
                        )
                    )
                    .ExecuteUpdateAsync(
                        updates =>
                            updates
                                .SetProperty(
                                    message => message.Status,
                                    EmailNotificationOutbox.StatusProcessing
                                )
                                .SetProperty(message => message.LockToken, lockToken)
                                .SetProperty(message => message.LockedAtUtc, claimedAtUtc)
                                .SetProperty(message => message.LastAttemptAtUtc, claimedAtUtc)
                                .SetProperty(
                                    message => message.AttemptCount,
                                    message => message.AttemptCount + 1
                                ),
                        cancellationToken
                    );
                if (claimed == 0)
                {
                    continue;
                }

                var message = await db
                    .EmailNotificationOutbox.IgnoreQueryFilters()
                    .SingleAsync(
                        item => item.Id == messageId && item.LockToken == lockToken,
                        cancellationToken
                    );

                SystemEmailSendResult result;
                try
                {
                    result = await _emailSender.SendAsync(
                        message.RecipientEmail,
                        message.RecipientName,
                        message.Subject,
                        message.HtmlBody,
                        message.TextBody,
                        cancellationToken
                    );
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Email notification {NotificationId} delivery threw unexpectedly.",
                        message.Id
                    );
                    result = new SystemEmailSendResult(
                        false,
                        "حدث خطأ غير متوقع أثناء إرسال الرسالة."
                    );
                }
                if (result.Succeeded)
                {
                    message.Status = EmailNotificationOutbox.StatusSent;
                    message.SentAtUtc = _systemClock.UtcNow;
                    message.LastError = string.Empty;
                }
                else
                {
                    message.LastError = Truncate(result.Message, 1000);
                    if (message.AttemptCount >= MaxAttempts)
                    {
                        message.Status = EmailNotificationOutbox.StatusFailed;
                        _logger.LogError(
                            "Email notification {NotificationId} exhausted all delivery attempts.",
                            message.Id
                        );
                    }
                    else
                    {
                        message.Status = EmailNotificationOutbox.StatusPending;
                        message.NextAttemptAtUtc = _systemClock.UtcNow.Add(
                            ResolveRetryDelay(message.AttemptCount)
                        );
                    }
                }

                message.LockToken = string.Empty;
                message.LockedAtUtc = null;
                await db.SaveChangesAsync(cancellationToken);
                processed++;
            }

            return processed;
        }

        private static TimeSpan ResolveRetryDelay(int attemptCount) =>
            attemptCount switch
            {
                <= 1 => TimeSpan.FromMinutes(1),
                2 => TimeSpan.FromMinutes(5),
                3 => TimeSpan.FromMinutes(15),
                _ => TimeSpan.FromHours(1),
            };

        private static string Truncate(string? value, int maxLength)
        {
            var normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
        }
    }
}
