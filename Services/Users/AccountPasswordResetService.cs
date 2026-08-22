using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Utilities.Security;

namespace VehiclePermitSystemWeb.Services.Users
{
    public interface IAccountPasswordResetService
    {
        bool IsDeliveryConfigured { get; }
        PasswordResetChallenge? CreateChallenge(string tenantId, string accountIdentifier);
        PasswordResetValidationResult Validate(string token);
        PasswordResetResult Reset(string token, string newPassword);
        Task<SystemEmailSendResult> SendAsync(
            PasswordResetChallenge challenge,
            string resetUrl,
            CancellationToken cancellationToken = default
        );
    }

    public sealed record PasswordResetChallenge(
        string TenantId,
        string Username,
        string Email,
        string DisplayName,
        string Token
    );

    public sealed record PasswordResetValidationResult(
        bool Succeeded,
        string Message,
        string TenantId = "",
        string Username = ""
    );

    public sealed record PasswordResetResult(
        bool Succeeded,
        string Message,
        string TenantId = "",
        string Username = "",
        string DisplayName = ""
    );

    public sealed class AccountPasswordResetService : IAccountPasswordResetService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ITimeLimitedDataProtector _protector;
        private readonly ISystemEmailSender _emailSender;
        private readonly ILogger<AccountPasswordResetService> _logger;
        private readonly TimeSpan _tokenLifetime;

        public AccountPasswordResetService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IDataProtectionProvider dataProtectionProvider,
            ISystemEmailSender emailSender,
            IConfiguration configuration,
            ILogger<AccountPasswordResetService> logger
        )
        {
            _dbContextFactory = dbContextFactory;
            _protector = dataProtectionProvider
                .CreateProtector("VehiclePermitSystemWeb.Account.PasswordReset.v1")
                .ToTimeLimitedDataProtector();
            _emailSender = emailSender;
            _logger = logger;
            var configuredMinutes = configuration.GetValue(
                "Security:PasswordResetTokenMinutes",
                30
            );
            _tokenLifetime = TimeSpan.FromMinutes(Math.Clamp(configuredMinutes, 10, 120));
        }

        public bool IsDeliveryConfigured => _emailSender.IsConfigured;

        public PasswordResetChallenge? CreateChallenge(
            string tenantId,
            string accountIdentifier
        )
        {
            var normalizedTenantId = (tenantId ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedIdentifier = (accountIdentifier ?? string.Empty).Trim();
            if (
                string.IsNullOrWhiteSpace(normalizedTenantId)
                || string.IsNullOrWhiteSpace(normalizedIdentifier)
            )
            {
                return null;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var eligibleUsers = db
                .UserAccounts.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(user =>
                    user.TenantId == normalizedTenantId
                    && user.IsActive
                    && user.IsEmailConfirmed
                    && user.Email != ""
                )
                .ToList();

            var user = eligibleUsers.FirstOrDefault(item =>
                string.Equals(
                    item.Username,
                    normalizedIdentifier,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (user == null)
            {
                var emailMatches = eligibleUsers
                    .Where(item =>
                        string.Equals(
                            item.Email.Trim(),
                            normalizedIdentifier,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .Take(2)
                    .ToList();
                if (emailMatches.Count == 1)
                {
                    user = emailMatches[0];
                }
            }

            if (user == null)
            {
                var phoneMatches = eligibleUsers
                    .Where(item => item.PhoneNumber == normalizedIdentifier)
                    .Take(2)
                    .ToList();
                if (phoneMatches.Count != 1)
                {
                    return null;
                }

                user = phoneMatches[0];
            }

            var normalizedEmail = user.Email.Trim().ToLowerInvariant();
            var payload = JsonSerializer.Serialize(
                new PasswordResetTokenPayload(
                    user.TenantId,
                    user.Username,
                    normalizedEmail,
                    CreatePasswordFingerprint(user)
                )
            );

            return new PasswordResetChallenge(
                user.TenantId,
                user.Username,
                user.Email.Trim(),
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                _protector.Protect(payload, _tokenLifetime)
            );
        }

        public PasswordResetValidationResult Validate(string token)
        {
            var payloadResult = ReadPayload(token);
            if (!payloadResult.Succeeded || payloadResult.Payload == null)
            {
                return new PasswordResetValidationResult(false, payloadResult.Message);
            }

            using var db = _dbContextFactory.CreateDbContext();
            var user = db
                .UserAccounts.IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefault(item =>
                    item.TenantId == payloadResult.Payload.TenantId
                    && item.Username == payloadResult.Payload.Username
                    && item.IsActive
                );
            if (!IsMatchingUser(user, payloadResult.Payload))
            {
                return new PasswordResetValidationResult(
                    false,
                    "رابط الاستعادة غير صالح أو تم استخدامه مسبقاً."
                );
            }

            return new PasswordResetValidationResult(
                true,
                string.Empty,
                user!.TenantId,
                user.Username
            );
        }

        public PasswordResetResult Reset(string token, string newPassword)
        {
            if (!PasswordValidationRules.IsStrongPassword(newPassword))
            {
                return new PasswordResetResult(
                    false,
                    "كلمة المرور يجب أن تكون 8 أحرف على الأقل وتحتوي على حرف كبير وحرف صغير ورقم ورمز خاص."
                );
            }

            var payloadResult = ReadPayload(token);
            if (!payloadResult.Succeeded || payloadResult.Payload == null)
            {
                return new PasswordResetResult(false, payloadResult.Message);
            }

            using var db = _dbContextFactory.CreateDbContext();
            var payload = payloadResult.Payload;
            var user = db.UserAccounts.IgnoreQueryFilters().FirstOrDefault(item =>
                item.TenantId == payload.TenantId
                && item.Username == payload.Username
                && item.IsActive
            );
            if (!IsMatchingUser(user, payload))
            {
                return new PasswordResetResult(
                    false,
                    "رابط الاستعادة غير صالح أو تم استخدامه مسبقاً."
                );
            }

            if (UserAccountService.VerifyPassword(user!, newPassword, out _))
            {
                return new PasswordResetResult(
                    false,
                    "اختر كلمة مرور جديدة مختلفة عن كلمة المرور الحالية."
                );
            }

            UserAccountService.SetPassword(user!, newPassword);
            user!.MustChangePassword = false;
            var activeSessions = db.SessionRecords.IgnoreQueryFilters().Where(session =>
                session.TenantId == user.TenantId && session.Username == user.Username
            );
            db.SessionRecords.RemoveRange(activeSessions);
            db.SaveChanges();

            return new PasswordResetResult(
                true,
                "تم تعيين كلمة المرور الجديدة بنجاح.",
                user.TenantId,
                user.Username,
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName
            );
        }

        public Task<SystemEmailSendResult> SendAsync(
            PasswordResetChallenge challenge,
            string resetUrl,
            CancellationToken cancellationToken = default
        )
        {
            if (!IsDeliveryConfigured)
            {
                return Task.FromResult(
                    new SystemEmailSendResult(false, "بريد الموقع غير مهيأ للإرسال.")
                );
            }

            var safeName = HtmlEncoder.Default.Encode(challenge.DisplayName);
            var safeUrl = HtmlEncoder.Default.Encode(resetUrl);
            var subject = $"استعادة كلمة المرور | {ProductIdentity.DisplayName}";
            var htmlBody =
                $"""
                <!doctype html>
                <html lang="ar" dir="rtl">
                <body style="margin:0;padding:0;background:#f4f6f8;font-family:Tahoma,Arial,sans-serif;color:#172033">
                  <div style="display:none;max-height:0;overflow:hidden">رابط آمن لتعيين كلمة مرور جديدة.</div>
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f6f8">
                    <tr><td align="center" style="padding:32px 16px">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#fff;border:1px solid #e2e7ec;border-radius:10px;overflow:hidden">
                        <tr><td style="padding:22px 28px;background:#101820;color:#fff;font-size:20px;font-weight:700">{ProductIdentity.DisplayName}</td></tr>
                        <tr><td style="padding:32px 28px;text-align:right">
                          <h1 style="margin:0 0 14px;font-size:24px;line-height:1.5;color:#172033">مرحباً {safeName}</h1>
                          <p style="margin:0 0 22px;font-size:16px;line-height:1.9;color:#4b5563">تلقينا طلباً لتعيين كلمة مرور جديدة لحسابك. اضغط الزر التالي لإكمال العملية بأمان.</p>
                          <a href="{safeUrl}" style="display:inline-block;padding:13px 28px;background:#159a8c;color:#fff;text-decoration:none;border-radius:6px;font-size:16px;font-weight:700">تعيين كلمة مرور جديدة</a>
                          <p style="margin:24px 0 0;font-size:13px;line-height:1.8;color:#7b8493">الرابط صالح لمدة {_tokenLifetime.TotalMinutes:0} دقيقة ويُلغى بعد استخدامه. إذا لم تطلب الاستعادة فتجاهل الرسالة، ولن تتغير كلمة مرورك.</p>
                        </td></tr>
                      </table>
                    </td></tr>
                  </table>
                </body>
                </html>
                """;
            var textBody =
                $"مرحباً {challenge.DisplayName}\n\nلتعيين كلمة مرور جديدة افتح الرابط التالي:\n{resetUrl}\n\nالرابط صالح لمدة {_tokenLifetime.TotalMinutes:0} دقيقة ويُلغى بعد استخدامه. إذا لم تطلب الاستعادة فتجاهل الرسالة.";

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var db = _dbContextFactory.CreateDbContext();
                var now = DateTime.UtcNow;
                var tokenFingerprint = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(challenge.Token))
                );
                db.EmailNotificationOutbox.Add(
                    new EmailNotificationOutbox
                    {
                        TenantId = challenge.TenantId,
                        NotificationType = "PasswordReset",
                        ReferenceType = "UserAccount",
                        ReferenceId = challenge.Username,
                        DeduplicationKey = $"password-reset:{tokenFingerprint}",
                        RecipientEmail = challenge.Email,
                        RecipientName = challenge.DisplayName,
                        Subject = subject,
                        HtmlBody = htmlBody,
                        TextBody = textBody,
                        Status = EmailNotificationOutbox.StatusPending,
                        CreatedAtUtc = now,
                        NextAttemptAtUtc = now,
                    }
                );
                db.SaveChanges();
                return Task.FromResult(
                    new SystemEmailSendResult(true, "تمت جدولة رسالة الاستعادة للإرسال.")
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to queue password reset email.");
                return Task.FromResult(
                    new SystemEmailSendResult(false, "تعذر جدولة رسالة الاستعادة.")
                );
            }
        }

        private PayloadReadResult ReadPayload(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new PayloadReadResult(false, "رابط الاستعادة غير صالح.");
            }

            try
            {
                var json = _protector.Unprotect(token, out var expiresAt);
                if (expiresAt <= DateTimeOffset.UtcNow)
                {
                    return new PayloadReadResult(
                        false,
                        "انتهت صلاحية رابط الاستعادة. اطلب رابطاً جديداً."
                    );
                }

                var payload = JsonSerializer.Deserialize<PasswordResetTokenPayload>(json);
                if (
                    payload == null
                    || string.IsNullOrWhiteSpace(payload.TenantId)
                    || string.IsNullOrWhiteSpace(payload.Username)
                    || string.IsNullOrWhiteSpace(payload.Email)
                    || string.IsNullOrWhiteSpace(payload.PasswordFingerprint)
                )
                {
                    return new PayloadReadResult(false, "رابط الاستعادة غير صالح.");
                }

                return new PayloadReadResult(true, string.Empty, payload);
            }
            catch (Exception exception) when (
                exception is CryptographicException or JsonException
            )
            {
                _logger.LogWarning(exception, "Invalid password reset token was submitted.");
                return new PayloadReadResult(
                    false,
                    "رابط الاستعادة غير صالح أو منتهي."
                );
            }
        }

        private static bool IsMatchingUser(
            UserAccount? user,
            PasswordResetTokenPayload payload
        )
        {
            if (
                user == null
                || !user.IsEmailConfirmed
                || !string.Equals(
                    user.Email.Trim(),
                    payload.Email,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            var actualFingerprint = Encoding.UTF8.GetBytes(CreatePasswordFingerprint(user));
            var expectedFingerprint = Encoding.UTF8.GetBytes(payload.PasswordFingerprint);
            return actualFingerprint.Length == expectedFingerprint.Length
                && CryptographicOperations.FixedTimeEquals(
                    actualFingerprint,
                    expectedFingerprint
                );
        }

        private static string CreatePasswordFingerprint(UserAccount user)
        {
            var bytes = SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    $"{user.TenantId}\n{user.Username}\n{user.PasswordHash}\n{user.PasswordSalt}"
                )
            );
            return Convert.ToHexString(bytes);
        }

        private sealed record PasswordResetTokenPayload(
            string TenantId,
            string Username,
            string Email,
            string PasswordFingerprint
        );

        private sealed record PayloadReadResult(
            bool Succeeded,
            string Message,
            PasswordResetTokenPayload? Payload = null
        );
    }
}
