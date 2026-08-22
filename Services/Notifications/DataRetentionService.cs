using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Utilities.Online;

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
        int AuditLogsDeleted,
        int PersonalDataFieldsSanitized = 0
    );

    public sealed class DataRetentionService : IDataRetentionService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;
        private readonly IConfiguration _configuration;

        public DataRetentionService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock,
            IConfiguration configuration
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
            _configuration = configuration;
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
            var personalDataFieldsSanitized = 0;

            foreach (var tenant in tenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (OnlineEditionSettings.HideSensitiveIdentityFields(_configuration))
                {
                    personalDataFieldsSanitized += await SanitizeTenantPersonalDataAsync(
                        db,
                        tenant.TenantId,
                        cancellationToken
                    );
                    personalDataFieldsSanitized += await MinimizeExpiredOperationalDataAsync(
                        db,
                        tenant.TenantId,
                        now,
                        _configuration,
                        cancellationToken
                    );
                }

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
                auditLogsDeleted,
                personalDataFieldsSanitized
            );
        }

        private static async Task<int> SanitizeTenantPersonalDataAsync(
            ApplicationDbContext db,
            string tenantId,
            CancellationToken cancellationToken
        )
        {
            var sanitizedCount = 0;
            var referenceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string ResolveReference(string? value)
            {
                var normalized = (value ?? string.Empty).Trim();
                if (PersonalDataSanitizer.IsInternalReference(normalized))
                {
                    return normalized;
                }

                var key = string.IsNullOrWhiteSpace(normalized)
                    ? Guid.NewGuid().ToString("N")
                    : normalized;
                if (!referenceMap.TryGetValue(key, out var reference))
                {
                    reference = PersonalDataSanitizer.CreateInternalReference();
                    referenceMap[key] = reference;
                }

                return reference;
            }

            var permits = await db.Permits.IgnoreQueryFilters()
                .Where(item => item.TenantId == tenantId && !item.NationalId.StartsWith("REF-"))
                .ToListAsync(cancellationToken);
            foreach (var permit in permits)
            {
                permit.NationalId = ResolveReference(permit.NationalId);
                sanitizedCount++;
            }

            var visits = await db.Visits.IgnoreQueryFilters()
                .Where(item => item.TenantId == tenantId && !item.NationalId.StartsWith("REF-"))
                .ToListAsync(cancellationToken);
            foreach (var visit in visits)
            {
                visit.NationalId = ResolveReference(visit.NationalId);
                sanitizedCount++;
            }

            var profiles = await db.PersonProfiles.IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.NationalId != string.Empty
                    && !item.NationalId.StartsWith("REF-"))
                .ToListAsync(cancellationToken);
            foreach (var profile in profiles)
            {
                profile.NationalId = ResolveReference(profile.NationalId);
                sanitizedCount++;
            }

            var companions = await db.VisitCompanions.IgnoreQueryFilters()
                .Where(item => item.TenantId == tenantId && item.NationalId != string.Empty)
                .ToListAsync(cancellationToken);
            foreach (var companion in companions)
            {
                companion.NationalId = string.Empty;
                sanitizedCount++;
            }

            var permitActivities = await db.PermitActivities.IgnoreQueryFilters()
                .Where(item => item.TenantId == tenantId && item.NationalId != string.Empty)
                .ToListAsync(cancellationToken);
            foreach (var activity in permitActivities)
            {
                activity.NationalId = string.Empty;
                sanitizedCount++;
            }

            var auditLogs = await db.AuditLogs.IgnoreQueryFilters()
                .Where(item => item.TenantId == tenantId)
                .ToListAsync(cancellationToken);
            foreach (var audit in auditLogs)
            {
                var message = PersonalDataSanitizer.SanitizeAuditText(audit.Message);
                var beforeJson = PersonalDataSanitizer.SanitizeAuditJson(audit.BeforeJson);
                var afterJson = PersonalDataSanitizer.SanitizeAuditJson(audit.AfterJson);
                if (message == audit.Message
                    && beforeJson == audit.BeforeJson
                    && afterJson == audit.AfterJson)
                {
                    continue;
                }

                audit.Message = message;
                audit.BeforeJson = beforeJson;
                audit.AfterJson = afterJson;
                sanitizedCount++;
            }

            return sanitizedCount;
        }

        private static async Task<int> MinimizeExpiredOperationalDataAsync(
            ApplicationDbContext db,
            string tenantId,
            DateTime now,
            IConfiguration configuration,
            CancellationToken cancellationToken
        )
        {
            var sanitizedCount = 0;
            var visitCutoff = now.AddDays(
                -OnlineEditionSettings.CompletedVisitPersonalDataRetentionDays(configuration)
            );
            var permitCutoff = now.AddDays(
                -OnlineEditionSettings.ArchivedPermitPersonalDataRetentionDays(configuration)
            );
            var photoCutoff = now.AddDays(
                -OnlineEditionSettings.InactivePersonPhotoRetentionDays(configuration)
            );
            var ipCutoff = now.AddDays(
                -OnlineEditionSettings.SecurityIpRetentionDays(configuration)
            );

            var completedVisits = await db.Visits.IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.ArchivedAt.HasValue
                    && item.ArchivedAt.Value < visitCutoff
                    && (item.VisitorName != "زائر محفوظ"
                        || item.PhoneNumber != string.Empty
                        || item.VisitorEmail != null
                        || item.Purpose != "زيارة مكتملة"
                        || item.HostName != string.Empty
                        || item.VisitedPersonName != string.Empty))
                .ToListAsync(cancellationToken);
            foreach (var visit in completedVisits)
            {
                visit.VisitorName = "زائر محفوظ";
                visit.PhoneNumber = string.Empty;
                visit.VisitorEmail = null;
                visit.Purpose = "زيارة مكتملة";
                visit.HostName = string.Empty;
                visit.VisitedPersonName = string.Empty;
                sanitizedCount++;
            }

            if (completedVisits.Count > 0)
            {
                var visitIds = completedVisits.Select(item => item.VisitId).ToList();
                var companions = await db.VisitCompanions.IgnoreQueryFilters()
                    .Where(item => item.TenantId == tenantId && visitIds.Contains(item.VisitId))
                    .ToListAsync(cancellationToken);
                foreach (var companion in companions)
                {
                    companion.FullName = "مرافق محفوظ";
                    companion.NationalId = string.Empty;
                    companion.PhoneNumber = string.Empty;
                    companion.Relationship = string.Empty;
                    sanitizedCount++;
                }
            }

            var archivedPermits = await db.Permits.IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.ArchivedAt.HasValue
                    && item.ArchivedAt.Value < permitCutoff
                    && (item.DriverName != "حامل تصريح محفوظ"
                        || item.EmployeePhone != string.Empty
                        || item.HolderEmail != null
                        || item.PlateNumber != string.Empty
                        || item.ManagerName != string.Empty
                        || item.OfficerName != string.Empty))
                .ToListAsync(cancellationToken);
            foreach (var permit in archivedPermits)
            {
                permit.DriverName = "حامل تصريح محفوظ";
                permit.EmployeePhone = string.Empty;
                permit.HolderEmail = null;
                permit.PlateNumber = string.Empty;
                permit.ManagerName = string.Empty;
                permit.OfficerName = string.Empty;
                sanitizedCount++;
            }

            var inactiveProfiles = await db.PersonProfiles.IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId
                    && !item.IsActive
                    && item.UpdatedAtUtc < photoCutoff)
                .ToListAsync(cancellationToken);
            if (inactiveProfiles.Count > 0)
            {
                db.PersonProfiles.RemoveRange(inactiveProfiles);
                sanitizedCount += inactiveProfiles.Count;
            }

            var oldActivityIps = await db.PermitActivities.IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.OccurredAt < ipCutoff
                    && item.IpAddress != string.Empty)
                .ToListAsync(cancellationToken);
            foreach (var activity in oldActivityIps)
            {
                activity.IpAddress = string.Empty;
                sanitizedCount++;
            }

            var staleDevices = await db.DisplayDevices.IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.LastSeenUtc.HasValue
                    && item.LastSeenUtc.Value < ipCutoff
                    && (item.IpAddress != string.Empty || item.LastIpAddress != string.Empty))
                .ToListAsync(cancellationToken);
            foreach (var device in staleDevices)
            {
                device.IpAddress = string.Empty;
                device.LastIpAddress = string.Empty;
                sanitizedCount++;
            }

            return sanitizedCount;
        }
    }
}
