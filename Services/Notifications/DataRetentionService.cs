using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Common;

namespace VehiclePermitSystemWeb.Services.Notifications
{
    public interface IDataRetentionService
    {
        Task<DataRetentionResult> RunAsync(CancellationToken cancellationToken = default);
    }

    public sealed record DataRetentionResult(
        int TenantCount,
        int NotificationsDeleted,
        int EmailRecordsDeleted,
        int AuditLogsDeleted
    );

    public sealed class DataRetentionService : IDataRetentionService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;

        public DataRetentionService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
        }

        public async Task<DataRetentionResult> RunAsync(
            CancellationToken cancellationToken = default
        )
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var tenants = await db
                .Tenants.IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
            var now = _systemClock.UtcNow;
            var notificationsDeleted = 0;
            var emailRecordsDeleted = 0;
            var auditLogsDeleted = 0;

            foreach (var tenant in tenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var notificationCutoff = now.AddDays(
                    -Math.Clamp(tenant.NotificationRetentionDays, 7, 730)
                );
                var emailCutoff = now.AddDays(
                    -Math.Clamp(tenant.EmailOutboxRetentionDays, 7, 365)
                );
                var auditCutoff = now.AddDays(
                    -Math.Clamp(tenant.AuditLogRetentionDays, 90, 2555)
                );

                var notifications = await db
                    .InAppNotifications.IgnoreQueryFilters()
                    .Where(item =>
                        item.TenantId == tenant.TenantId
                        && item.CreatedAtUtc < notificationCutoff
                    )
                    .ToListAsync(cancellationToken);
                db.InAppNotifications.RemoveRange(notifications);
                notificationsDeleted += notifications.Count;

                var emailRecords = await db
                    .EmailNotificationOutbox.IgnoreQueryFilters()
                    .Where(item =>
                        item.TenantId == tenant.TenantId
                        && item.CreatedAtUtc < emailCutoff
                        && (
                            item.Status == EmailNotificationOutbox.StatusSent
                            || item.Status == EmailNotificationOutbox.StatusFailed
                        )
                    )
                    .ToListAsync(cancellationToken);
                db.EmailNotificationOutbox.RemoveRange(emailRecords);
                emailRecordsDeleted += emailRecords.Count;

                var auditLogs = await db
                    .AuditLogs.IgnoreQueryFilters()
                    .Where(item =>
                        item.TenantId == tenant.TenantId && item.OccurredAt < auditCutoff
                    )
                    .ToListAsync(cancellationToken);
                db.AuditLogs.RemoveRange(auditLogs);
                auditLogsDeleted += auditLogs.Count;
                tenant.LastRetentionRunAtUtc = now;

                await db.SaveChangesAsync(cancellationToken);
            }

            return new DataRetentionResult(
                tenants.Count,
                notificationsDeleted,
                emailRecordsDeleted,
                auditLogsDeleted
            );
        }
    }
}
