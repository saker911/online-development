using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.DTOs;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Account;
using VehiclePermitSystemWeb.Models.ViewModels.Backup;
using VehiclePermitSystemWeb.Models.ViewModels.Delegations;
using VehiclePermitSystemWeb.Models.ViewModels.Departments;
using VehiclePermitSystemWeb.Models.ViewModels.Display;
using VehiclePermitSystemWeb.Models.ViewModels.Permits;
using VehiclePermitSystemWeb.Models.ViewModels.Reports;
using VehiclePermitSystemWeb.Models.ViewModels.Scan;
using VehiclePermitSystemWeb.Models.ViewModels.Users;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;
using VehiclePermitSystemWeb.Security;

namespace VehiclePermitSystemWeb.Services.Permits
{
    public sealed class PermitService : IPermitService
    {
        private const string PermitCurrentStateInside = "Inside";
        private const string PermitCurrentStateOutside = "Outside";
        private static readonly TimeSpan ShortCacheWindow = TimeSpan.FromSeconds(5);

        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;
        private readonly ISystemClock _systemClock;
        private readonly ILeavePolicyService _leavePolicyService;
        private readonly IPermitMovementService _permitMovementService;
        private readonly IPermitMonitoringService _permitMonitoringService;
        private readonly IPermitApprovalService _permitApprovalService;
        private readonly IPermitLifecycleService _permitLifecycleService;
        private readonly IPermitAuditService _permitAuditService;
        private readonly IAccessControlService _accessControl;
        private readonly IDelegationService _delegationService;

        public PermitService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IConfiguration configuration,
            IMemoryCache memoryCache,
            ISystemClock systemClock,
            ILeavePolicyService leavePolicyService,
            IPermitMovementService permitMovementService,
            IPermitMonitoringService permitMonitoringService,
            IPermitApprovalService permitApprovalService,
            IPermitLifecycleService permitLifecycleService,
            IPermitAuditService permitAuditService,
            IAccessControlService accessControl,
            IDelegationService delegationService
        )
        {
            _dbContextFactory = dbContextFactory;
            _configuration = configuration;
            _memoryCache = memoryCache;
            _systemClock = systemClock;
            _leavePolicyService = leavePolicyService;
            _permitMovementService = permitMovementService;
            _permitMonitoringService = permitMonitoringService;
            _permitApprovalService = permitApprovalService;
            _permitLifecycleService = permitLifecycleService;
            _permitAuditService = permitAuditService;
            _accessControl = accessControl;
            _delegationService = delegationService;
        }

        private sealed record WorkHoursSettings(
            TimeOnly StartTime,
            TimeOnly EndTime,
            TimeSpan AttendanceGrace,
            TimeSpan WorkEndExitGrace,
            TimeSpan LateReturnGrace,
            string OfficialWorkDaysCsv
        );

        public IEnumerable<Permit> GetPendingPermits(string? username = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizePermitStates(db);
            var user = ResolveUserAccount(db, username);
            var permits = ApplyPendingApprovalScope(
                db.Permits.AsNoTracking()
                    .Where(p => p.ApprovalStatus == "Pending" && !p.ArchivedAt.HasValue),
                user
            );
            return permits.OrderBy(p => p.ExpiresAt).ToList();
        }

        public IEnumerable<Permit> GetAllPermits(string? username = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizePermitStates(db);
            var user = ResolveUserAccount(db, username);
            var permits = ApplyPermitDepartmentScope(db.Permits.AsNoTracking(), user);
            return permits.OrderByDescending(p => p.ExpiresAt).ToList();
        }

        public IEnumerable<Permit> GetVisiblePermits(string? username = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizePermitStates(db);
            var user = ResolveUserAccount(db, username);
            var permits = db.Permits.AsNoTracking().Where(p => !p.ArchivedAt.HasValue);
            permits = ApplyPermitDepartmentScope(permits, user);
            return permits.OrderByDescending(p => p.ExpiresAt).ToList();
        }

        public IEnumerable<Permit> GetApprovedPermitsForDisplay()
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizePermitStates(db);
            return db
                .Permits.AsNoTracking()
                .Where(p => !p.ArchivedAt.HasValue)
                .AsEnumerable()
                .Where(p =>
                    string.Equals(p.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase)
                )
                .OrderByDescending(p => p.PermitDate ?? p.ExpiresAt ?? DateTime.MinValue)
                .ThenByDescending(p => p.ExpiresAt)
                .ToList();
        }

        public IEnumerable<Permit> GetEmployeesOutForDisplay()
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizePermitStates(db);
            return db
                .Permits.AsNoTracking()
                .Where(p =>
                    p.ApprovalStatus == "Out"
                    && p.OutTime != null
                    && p.ReturnTime == null
                    && !p.ArchivedAt.HasValue
                )
                .OrderByDescending(p => p.OutTime)
                .ToList();
        }

        public IEnumerable<PermitActivity> GetRecentPermitActivities(
            int take = 20,
            string? username = null
        )
        {
            var cacheKey = GetRecentActivitiesCacheKey(take, username);
            if (_memoryCache.TryGetValue(cacheKey, out List<PermitActivity>? cachedActivities))
            {
                return cachedActivities
                    ?? (IEnumerable<PermitActivity>)Array.Empty<PermitActivity>();
            }

            using var db = _dbContextFactory.CreateDbContext();
            var scopedPermitNumbers = GetVisiblePermits(username)
                .Select(permit => permit.PermitNumber)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var activities = db
                .PermitActivities.AsNoTracking()
                .Where(activity => scopedPermitNumbers.Contains(activity.PermitNumber))
                .OrderByDescending(x => x.OccurredAt)
                .Take(take)
                .ToList();

            _memoryCache.Set(cacheKey, activities, ShortCacheWindow);
            if (activities.Count > 0)
            {
                _memoryCache.Set(GetLatestActivityCacheKey(), activities[0], ShortCacheWindow);
            }

            return activities;
        }

        public PermitActivity? GetLatestPermitActivity()
        {
            if (
                _memoryCache.TryGetValue(
                    GetLatestActivityCacheKey(),
                    out PermitActivity? cachedActivity
                )
            )
            {
                return cachedActivity;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var activity = db
                .PermitActivities.AsNoTracking()
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();

            if (activity != null)
            {
                _memoryCache.Set(GetLatestActivityCacheKey(), activity, ShortCacheWindow);
            }

            return activity;
        }

        public IEnumerable<PermitActivity> GetRecentPermitActivitiesForDevice(
            string deviceId,
            int take = 20,
            string? username = null
        )
        {
            var normalizedDeviceId = (deviceId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedDeviceId))
            {
                return Array.Empty<PermitActivity>();
            }

            using var db = _dbContextFactory.CreateDbContext();
            var scopedPermitNumbers = GetVisiblePermits(username)
                .Select(permit => permit.PermitNumber)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return db.PermitActivities.AsNoTracking()
                .Where(activity =>
                    activity.DeviceId == normalizedDeviceId
                    && scopedPermitNumbers.Contains(activity.PermitNumber)
                )
                .OrderByDescending(activity => activity.OccurredAt)
                .ThenByDescending(activity => activity.Id)
                .Take(Math.Clamp(take, 1, 100))
                .ToList();
        }

        public IEnumerable<PermitActivity> GetPermitActivities(
            string permitNumber,
            string? username = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var permit = GetPermitByNumber(permitNumber, username);
            if (permit == null)
            {
                return Array.Empty<PermitActivity>();
            }

            return db
                .PermitActivities.AsNoTracking()
                .Where(x => x.PermitNumber == permitNumber)
                .OrderByDescending(x => x.OccurredAt)
                .ToList();
        }

        public IEnumerable<PermitActivity> GetSequencedPermitActivities(string? username = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var scopedPermitNumbers = GetAllPermits(username)
                .Select(permit => permit.PermitNumber)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return db
                .PermitActivities.AsNoTracking()
                .Where(activity =>
                    !string.IsNullOrWhiteSpace(activity.SequenceId)
                    && scopedPermitNumbers.Contains(activity.PermitNumber)
                )
                .OrderByDescending(activity => activity.OccurredAt)
                .ThenByDescending(activity => activity.Id)
                .ToList();
        }

        public List<PermitConflictViewModel> FindPermitConflicts(
            Permit candidate,
            string? excludePermitNumber = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizePermitStates(db);

            var candidateName = NormalizeComparisonValue(candidate.DriverName);
            var candidateNationalId = NormalizeComparisonValue(candidate.NationalId);
            var candidatePlateNumber = NormalizePlateComparisonValue(candidate.PlateNumber);

            return db
                .Permits.AsNoTracking()
                .AsEnumerable()
                .Where(permit =>
                    permit.ArchivedAt == null
                    && !string.Equals(
                        permit.PermitNumber,
                        excludePermitNumber,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Select(permit => new
                {
                    Permit = permit,
                    Reasons = BuildPermitConflictReasons(
                        candidateName,
                        candidateNationalId,
                        candidatePlateNumber,
                        permit
                    ),
                })
                .Where(item => item.Reasons.Count > 0)
                .Select(item => new PermitConflictViewModel
                {
                    PermitNumber = item.Permit.PermitNumber,
                    DriverName = item.Permit.DriverName,
                    NationalId = item.Permit.NationalId,
                    PlateNumber = item.Permit.PlateNumber,
                    PermitTypeDisplay = item.Permit.PermitTypeDisplay,
                    ApprovalStatusDisplay = item.Permit.ApprovalStatusDisplay,
                    Reasons = item.Reasons,
                })
                .OrderBy(item => item.PermitNumber)
                .ToList();
        }

        public Permit? GetPermitByNumber(string permitNumber, string? username = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizePermitStates(db);
            var user = ResolveUserAccount(db, username);
            var lookupKeys = BuildPermitLookupKeys(permitNumber);
            var permit = db
                .Permits.AsNoTracking()
                .FirstOrDefault(p => lookupKeys.Contains(p.PermitNumber));

            if (permit == null || !_accessControl.CanAccessPermit(permit, user))
            {
                return string.IsNullOrWhiteSpace(username) ? permit : null;
            }

            return permit;
        }

        public void AddPermit(Permit permit, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var workHours = GetWorkHoursSettings(db);
            permit.PermitNumber = GenerateNextPermitNumber(db);
            permit.ApprovalStatus = permit.IsVisitorPermit
                ? Permit.ApprovalStatusPending
                : Permit.ApprovalStatusPendingReview;
            permit.CurrentState = PermitCurrentStateOutside;
            permit.UnauthorizedExitWarningCount = 0;
            permit.LastUnauthorizedExitWarningAt = null;
            permit.PermitType = string.IsNullOrWhiteSpace(permit.PermitType)
                ? Permit.PermitTypeVisitor
                : permit.PermitType;
            permit.NormalizeAccessModeState();
            permit.VisitLocation = permit.VisitLocation?.Trim() ?? string.Empty;
            if (permit.IsVisitorPermit && !permit.PermitDate.HasValue)
            {
                permit.PermitDate = _systemClock.LocalNow;
            }
            EnsureVisitorPermitExpiration(
                permit,
                permit.PermitDate ?? _systemClock.LocalNow,
                workHours
            );
            permit.ExpectedReturnTime = null;
            permit.LeaveWindowStartAt = null;
            permit.LeaveWindowEndAt = null;
            permit.ArchivedAt = null;
            // record creator if provided
            permit.CreatedBy = performedBy ?? string.Empty;
            permit.QrToken = GenerateQrToken(permit);
            db.Permits.Add(permit);
            db.SaveChanges();
        }

        public void UpdatePermit(Permit permit, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var workHours = GetWorkHoursSettings(db);
            var existing = db.Permits.FirstOrDefault(p => p.PermitNumber == permit.PermitNumber);
            if (existing == null)
            {
                return;
            }

            var preserveLifecycleState = IsTerminalPermit(existing);
            var requestedExpiry = permit.ExpiresAt;

            existing.DriverName = permit.DriverName;
            existing.NationalId = permit.NationalId;
            existing.PermitType = string.IsNullOrWhiteSpace(permit.PermitType)
                ? Permit.PermitTypeTemporary
                : permit.PermitType;
            existing.VehicleType = permit.VehicleType;
            existing.PlateNumber = permit.PlateNumber;
            existing.Subject = permit.Subject;
            existing.AuthorizingEntity = permit.AuthorizingEntity;
            existing.DepartmentName = permit.DepartmentName;
            existing.VisitLocation = permit.VisitLocation;
            existing.OfficerName = permit.OfficerName;
            existing.ManagerName = permit.ManagerName;
            existing.EmployeeNumber = permit.EmployeeNumber;
            existing.EmployeeDepartment = permit.EmployeeDepartment;
            existing.JobTitle = permit.JobTitle;
            existing.EmployeePhone = permit.EmployeePhone;
            existing.HolderEmail = permit.HolderEmail;
            existing.RequiresReturn = permit.RequiresReturn;
            existing.AccessMode = permit.AccessMode;
            existing.NormalizeAccessModeState();
            if (requestedExpiry.HasValue)
            {
                existing.ExpiresAt = requestedExpiry;
            }
            existing.ExpectedReturnTime = permit.ExpectedReturnTime;
            existing.LeaveWindowStartAt = null;
            existing.LeaveWindowEndAt = null;
            existing.PendingExitRequest = false;
            existing.LeaveReason = string.Empty;
            existing.TemporaryExitPermissionUntil = null;
            existing.UnauthorizedExitWarningCount = 0;
            existing.LastUnauthorizedExitWarningAt = null;
            if (existing.IsFullAccessPermit)
            {
                existing.PendingUnauthorizedExitAt = null;
                existing.PendingUnauthorizedExitSequenceId = string.Empty;
                existing.LateReturnWarningCount = 0;
                existing.LastLateReturnWarningForExpectedReturnTime = null;
            }
            EnsureVisitorPermitExpiration(
                existing,
                existing.PermitDate ?? _systemClock.LocalNow,
                workHours
            );
            var canRenewFromEdit =
                preserveLifecycleState
                && requestedExpiry.HasValue
                && requestedExpiry.Value > _systemClock.LocalNow;

            if (canRenewFromEdit)
            {
                existing.ApprovalStatus = "Approved";
                existing.ArchivedAt = null;
                existing.OutTime = null;
                existing.ReturnTime = null;
                existing.CurrentState = PermitCurrentStateOutside;
                existing.LastAutomaticWorkEndExitAt = null;
                existing.QrToken = GenerateQrToken(existing);
            }
            else if (!preserveLifecycleState)
            {
                existing.ApprovalStatus = existing.IsVisitorPermit
                    ? Permit.ApprovalStatusPending
                    : Permit.ApprovalStatusPendingReview;
                existing.QrToken = GenerateQrToken(existing);
            }
            db.SaveChanges();
        }

        public void DeletePermit(string permitNumber, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var permit = db.Permits.FirstOrDefault(p => p.PermitNumber == permitNumber);
            if (permit == null)
            {
                return;
            }

            // Archive the permit instead of deleting
            permit.ArchivedAt = _systemClock.LocalNow;
            permit.ApprovalStatus = "Archived";

            // Record permit activity and user activity for audit
            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "Archive",
                "أرشفة تصريح",
                $"تمت أرشفة التصريح {permit.PermitNumber}.",
                "PermitsController",
                performedBy,
                _systemClock.LocalNow
            );

            if (!string.IsNullOrWhiteSpace(performedBy))
            {
                _permitAuditService.RecordUserActivity(
                    db,
                    permit.PermitNumber,
                    permit.DriverName,
                    "Archive",
                    "أرشفة تصريح",
                    $"المستخدم {performedBy} قام بأرشفة التصريح {permit.PermitNumber}.",
                    "PermitsController",
                    performedBy,
                    _systemClock.LocalNow
                );
            }

            db.SaveChanges();
        }

        public void CancelExpiredPermits(string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizePermitStates(db);
            var expired = db
                .Permits.Where(p =>
                    p.ExpiresAt.HasValue
                    && p.ExpiresAt.Value <= _systemClock.LocalNow
                    && !p.ArchivedAt.HasValue
                )
                .ToList();

            foreach (var permit in expired)
            {
                permit.ApprovalStatus = "Expired";
                permit.ArchivedAt = permit.ExpiresAt ?? _systemClock.LocalNow;
            }

            db.SaveChanges();
        }

        public bool TryValidatePermitQrToken(
            string token,
            out Permit? permit,
            out string status,
            out string message
        )
        {
            permit = null;
            status = "invalid";
            message = "رمز التحقق غير صالح.";

            if (!PermitQrTokenGenerator.IsCurrentVersion(token))
            {
                return false;
            }

            using var db = _dbContextFactory.CreateDbContext();
            permit = db.Permits.AsNoTracking().FirstOrDefault(p => p.QrToken == token);
            if (permit == null)
            {
                message = "هذا التصريح غير مصرح أو تم تعديله.";
                status = "unauthorized";
                return false;
            }

            if (
                permit.ArchivedAt.HasValue
                || string.Equals(
                    permit.ApprovalStatus,
                    "Expired",
                    StringComparison.OrdinalIgnoreCase
                )
                || (permit.ExpiresAt.HasValue && permit.ExpiresAt.Value <= _systemClock.LocalNow)
            )
            {
                message = "هذا التصريح منتهي أو غير فعال.";
                status = "expired";
                return false;
            }

            if (
                !string.Equals(
                    permit.ApprovalStatus,
                    "Approved",
                    StringComparison.OrdinalIgnoreCase
                )
                && !string.Equals(
                    permit.ApprovalStatus,
                    "Out",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                message = string.Equals(
                    permit.ApprovalStatus,
                    "Stopped",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "هذا التصريح موقوف وغير مصرح باستخدامه."
                    : "هذا التصريح غير مصرح باستخدامه حاليًا.";
                status = "unauthorized";
                return false;
            }

            status = "authorized";
            message = "التصريح فعال ومصرح به.";
            return true;
        }

        public string EnsurePermitQrToken(string permitNumber, string? performedBy = null)
        {
            if (string.IsNullOrWhiteSpace(permitNumber))
            {
                return string.Empty;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var permit = db.Permits.FirstOrDefault(p => p.PermitNumber == permitNumber);
            if (permit == null)
            {
                return string.Empty;
            }

            if (PermitQrTokenGenerator.IsCurrentVersion(permit.QrToken))
            {
                return permit.QrToken;
            }

            permit.QrToken = GenerateQrToken(permit);
            db.SaveChanges();
            return permit.QrToken;
        }

        public (bool allowed, string reason) RecordPermitScan(
            string permitNumber,
            string? recordedBy = null,
            bool overrideEntry = false,
            PermitScanAuditContext? auditContext = null
        )
        {
            return _permitMovementService.RecordPermitScan(
                permitNumber,
                recordedBy,
                overrideEntry,
                auditContext
            );
        }

        public bool MarkPermitDeparted(
            string permitNumber,
            string? recordedBy = null,
            PermitScanAuditContext? auditContext = null
        )
        {
            return _permitMovementService.MarkPermitDeparted(
                permitNumber,
                recordedBy,
                auditContext
            );
        }

        public bool MarkPermitReturned(
            string permitNumber,
            string? recordedBy = null,
            PermitScanAuditContext? auditContext = null
        )
        {
            return _permitMovementService.MarkPermitReturned(permitNumber, recordedBy, auditContext);
        }

        public bool StopPermit(string permitNumber, string? performedBy = null)
        {
            return _permitLifecycleService.StopPermit(permitNumber, performedBy);
        }

        public bool ReactivatePermit(string permitNumber, string? performedBy = null)
        {
            return _permitLifecycleService.ReactivatePermit(permitNumber, performedBy);
        }

        public bool ClearDailyLeaveSchedule(string permitNumber, string? performedBy = null)
        {
            return _permitLifecycleService.ClearDailyLeaveSchedule(permitNumber, performedBy);
        }

        public bool ResolveUnauthorizedExitReview(
            string sequenceId,
            bool confirmViolation,
            string? performedBy = null
        )
        {
            return _permitMovementService.ResolveUnauthorizedExitReview(
                sequenceId,
                confirmViolation,
                performedBy
            );
        }

        public bool AddOperatorNote(
            string permitNumber,
            string noteText,
            string? performedBy = null,
            PermitScanAuditContext? auditContext = null
        )
        {
            var normalizedPermitNumber = (permitNumber ?? string.Empty).Trim();
            var normalizedNoteText = (noteText ?? string.Empty).Trim();
            if (
                string.IsNullOrWhiteSpace(normalizedPermitNumber)
                || string.IsNullOrWhiteSpace(normalizedNoteText)
            )
            {
                return false;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var permit = db.Permits.FirstOrDefault(item =>
                item.PermitNumber == normalizedPermitNumber
            );
            if (permit == null)
            {
                return false;
            }

            var now = _systemClock.UtcNow;
            var normalizedPerformedBy = (performedBy ?? string.Empty).Trim();
            var duplicateWindowStart = now.AddSeconds(-5);
            var duplicateExists = db.PermitActivities.Any(activity =>
                activity.PermitNumber == normalizedPermitNumber
                && activity.ActionType == "OperatorNote"
                && activity.Message == normalizedNoteText
                && activity.RecordedBy == normalizedPerformedBy
                && activity.OccurredAt >= duplicateWindowStart
            );
            if (duplicateExists)
            {
                return true;
            }

            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "OperatorNote",
                "ملاحظة من البوابة",
                normalizedNoteText,
                "DisplayController",
                performedBy,
                now,
                reasonCode: "OperatorNote",
                auditContext: auditContext
            );
            db.SaveChanges();
            return true;
        }

        public int ClosePermitsAtWorkEnd(string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    var settings = GetAdministrationSettings(db);
                    var workHours = BuildWorkHoursSettings(settings);
                    var now = _systemClock.LocalNow;

                    if (
                        !AdministrationWorkSchedule.TryGetMostRecentWorkEnd(
                            now,
                            settings,
                            out var workEndTime
                        )
                    )
                    {
                        return 0;
                    }

                    var closureTime = workEndTime.Add(workHours.WorkEndExitGrace);
                    if (now < closureTime)
                    {
                        return 0;
                    }

                    if (
                        settings.LastWorkEndClosureAt.HasValue
                        && settings.LastWorkEndClosureAt.Value >= closureTime
                    )
                    {
                        return 0;
                    }

                    SynchronizePermitStates(db);

                    var permitsToClose = db
                        .Permits.Where(p =>
                            p.PermitType == Permit.PermitTypePermanent
                            && p.ApprovalStatus == "Approved"
                            && p.CurrentState == PermitCurrentStateInside
                            && !p.ArchivedAt.HasValue
                            && (p.ReturnTime ?? p.PermitDate ?? DateTime.MinValue) <= closureTime
                            && p.LastAutomaticWorkEndExitAt != closureTime
                        )
                        .ToList();

                    foreach (var permit in permitsToClose)
                    {
                        if (permit.PendingUnauthorizedExitAt.HasValue)
                        {
                            var closeAuditContext = PermitScanAuditContext.CreateSystemContext(
                                "إغلاق نهاية الدوام",
                                performedBy
                            );
                            closeAuditContext.SequenceId = permit.PendingUnauthorizedExitSequenceId;
                            closeAuditContext.ClassificationStatus = "ClosedWithoutViolation";

                            _permitAuditService.RecordPermitActivity(
                                db,
                                permit,
                                "DeniedAttemptClosed",
                                "إغلاق محاولة معلقة",
                                $"أغلقت محاولة الخروج المعلقة لـ {permit.DriverName} دون مخالفة بعد بقائه حتى نهاية الدوام.",
                                "مزامنة النظام",
                                performedBy,
                                closureTime,
                                reasonCode: "DeniedAttemptClosed",
                                auditContext: closeAuditContext
                            );
                            permit.PendingUnauthorizedExitAt = null;
                            permit.PendingUnauthorizedExitSequenceId = string.Empty;
                        }

                        permit.OutTime = closureTime;
                        permit.ReturnTime = null;
                        permit.ExpectedReturnTime = null;
                        permit.LeaveWindowStartAt = null;
                        permit.LeaveWindowEndAt = null;
                        permit.PendingExitRequest = false;
                        permit.LeaveReason = string.Empty;
                        permit.CurrentState = PermitCurrentStateOutside;
                        permit.ApprovalStatus = "Approved";
                        permit.LastAutomaticWorkEndExitAt = closureTime;

                        _permitAuditService.RecordPermitActivity(
                            db,
                            permit,
                            "WorkEndExit",
                            "خروج نهاية الدوام",
                            $"تم تسجيل خروج {permit.DriverName} تلقائيًا عند نهاية الدوام.",
                            "مزامنة النظام",
                            performedBy,
                            closureTime,
                            reasonCode: "WorkEndExit",
                            auditContext: PermitScanAuditContext.CreateSystemContext(
                                "إغلاق نهاية الدوام",
                                performedBy
                            )
                        );

                        _permitAuditService.RecordPermitActivity(
                            db,
                            permit,
                            "LateCheckout",
                            "تأخر انصراف",
                            $"تم إغلاق انصراف {permit.DriverName} تلقائيًا بعد انتهاء فترة سماح الانصراف.",
                            "مزامنة النظام",
                            performedBy,
                            closureTime,
                            reasonCode: "LateCheckout",
                            lateMinutes: 0,
                            auditContext: PermitScanAuditContext.CreateSystemContext(
                                "إغلاق نهاية الدوام",
                                performedBy
                            )
                        );

                        _permitAuditService.NotifyPermitManagerOfActivity(
                            db,
                            permit,
                            performedBy,
                            "مزامنة النظام",
                            closureTime,
                            $"تم تسجيل خروج {permit.DriverName} تلقائيًا عند نهاية الدوام.",
                            "WorkEndExit",
                            "خروج نهاية الدوام"
                        );
                    }

                    settings.LastWorkEndClosureAt = closureTime;
                    db.SaveChanges();
                    return permitsToClose.Count;
                }
            );
        }

        public bool SubmitLeaveRequest(
            string permitNumber,
            bool requiresReturn,
            string leaveReason,
            DateTime? leaveStartAt,
            DateTime? leaveEndAt,
            string? performedBy = null,
            bool isDailySchedule = false,
            DateTime? scheduleStartDate = null,
            DateTime? scheduleEndDate = null,
            int? dailyExitMinutes = null,
            int? dailyReturnMinutes = null
        )
        {
            return _permitLifecycleService.SubmitLeaveRequest(
                permitNumber,
                requiresReturn,
                leaveReason,
                leaveStartAt,
                leaveEndAt,
                performedBy,
                isDailySchedule,
                scheduleStartDate,
                scheduleEndDate,
                dailyExitMinutes,
                dailyReturnMinutes
            );
        }

        public void UpdatePermitApprovalStatus(
            string permitNumber,
            string approvalStatus,
            string? performedBy = null,
            bool? requiresReturn = null,
            DateTime? expectedReturnTime = null,
            bool? pendingExitRequest = null,
            string? leaveReason = null
        )
        {
            _permitApprovalService.UpdatePermitApprovalStatus(
                permitNumber,
                approvalStatus,
                performedBy,
                requiresReturn,
                expectedReturnTime,
                pendingExitRequest,
                leaveReason
            );
        }

        public bool ForwardPermitToGeneralManager(string permitNumber, string? performedBy = null)
        {
            return _permitApprovalService.ForwardPermitToGeneralManager(permitNumber, performedBy);
        }

        private static T ExecuteInTransaction<T>(ApplicationDbContext db, Func<T> action)
        {
            using var transaction = db.Database.BeginTransaction();
            try
            {
                var result = action();
                transaction.Commit();
                return result;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private IQueryable<Permit> ApplyPermitDepartmentScope(
            IQueryable<Permit> permits,
            UserAccount? user
        )
        {
            if (user == null)
            {
                return permits.Where(_ => false);
            }

            // Use centralized access control to filter visible permits
            return permits
                .ToList()
                .Where(p => _accessControl.CanAccessPermit(p, user))
                .AsQueryable();
        }

        private IQueryable<Permit> ApplyPendingApprovalScope(
            IQueryable<Permit> permits,
            UserAccount? user
        )
        {
            if (!CanApprovePermitRequests(user))
            {
                return permits.Where(_ => false);
            }

            // Allow general manager to see all pending approvals
            if (IsGeneralManager(user))
            {
                return permits;
            }

            // Use centralized access control to determine which permits this user can approve
            return permits
                .ToList()
                .Where(p => _accessControl.CanApprovePermit(p, user))
                .AsQueryable();
        }

        private UserAccount? ResolveUserAccount(ApplicationDbContext db, string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            return db.UserAccounts.AsNoTracking().FirstOrDefault(u => u.Username == username);
        }

        private bool CanApprovePermitRequests(UserAccount? user)
        {
            return user != null
                && user.IsActive
                && _delegationService.HasEffectivePermission(user, AppPermissions.ApprovePermit);
        }

        private static bool IsGeneralManager(UserAccount? user)
        {
            return user != null
                && string.Equals(
                    user.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool CanCurrentUserScopePermits(UserAccount? user)
        {
            if (user == null || !user.IsActive)
            {
                return false;
            }

            // Do not scope for general manager or administrative users with full management rights
            if (IsGeneralManager(user) || user.CanManageAdministration || user.CanManageUsers)
            {
                return false;
            }

            // Scope permits to a department when the user has a department assigned (covers
            // department managers and regular employees who should only see their department).
            var dept = (user.Department ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(dept);
        }

        private static List<string> BuildPermitConflictReasons(
            string candidateName,
            string candidateNationalId,
            string candidatePlateNumber,
            Permit existingPermit
        )
        {
            var reasons = new List<string>();

            if (
                !string.IsNullOrWhiteSpace(candidateNationalId)
                && string.Equals(
                    candidateNationalId,
                    NormalizeComparisonValue(existingPermit.NationalId),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                reasons.Add("رقم الهوية");
            }

            if (
                !string.IsNullOrWhiteSpace(candidateName)
                && string.Equals(
                    candidateName,
                    NormalizeComparisonValue(existingPermit.DriverName),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                reasons.Add("اسم المصرح له");
            }

            if (
                !string.IsNullOrWhiteSpace(candidatePlateNumber)
                && string.Equals(
                    candidatePlateNumber,
                    NormalizePlateComparisonValue(existingPermit.PlateNumber),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                reasons.Add("رقم اللوحة");
            }

            return reasons;
        }

        private static string NormalizeComparisonValue(
            string? value,
            bool collapseWhitespace = true
        )
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim();
            if (collapseWhitespace)
            {
                normalized = string.Join(
                    " ",
                    normalized.Split(
                        new[] { ' ', '\t', '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries
                    )
                );
            }

            return NormalizeArabicDigits(normalized).ToUpperInvariant();
        }

        private static string NormalizePlateComparisonValue(string? value)
        {
            return NormalizeComparisonValue(value)
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty);
        }

        private static string NormalizeArabicDigits(string value)
        {
            return value
                .Replace('٠', '0')
                .Replace('١', '1')
                .Replace('٢', '2')
                .Replace('٣', '3')
                .Replace('٤', '4')
                .Replace('٥', '5')
                .Replace('٦', '6')
                .Replace('٧', '7')
                .Replace('٨', '8')
                .Replace('٩', '9');
        }

        private static string NormalizePermitIdentifier(string? permitNumber)
        {
            if (string.IsNullOrWhiteSpace(permitNumber))
            {
                return string.Empty;
            }

            if (TryGetPermitSequenceNumber(permitNumber, out var sequenceNumber))
            {
                return $"PERMIT-{sequenceNumber:D5}";
            }

            return permitNumber.Trim();
        }

        private static bool TryGetPermitSequenceNumber(string? permitNumber, out int sequenceNumber)
        {
            sequenceNumber = 0;
            if (string.IsNullOrWhiteSpace(permitNumber))
            {
                return false;
            }

            var normalized = NormalizeArabicDigits(permitNumber.Trim());
            var digits = new string(normalized.Where(char.IsDigit).ToArray());
            return digits.Length > 0 && int.TryParse(digits, out sequenceNumber);
        }

        private static string[] BuildPermitLookupKeys(string? permitNumber)
        {
            if (string.IsNullOrWhiteSpace(permitNumber))
            {
                return Array.Empty<string>();
            }

            var normalized = NormalizePermitIdentifier(permitNumber);
            var keys = new List<string> { normalized };

            if (TryGetPermitSequenceNumber(permitNumber, out var sequenceNumber))
            {
                keys.Add($"PERMIT-{sequenceNumber:D5}");
                keys.Add($"P{sequenceNumber}");
                keys.Add($"P{sequenceNumber:D4}");
                keys.Add($"P{sequenceNumber:D5}");
                keys.Add($"P{sequenceNumber:D6}");
            }

            return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string GetRecentActivitiesCacheKey(int take, string? username)
        {
            var normalizedUsername = string.IsNullOrWhiteSpace(username)
                ? "all"
                : username.Trim().ToUpperInvariant();
            return $"permit:recent:{normalizedUsername}:{take}";
        }

        private static string GetLatestActivityCacheKey()
        {
            return "permit:latest";
        }

        private WorkHoursSettings GetWorkHoursSettings()
        {
            var settings = GetAdministrationSettings();
            return BuildWorkHoursSettings(settings);
        }

        private WorkHoursSettings GetWorkHoursSettings(ApplicationDbContext db)
        {
            return BuildWorkHoursSettings(GetAdministrationSettings(db));
        }

        private static WorkHoursSettings BuildWorkHoursSettings(AdministrationSettings settings)
        {
            return new WorkHoursSettings(
                settings.WorkStartTime,
                settings.WorkEndTime,
                TimeSpan.FromMinutes(Math.Max(0, settings.AttendanceGraceMinutes)),
                TimeSpan.FromMinutes(Math.Max(0, settings.WorkEndExitGraceMinutes)),
                TimeSpan.FromMinutes(Math.Max(0, settings.LateReturnGraceMinutes)),
                settings.OfficialWorkDaysCsv
            );
        }

        private AdministrationSettings GetAdministrationSettings()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var existing = GetAdministrationSettings(db);
            return existing;
        }

        private AdministrationSettings GetAdministrationSettings(ApplicationDbContext db)
        {
            var existing = db
                .AdministrationSettings.OrderByDescending(x => x.Id == 1)
                .ThenBy(x => x.Id)
                .FirstOrDefault();
            if (existing == null)
            {
                existing = BuildDefaultAdministrationSettings();
                db.AdministrationSettings.Add(existing);
                db.SaveChanges();
            }

            return existing;
        }

        private AdministrationSettings BuildDefaultAdministrationSettings()
        {
            return new AdministrationSettings
            {
                OrganizationName =
                    _configuration["Administration:OrganizationName"] ?? string.Empty,
                DepartmentName = _configuration["Administration:DepartmentName"] ?? string.Empty,
                Address = _configuration["Administration:Address"] ?? string.Empty,
                Phone = _configuration["Administration:Phone"] ?? string.Empty,
                Email = _configuration["Administration:Email"] ?? string.Empty,
                ManagerName = _configuration["Administration:ManagerName"] ?? string.Empty,
                ManagerTitle = _configuration["Administration:ManagerTitle"] ?? string.Empty,
                SignatureText = _configuration["Administration:SignatureText"] ?? string.Empty,
                LogoPath = _configuration["Administration:LogoPath"] ?? string.Empty,
                SignatureImagePath =
                    _configuration["Administration:SignatureImagePath"] ?? string.Empty,
                DisplayBaseUrl = _configuration["Security:DisplayBaseUrl"] ?? string.Empty,
                WorkStartTime = new TimeOnly(8, 0),
                WorkEndTime = new TimeOnly(16, 0),
                AttendanceGraceMinutes = 15,
                WorkEndExitGraceMinutes = 30,
                LateReturnGraceMinutes = 5,
                OfficialWorkDaysCsv = AdministrationWorkSchedule.DefaultOfficialWorkDaysCsv,
            };
        }

        private static bool ShouldExpireAfterReturn(Permit permit)
        {
            return permit.RequiresReturn
                && !string.Equals(
                    permit.PermitType,
                    Permit.PermitTypePermanent,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private bool CanStopPermit(Permit permit)
        {
            return !permit.ArchivedAt.HasValue
                && string.Equals(
                    permit.ApprovalStatus,
                    "Approved",
                    StringComparison.OrdinalIgnoreCase
                )
                && (!permit.ExpiresAt.HasValue || permit.ExpiresAt.Value > _systemClock.LocalNow);
        }

        private static bool CanReactivatePermit(Permit permit)
        {
            return string.Equals(
                permit.ApprovalStatus,
                "Stopped",
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static bool IsTerminalPermit(Permit permit)
        {
            return permit.ArchivedAt.HasValue
                || string.Equals(
                    permit.ApprovalStatus,
                    "Expired",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    permit.ApprovalStatus,
                    "Exited",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    permit.ApprovalStatus,
                    "Stopped",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool IsPermitHiddenFromOperationalViews(Permit permit)
        {
            return permit.ArchivedAt.HasValue;
        }

        private static DateTime CalculateVisitorPermitExpiration(
            DateTime referenceTime,
            WorkHoursSettings workHours
        )
        {
            return AdministrationWorkSchedule.GetExpectedShiftEnd(
                referenceTime,
                workHours.StartTime,
                workHours.EndTime,
                workHours.OfficialWorkDaysCsv
            );
        }

        private static bool EnsureVisitorPermitExpiration(
            Permit permit,
            DateTime referenceTime,
            WorkHoursSettings workHours
        )
        {
            if (!permit.IsVisitorPermit || permit.ExpiresAt.HasValue)
            {
                return false;
            }

            permit.ExpiresAt = CalculateVisitorPermitExpiration(referenceTime, workHours);
            return true;
        }

        private void SynchronizePermitStates(ApplicationDbContext db)
        {
            var now = _systemClock.LocalNow;
            var workHours = GetWorkHoursSettings(db);
            var activePermits = db.Permits.Where(permit => !permit.ArchivedAt.HasValue).ToList();
            var archivedPermitsNeedingCleanup = db
                .Permits.Where(permit =>
                    permit.ArchivedAt >= DateTime.MinValue
                    && (
                        permit.TemporaryExitPermissionUntil.HasValue
                        || permit.PendingExitRequest
                        || permit.LeaveWindowStartAt.HasValue
                        || permit.LeaveWindowEndAt.HasValue
                        || permit.ExpectedReturnTime.HasValue
                        || permit.LeaveReason != string.Empty
                        || permit.PendingUnauthorizedExitAt.HasValue
                        || permit.PendingUnauthorizedExitSequenceId != string.Empty
                        || permit.UnauthorizedExitWarningCount > 0
                        || permit.LastUnauthorizedExitWarningAt.HasValue
                        || permit.LateReturnWarningCount > 0
                        || permit.LastLateReturnWarningForExpectedReturnTime.HasValue
                    )
                )
                .ToList();
            var permits = activePermits.Concat(archivedPermitsNeedingCleanup);
            var changed = false;

            foreach (var permit in permits)
            {
                var originalAccessMode = permit.AccessMode;
                var originalRequiresReturn = permit.RequiresReturn;
                permit.NormalizeAccessModeState();
                if (
                    !string.Equals(
                        originalAccessMode,
                        permit.AccessMode,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || originalRequiresReturn != permit.RequiresReturn
                )
                {
                    changed = true;
                }

                if (permit.IsFullAccessPermit)
                {
                    if (
                        permit.ExpectedReturnTime.HasValue
                        || permit.PendingExitRequest
                        || permit.LeaveWindowStartAt.HasValue
                        || permit.LeaveWindowEndAt.HasValue
                        || !string.IsNullOrWhiteSpace(permit.LeaveReason)
                    )
                    {
                        permit.ExpectedReturnTime = null;
                        permit.PendingExitRequest = false;
                        permit.LeaveWindowStartAt = null;
                        permit.LeaveWindowEndAt = null;
                        permit.LeaveReason = string.Empty;
                        changed = true;
                    }

                    if (
                        permit.PendingUnauthorizedExitAt.HasValue
                        || !string.IsNullOrWhiteSpace(permit.PendingUnauthorizedExitSequenceId)
                        || permit.UnauthorizedExitWarningCount > 0
                        || permit.LastUnauthorizedExitWarningAt.HasValue
                        || permit.LateReturnWarningCount > 0
                        || permit.LastLateReturnWarningForExpectedReturnTime.HasValue
                    )
                    {
                        permit.PendingUnauthorizedExitAt = null;
                        permit.PendingUnauthorizedExitSequenceId = string.Empty;
                        permit.UnauthorizedExitWarningCount = 0;
                        permit.LastUnauthorizedExitWarningAt = null;
                        permit.LateReturnWarningCount = 0;
                        permit.LastLateReturnWarningForExpectedReturnTime = null;
                        changed = true;
                    }
                }

                if (
                    permit.TemporaryExitPermissionUntil.HasValue
                    && permit.TemporaryExitPermissionUntil.Value <= now
                )
                {
                    permit.TemporaryExitPermissionUntil = null;
                    changed = true;
                }

                var synchronizedState = permit.ReturnTime.HasValue
                    ? PermitCurrentStateInside
                    : PermitCurrentStateOutside;
                if (
                    string.Equals(
                        permit.ApprovalStatus,
                        "Expired",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        permit.ApprovalStatus,
                        "Exited",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    synchronizedState = PermitCurrentStateOutside;
                }
                if (
                    !string.Equals(
                        permit.CurrentState,
                        synchronizedState,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    permit.CurrentState = synchronizedState;
                    changed = true;
                }

                if (EnsureVisitorPermitExpiration(permit, permit.PermitDate ?? now, workHours))
                {
                    changed = true;
                }

                if (
                    string.Equals(
                        permit.ApprovalStatus,
                        "Expired",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        permit.ApprovalStatus,
                        "Exited",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    if (permit.PendingExitRequest)
                    {
                        permit.PendingExitRequest = false;
                        changed = true;
                    }

                    if (permit.LeaveWindowStartAt.HasValue || permit.LeaveWindowEndAt.HasValue)
                    {
                        permit.LeaveWindowStartAt = null;
                        permit.LeaveWindowEndAt = null;
                        changed = true;
                    }

                    if (!string.IsNullOrWhiteSpace(permit.LeaveReason))
                    {
                        permit.LeaveReason = string.Empty;
                        changed = true;
                    }

                    if (
                        permit.PendingUnauthorizedExitAt.HasValue
                        || !string.IsNullOrWhiteSpace(permit.PendingUnauthorizedExitSequenceId)
                    )
                    {
                        permit.PendingUnauthorizedExitAt = null;
                        permit.PendingUnauthorizedExitSequenceId = string.Empty;
                        changed = true;
                    }
                }

                if (
                    permit.PendingExitRequest
                    && string.Equals(
                        permit.CurrentState,
                        PermitCurrentStateInside,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && _leavePolicyService.IsLeaveWindowExpired(permit, now)
                )
                {
                    _leavePolicyService.ClearLeaveRequestState(permit);
                    changed = true;
                }

                if (
                    ShouldExpireAfterReturn(permit)
                    && permit.ReturnTime.HasValue
                    && !permit.ArchivedAt.HasValue
                )
                {
                    permit.ApprovalStatus = "Returned";
                    if (!permit.ExpiresAt.HasValue || permit.ExpiresAt > permit.ReturnTime.Value)
                    {
                        permit.ExpiresAt = permit.ReturnTime.Value;
                    }
                    permit.ArchivedAt = permit.ReturnTime.Value;
                    changed = true;
                    continue;
                }

                if (
                    permit.ExpiresAt.HasValue
                    && permit.ExpiresAt.Value <= now
                    && !permit.ArchivedAt.HasValue
                    && permit.ApprovalStatus != "Rejected"
                )
                {
                    permit.ApprovalStatus = "Expired";
                    permit.ArchivedAt = permit.ExpiresAt.Value;
                    permit.CurrentState = PermitCurrentStateOutside;
                    permit.PendingExitRequest = false;
                    permit.LeaveWindowStartAt = null;
                    permit.LeaveWindowEndAt = null;
                    permit.LeaveReason = string.Empty;
                    permit.PendingUnauthorizedExitAt = null;
                    permit.PendingUnauthorizedExitSequenceId = string.Empty;
                    changed = true;
                }
            }

            if (changed)
            {
                db.SaveChanges();
            }
        }

        private static string GenerateNextPermitNumber(ApplicationDbContext db)
        {
            var nextNumber =
                db.Permits.IgnoreQueryFilters().AsEnumerable()
                    .Select(p =>
                        TryGetPermitSequenceNumber(p.PermitNumber, out var value) ? value : 0
                    )
                    .DefaultIfEmpty(0)
                    .Max() + 1;
            return $"PERMIT-{nextNumber:D5}";
        }

        private string GenerateQrToken(Permit permit)
        {
            return PermitQrTokenGenerator.Generate(permit);
        }
    }
}
