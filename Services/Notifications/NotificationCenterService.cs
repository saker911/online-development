using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Notifications;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Services.Notifications
{
    public sealed class NotificationCenterService : INotificationCenterService
    {
        private static readonly HashSet<string> SecurityActionTypes = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            "SecurityViolation",
            "NoReturnViolation",
            "PendingUnauthorizedExit",
            "UnauthorizedExitNeedsReview",
            "UnauthorizedExitStopped",
            "UnauthorizedExitConfirmed",
            "ExitUnauthorized",
            "QrConcurrentGateUse",
        };

        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IReportsDashboardService _reportsDashboardService;
        private readonly IPermitService _permitService;
        private readonly IVisitService _visitService;
        private readonly IUserAdminService _userAdminService;
        private readonly IAccessControlService _accessControlService;
        private readonly ISystemClock _systemClock;

        public NotificationCenterService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IReportsDashboardService reportsDashboardService,
            IPermitService permitService,
            IVisitService visitService,
            IUserAdminService userAdminService,
            IAccessControlService accessControlService,
            ISystemClock systemClock
        )
        {
            _dbContextFactory = dbContextFactory;
            _reportsDashboardService = reportsDashboardService;
            _permitService = permitService;
            _visitService = visitService;
            _userAdminService = userAdminService;
            _accessControlService = accessControlService;
            _systemClock = systemClock;
        }

        public NotificationCenterPageViewModel GetCenter(
            ClaimsPrincipal principal,
            string? category = null,
            string? state = null,
            int limit = 60
        )
        {
            var username = NormalizeUsername(principal.Identity?.Name);
            if (string.IsNullOrWhiteSpace(username))
            {
                return new NotificationCenterPageViewModel { IsEnabled = false };
            }

            using var db = _dbContextFactory.CreateDbContext();
            var tenant = GetCurrentTenant(db);
            if (tenant == null || !tenant.NotificationCenterEnabled)
            {
                return new NotificationCenterPageViewModel
                {
                    IsEnabled = false,
                    CanManageSettings = principal.HasPermission(AppPermissions.ManageAdministration),
                };
            }

            Synchronize(db, tenant, principal, username);

            var normalizedCategory = NormalizeCategory(category);
            var normalizedState = NormalizeState(state);
            var query = db
                .InAppNotifications.AsNoTracking()
                .Where(item =>
                    item.RecipientUsername == username && item.DismissedAtUtc == null
                );

            var totalCount = query.Count();
            var unreadCount = query.Count(item => item.ReadAtUtc == null);
            if (!string.IsNullOrWhiteSpace(normalizedCategory))
            {
                query = query.Where(item => item.Category == normalizedCategory);
            }
            if (normalizedState == "unread")
            {
                query = query.Where(item => item.ReadAtUtc == null);
            }
            else if (normalizedState == "read")
            {
                query = query.Where(item => item.ReadAtUtc != null);
            }

            return new NotificationCenterPageViewModel
            {
                Items = query
                    .OrderByDescending(item => item.OccurredAtUtc)
                    .ThenByDescending(item => item.Id)
                    .Take(Math.Clamp(limit, 1, 100))
                    .Select(item => new NotificationRowViewModel
                    {
                        Id = item.Id,
                        Category = item.Category,
                        Severity = item.Severity,
                        Title = item.Title,
                        Message = item.Message,
                        ActionUrl = item.ActionUrl,
                        OccurredAtUtc = item.OccurredAtUtc,
                        IsRead = item.ReadAtUtc != null,
                    })
                    .ToList(),
                TotalCount = totalCount,
                UnreadCount = unreadCount,
                Category = normalizedCategory,
                State = normalizedState,
                IsEnabled = true,
                CanManageSettings = principal.HasPermission(AppPermissions.ManageAdministration),
            };
        }

        public bool MarkRead(long id, string username) =>
            UpdateNotification(id, username, notification =>
            {
                notification.ReadAtUtc ??= _systemClock.UtcNow;
            });

        public int MarkAllRead(string username)
        {
            var normalizedUsername = NormalizeUsername(username);
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                return 0;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var items = db
                .InAppNotifications.Where(item =>
                    item.RecipientUsername == normalizedUsername
                    && item.DismissedAtUtc == null
                    && item.ReadAtUtc == null
                )
                .ToList();
            var now = _systemClock.UtcNow;
            items.ForEach(item => item.ReadAtUtc = now);
            db.SaveChanges();
            return items.Count;
        }

        public bool Dismiss(long id, string username) =>
            UpdateNotification(id, username, notification =>
            {
                var now = _systemClock.UtcNow;
                notification.ReadAtUtc ??= now;
                notification.DismissedAtUtc = now;
            });

        public NotificationSettingsViewModel GetSettings()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var tenant = GetCurrentTenant(db) ?? new Tenant();
            return MapSettings(tenant);
        }

        public void UpdateSettings(NotificationSettingsViewModel model, string username)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var tenant = GetCurrentTenant(db)
                ?? throw new InvalidOperationException("تعذر العثور على إعدادات الجهة الحالية.");

            tenant.NotificationCenterEnabled = model.NotificationCenterEnabled;
            tenant.PermitNotificationsEnabled = model.PermitNotificationsEnabled;
            tenant.VisitNotificationsEnabled = model.VisitNotificationsEnabled;
            tenant.SecurityAlertsEnabled = model.SecurityAlertsEnabled;
            tenant.FailedOperationAlertsEnabled = model.FailedOperationAlertsEnabled;
            tenant.UnauthorizedMovementAlertsEnabled = model.UnauthorizedMovementAlertsEnabled;
            tenant.NotificationRetentionDays = Math.Clamp(model.NotificationRetentionDays, 7, 730);
            tenant.EmailOutboxRetentionDays = Math.Clamp(model.EmailOutboxRetentionDays, 7, 365);
            tenant.AuditLogRetentionDays = Math.Clamp(model.AuditLogRetentionDays, 90, 2555);

            db.AuditLogs.Add(
                new AuditLog
                {
                    Username = NormalizeUsername(username),
                    ActionType = "NotificationSettingsUpdated",
                    ActionLabel = "تحديث إعدادات الإشعارات",
                    EntityType = "Tenant",
                    EntityId = tenant.TenantId,
                    Message = "تم تحديث مصادر التنبيه ومدد الاحتفاظ بالبيانات.",
                    Source = nameof(NotificationCenterService),
                    RecordedBy = NormalizeUsername(username),
                    ActualActorUsername = NormalizeUsername(username),
                    OccurredAt = _systemClock.UtcNow,
                    Success = true,
                }
            );
            db.SaveChanges();
        }

        private void Synchronize(
            ApplicationDbContext db,
            Tenant tenant,
            ClaimsPrincipal principal,
            string username
        )
        {
            var drafts = new List<NotificationDraft>();
            var canApprovePermits = principal.HasPermission(AppPermissions.ApprovePermit);
            var canViewSecurity = principal.HasPermission(AppPermissions.ReviewUnauthorizedExit)
                || principal.HasPermission(AppPermissions.StopPermit)
                || canApprovePermits;

            if (
                (tenant.PermitNotificationsEnabled && canApprovePermits)
                || (tenant.SecurityAlertsEnabled && canViewSecurity)
            )
            {
                var report = _reportsDashboardService.BuildNotificationCenterModel(8, username);
                foreach (var section in report.Sections)
                {
                    var isPermit = string.Equals(
                        section.Key,
                        "PendingPermits",
                        StringComparison.OrdinalIgnoreCase
                    );
                    if (isPermit && (!tenant.PermitNotificationsEnabled || !canApprovePermits))
                    {
                        continue;
                    }
                    if (!isPermit && (!tenant.SecurityAlertsEnabled || !canViewSecurity))
                    {
                        continue;
                    }

                    foreach (var item in section.Items)
                    {
                        var reference = string.IsNullOrWhiteSpace(item.ReferenceId)
                            ? item.PermitNumber
                            : item.ReferenceId;
                        drafts.Add(
                            new NotificationDraft(
                                isPermit ? NotificationCategories.Permit : NotificationCategories.Security,
                                isPermit ? NotificationSeverities.Info : NotificationSeverities.Warning,
                                isPermit ? "طلب تصريح بانتظار الاعتماد" : section.Title,
                                JoinMessage(item.DriverName, item.Subtitle, item.Summary),
                                isPermit
                                    ? $"/Permits/Details/{Uri.EscapeDataString(item.PermitNumber)}?focusActions=true"
                                    : $"/Reports/PermitActivity?activityQuery={Uri.EscapeDataString(item.PermitNumber)}&selectedSequenceId={Uri.EscapeDataString(item.SequenceId)}",
                                $"report:{section.Key}:{reference}",
                                _systemClock.UtcNow
                            )
                        );
                    }
                }
            }

            if (tenant.VisitNotificationsEnabled)
            {
                var currentUser = _userAdminService.GetUserAccount(username);
                foreach (
                    var visit in _visitService
                        .GetAllVisits()
                        .Where(visit =>
                            (
                                string.Equals(visit.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                                && _accessControlService.CanApproveVisit(visit, currentUser)
                            )
                            || (
                                string.Equals(visit.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
                                && _accessControlService.CanAccessVisit(visit, currentUser)
                            )
                        )
                        .OrderByDescending(visit => visit.RequestedAtUtc ?? visit.VisitDate)
                        .Take(12)
                )
                {
                    drafts.Add(
                        new NotificationDraft(
                            NotificationCategories.Visit,
                            string.Equals(visit.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
                                ? NotificationSeverities.Warning
                                : NotificationSeverities.Info,
                            string.Equals(visit.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
                                ? "زيارة موقوفة تحتاج مراجعة"
                                : "طلب زيارة بانتظار القرار",
                            JoinMessage(visit.VisitorName, visit.VisitLocation, visit.ApprovalStatusDisplay),
                            $"/Visits/Details/{Uri.EscapeDataString(visit.VisitId)}",
                            $"visit:{visit.VisitId}:{visit.ApprovalStatus}:{visit.Status}",
                            visit.RequestedAtUtc ?? visit.VisitDate
                        )
                    );
                }
            }

            if (tenant.SecurityAlertsEnabled && canViewSecurity)
            {
                var since = _systemClock.UtcNow.AddDays(-7);
                var logs = db
                    .AuditLogs.AsNoTracking()
                    .Where(log => log.OccurredAt >= since)
                    .OrderByDescending(log => log.OccurredAt)
                    .Take(200)
                    .ToList()
                    .Where(log =>
                        (tenant.FailedOperationAlertsEnabled && !log.Success)
                        || (
                            tenant.UnauthorizedMovementAlertsEnabled
                            && SecurityActionTypes.Contains(log.ActionType)
                        )
                    )
                    .Take(20);

                foreach (var log in logs)
                {
                    drafts.Add(
                        new NotificationDraft(
                            NotificationCategories.Security,
                            log.Success ? NotificationSeverities.Warning : NotificationSeverities.Critical,
                            string.IsNullOrWhiteSpace(log.ActionLabel) ? "تنبيه أمني" : log.ActionLabel,
                            log.Message,
                            BuildAuditActionUrl(log),
                            $"audit:{log.Id}",
                            log.OccurredAt
                        )
                    );
                }
            }

            UpsertDrafts(db, username, drafts);
        }

        private void UpsertDrafts(
            ApplicationDbContext db,
            string username,
            IReadOnlyCollection<NotificationDraft> drafts
        )
        {
            if (drafts.Count == 0)
            {
                return;
            }

            var keys = drafts.Select(item => item.SourceKey).Distinct().ToList();
            var existing = db
                .InAppNotifications.Where(item =>
                    item.RecipientUsername == username && keys.Contains(item.SourceKey)
                )
                .ToDictionary(item => item.SourceKey, StringComparer.OrdinalIgnoreCase);
            var now = _systemClock.UtcNow;

            foreach (var draft in drafts.DistinctBy(item => item.SourceKey))
            {
                if (existing.TryGetValue(draft.SourceKey, out var notification))
                {
                    notification.Title = draft.Title;
                    notification.Message = draft.Message;
                    notification.ActionUrl = NormalizeLocalUrl(draft.ActionUrl);
                    notification.Severity = draft.Severity;
                    notification.OccurredAtUtc = draft.OccurredAtUtc;
                    continue;
                }

                db.InAppNotifications.Add(
                    new InAppNotification
                    {
                        RecipientUsername = username,
                        Category = draft.Category,
                        Severity = draft.Severity,
                        Title = draft.Title,
                        Message = draft.Message,
                        ActionUrl = NormalizeLocalUrl(draft.ActionUrl),
                        SourceKey = draft.SourceKey,
                        OccurredAtUtc = draft.OccurredAtUtc,
                        CreatedAtUtc = now,
                    }
                );
            }

            db.SaveChanges();
        }

        private bool UpdateNotification(long id, string username, Action<InAppNotification> update)
        {
            var normalizedUsername = NormalizeUsername(username);
            using var db = _dbContextFactory.CreateDbContext();
            var notification = db.InAppNotifications.FirstOrDefault(item =>
                item.Id == id && item.RecipientUsername == normalizedUsername
            );
            if (notification == null)
            {
                return false;
            }
            update(notification);
            db.SaveChanges();
            return true;
        }

        private static Tenant? GetCurrentTenant(ApplicationDbContext db) =>
            db.Tenants.FirstOrDefault(tenant => tenant.TenantId == db.CurrentTenantId);

        private static NotificationSettingsViewModel MapSettings(Tenant tenant) =>
            new()
            {
                NotificationCenterEnabled = tenant.NotificationCenterEnabled,
                PermitNotificationsEnabled = tenant.PermitNotificationsEnabled,
                VisitNotificationsEnabled = tenant.VisitNotificationsEnabled,
                SecurityAlertsEnabled = tenant.SecurityAlertsEnabled,
                FailedOperationAlertsEnabled = tenant.FailedOperationAlertsEnabled,
                UnauthorizedMovementAlertsEnabled = tenant.UnauthorizedMovementAlertsEnabled,
                NotificationRetentionDays = tenant.NotificationRetentionDays,
                EmailOutboxRetentionDays = tenant.EmailOutboxRetentionDays,
                AuditLogRetentionDays = tenant.AuditLogRetentionDays,
                LastRetentionRunAtUtc = tenant.LastRetentionRunAtUtc,
            };

        private static string NormalizeUsername(string? username) =>
            (username ?? string.Empty).Trim().ToLowerInvariant();

        private static string NormalizeCategory(string? category) =>
            category?.Trim().ToLowerInvariant() switch
            {
                "permit" => NotificationCategories.Permit,
                "visit" => NotificationCategories.Visit,
                "security" => NotificationCategories.Security,
                "system" => NotificationCategories.System,
                _ => string.Empty,
            };

        private static string NormalizeState(string? state) =>
            state?.Trim().ToLowerInvariant() switch
            {
                "read" => "read",
                "unread" => "unread",
                _ => "all",
            };

        private static string NormalizeLocalUrl(string? value)
        {
            var url = (value ?? string.Empty).Trim();
            return url.StartsWith("/", StringComparison.Ordinal)
                && !url.StartsWith("//", StringComparison.Ordinal)
                ? url
                : string.Empty;
        }

        private static string JoinMessage(params string?[] values) =>
            string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));

        private static string BuildAuditActionUrl(AuditLog log)
        {
            if (string.Equals(log.EntityType, "Permit", StringComparison.OrdinalIgnoreCase))
            {
                return $"/Reports/PermitActivity?activityQuery={Uri.EscapeDataString(log.EntityId)}";
            }
            if (string.Equals(log.EntityType, "Visit", StringComparison.OrdinalIgnoreCase))
            {
                return $"/Visits/Details/{Uri.EscapeDataString(log.EntityId)}";
            }
            return "/Monitoring";
        }

        private sealed record NotificationDraft(
            string Category,
            string Severity,
            string Title,
            string Message,
            string ActionUrl,
            string SourceKey,
            DateTime OccurredAtUtc
        );
    }
}
