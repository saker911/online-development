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

namespace VehiclePermitSystemWeb.Services.Permits
{
    public sealed partial class PermitMovementService : IPermitMovementService
    {
        private const string PermitCurrentStateInside = "Inside";
        private const string PermitCurrentStateOutside = "Outside";
        private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ShortCacheWindow = TimeSpan.FromSeconds(5);

        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IMemoryCache _memoryCache;
        private readonly ISystemClock _systemClock;
        private readonly IGatePolicyService _gatePolicyService;
        private readonly ILeavePolicyService _leavePolicyService;
        private readonly IPermitMonitoringService _permitMonitoringService;
        private readonly IPermitAuditService _permitAuditService;

        public PermitMovementService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IMemoryCache memoryCache,
            ISystemClock systemClock,
            IGatePolicyService gatePolicyService,
            ILeavePolicyService leavePolicyService,
            IPermitMonitoringService permitMonitoringService,
            IPermitAuditService permitAuditService
        )
        {
            _dbContextFactory = dbContextFactory;
            _memoryCache = memoryCache;
            _systemClock = systemClock;
            _gatePolicyService = gatePolicyService;
            _leavePolicyService = leavePolicyService;
            _permitMonitoringService = permitMonitoringService;
            _permitAuditService = permitAuditService;
        }

        private sealed record PermitOperationContext(
            Permit Permit,
            PermitActivitySnapshot? LatestActivity
        );

        private sealed record PermitActivitySnapshot(string ActionType, DateTime OccurredAt);

        private sealed record WorkHoursSettings(
            TimeOnly StartTime,
            TimeOnly EndTime,
            TimeSpan AttendanceGrace,
            TimeSpan WorkEndExitGrace,
            TimeSpan LateReturnGrace,
            string OfficialWorkDaysCsv
        );

        public (bool allowed, string reason) RecordPermitScan(
            string permitNumber,
            string? recordedBy = null,
            bool overrideEntry = false,
            PermitScanAuditContext? auditContext = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    SynchronizePermitStates(db);
                    var operationContext = LoadPermitOperationContext(db, permitNumber);
                    if (operationContext == null)
                    {
                        return (false, "Permit not found");
                    }

                    var permit = operationContext.Permit;
                    PreparePermitForAccessMode(permit);
                    if (!IsPermitActiveForScan(permit))
                    {
                        return (false, "permit_not_active");
                    }

                    if (overrideEntry)
                    {
                        var overrideReason = (
                            auditContext?.ManualOverrideReason ?? string.Empty
                        ).Trim();
                        if (string.IsNullOrWhiteSpace(overrideReason))
                        {
                            return (false, "override_reason_required");
                        }

                        permit.ApprovalStatus = "Approved";
                        permit.ArchivedAt = null;
                        permit.ExpiresAt = null;
                        permit.ExpectedReturnTime = null;
                        permit.CurrentState = PermitCurrentStateInside;
                        permit.PendingUnauthorizedExitAt = null;
                        permit.PendingUnauthorizedExitSequenceId = string.Empty;
                        _permitAuditService.RecordPermitActivity(
                            db,
                            permit,
                            "OverrideEntry",
                            "سماح دخول استثنائي",
                            $"تم تنفيذ سماح دخول استثنائي. السبب: {overrideReason}",
                            nameof(PermitMovementService),
                            recordedBy,
                            _systemClock.LocalNow,
                            reasonCode: "ManualOverrideEntry",
                            auditContext: auditContext
                        );
                        db.SaveChanges();
                        return (true, "OverrideEntry recorded");
                    }

                    if (
                        operationContext.LatestActivity != null
                        && IsDuplicateProtectedActivity(operationContext.LatestActivity.ActionType)
                        && (_systemClock.LocalNow - operationContext.LatestActivity.OccurredAt)
                            <= DuplicateWindow
                    )
                    {
                        return (
                            false,
                            GetDuplicateScanMessage(permit, operationContext.LatestActivity)
                        );
                    }

                    var now = _systemClock.LocalNow;
                    if (
                        RecordOverdueReturnViolationBeforeEntry(
                            db,
                            permit,
                            recordedBy,
                            now,
                            "قارئ الباركود",
                            auditContext
                        )
                    )
                    {
                        return (false, "permit_stopped");
                    }

                    if (AutoCloseStalePreviousDaySession(db, permit, recordedBy, now, auditContext))
                    {
                        return (false, "permit_stopped");
                    }

                    var gateDecision = _gatePolicyService.ResolveDecision(permit, now);

                    return gateDecision.Outcome switch
                    {
                        GateDecisionOutcome.AllowEntry => permit.IsPermanentPermit
                        && IsAutomaticWorkEndExit(permit)
                            ? HandleWorkEndEntry(
                                db,
                                permit,
                                recordedBy,
                                now,
                                "قارئ الباركود",
                                auditContext
                            )
                            : HandleEntry(
                                db,
                                permit,
                                recordedBy,
                                now,
                                "قارئ الباركود",
                                auditContext
                            ),
                        GateDecisionOutcome.AllowExit => HandleExit(
                            db,
                            permit,
                            recordedBy,
                            true,
                            now,
                            "قارئ الباركود",
                            auditContext
                        ),
                        GateDecisionOutcome.AllowFinalExit => HandleFinalExit(
                            db,
                            permit,
                            recordedBy,
                            now,
                            "قارئ الباركود",
                            auditContext
                        ),
                        GateDecisionOutcome.DenyStopped => (false, gateDecision.ReasonCode),
                        GateDecisionOutcome.DenyExpired => (false, gateDecision.ReasonCode),
                        GateDecisionOutcome.DenyNoPermission
                            when gateDecision.ReasonCode == "LeaveWindowNotStarted" => (
                            false,
                            gateDecision.ReasonCode
                        ),
                        GateDecisionOutcome.DenyNoPermission
                        or GateDecisionOutcome.DenyPendingReview
                        or GateDecisionOutcome.WaitForWorkEndClosure =>
                            HandleUnauthorizedExitAttempt(
                                db,
                                permit,
                                recordedBy,
                                now,
                                "قارئ الباركود",
                                auditContext
                            ),
                        _ => HandleExit(
                            db,
                            permit,
                            recordedBy,
                            permit.RequiresReturn,
                            now,
                            "قارئ الباركود",
                            auditContext
                        ),
                    };
                }
            );
        }

        public bool ResolveUnauthorizedExitReview(
            string sequenceId,
            bool confirmViolation,
            string? recordedBy = null
        )
        {
            if (string.IsNullOrWhiteSpace(sequenceId))
            {
                return false;
            }

            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    SynchronizePermitStates(db);

                    var sequenceActivities = db
                        .PermitActivities.Where(activity => activity.SequenceId == sequenceId)
                        .OrderBy(activity => activity.OccurredAt)
                        .ThenBy(activity => activity.Id)
                        .ToList();

                    var latestActivity = sequenceActivities.LastOrDefault();
                    var reviewActivity = sequenceActivities.LastOrDefault(activity =>
                        string.Equals(
                            activity.ReasonCode,
                            "UnauthorizedExitNeedsReview",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            activity.ReasonCode,
                            "PendingUnauthorizedExit",
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

                    if (
                        reviewActivity == null
                        || latestActivity == null
                        || !(
                            string.Equals(
                                latestActivity.ReasonCode,
                                "UnauthorizedExitNeedsReview",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || string.Equals(
                                latestActivity.ReasonCode,
                                "PendingUnauthorizedExit",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
                    {
                        return false;
                    }

                    var permit = db.Permits.FirstOrDefault(p =>
                        p.PermitNumber == reviewActivity.PermitNumber
                    );
                    if (permit == null)
                    {
                        return false;
                    }

                    var now = _systemClock.LocalNow;
                    var source = "مراجعة إدارية";
                    var auditContext = PermitScanAuditContext.CreateSystemContext(
                        source,
                        recordedBy ?? "system"
                    );

                    if (confirmViolation)
                    {
                        _permitAuditService.RecordPermitActivity(
                            db,
                            permit,
                            "AdministrativeReviewConfirmed",
                            "اعتماد المخالفة",
                            $"تم اعتماد المخالفة إداريًا للتسلسل الخاص بـ {permit.DriverName} بعد مراجعة الحالة.",
                            source,
                            recordedBy,
                            now,
                            reasonCode: "AdministrativeReviewConfirmed",
                            auditContext: BuildAuditContext(
                                auditContext,
                                source,
                                sequenceId,
                                "ConfirmedViolation"
                            )
                        );

                        _permitAuditService.NotifyPermitManagerOfActivity(
                            db,
                            permit,
                            recordedBy,
                            source,
                            now,
                            $"تم اعتماد مخالفة الخروج غير المصرح لـ {permit.DriverName} بعد المراجعة الإدارية.",
                            "AdministrativeReviewConfirmed",
                            "اعتماد مخالفة خروج"
                        );

                        StopPermitForUnauthorizedExitViolation(
                            db,
                            permit,
                            recordedBy,
                            source,
                            now,
                            $"تم اعتماد المخالفة والتصريح ما زال موقوفًا لـ {permit.DriverName} حتى تنفيذ إعادة التفعيل.",
                            BuildAuditContext(auditContext, source, sequenceId, "Stopped")
                        );
                    }
                    else
                    {
                        _permitAuditService.RecordPermitActivity(
                            db,
                            permit,
                            "AdministrativeReviewDismissed",
                            "إغلاق دون مخالفة",
                            $"أغلقت المراجعة الإدارية للتسلسل الخاص بـ {permit.DriverName} دون اعتماد مخالفة نهائية.",
                            source,
                            recordedBy,
                            now,
                            reasonCode: "AdministrativeReviewDismissed",
                            auditContext: BuildAuditContext(
                                auditContext,
                                source,
                                sequenceId,
                                "ClosedWithoutViolation"
                            )
                        );

                        _permitAuditService.NotifyPermitManagerOfActivity(
                            db,
                            permit,
                            recordedBy,
                            source,
                            now,
                            $"أغلقت المراجعة الإدارية لمحاولة الخروج الخاصة بـ {permit.DriverName} دون مخالفة.",
                            "AdministrativeReviewDismissed",
                            "إغلاق مراجعة دون مخالفة"
                        );
                    }

                    db.SaveChanges();
                    _ = PersistUnauthorizedExitViolationSummary(
                        db,
                        permit,
                        recordedBy,
                        source,
                        now,
                        sequenceId,
                        auditContext,
                        $"تم إيقاف التصريح بعد تسجيل مخالفات خروج غير مصرح في 3 أيام مختلفة لـ {permit.DriverName}."
                    );
                    return true;
                }
            );
        }

        public bool MarkPermitDeparted(
            string permitNumber,
            string? recordedBy = null,
            PermitScanAuditContext? auditContext = null
        )
        {
            return RecordPermitScan(permitNumber, recordedBy, false, auditContext).allowed;
        }

        public bool MarkPermitReturned(
            string permitNumber,
            string? recordedBy = null,
            PermitScanAuditContext? auditContext = null
        )
        {
            return HandleEntry(permitNumber, recordedBy, auditContext).allowed;
        }

        private PermitOperationContext? LoadPermitOperationContext(
            ApplicationDbContext db,
            string permitNumber
        )
        {
            var lookupKeys = BuildPermitLookupKeys(permitNumber);
            if (lookupKeys.Length == 0)
            {
                return null;
            }

            TryGetCachedPermitActivitySnapshot(permitNumber, out var cachedSnapshot);

            var permit = db
                .Permits.Include(p =>
                    p.Activities.OrderByDescending(activity => activity.OccurredAt)
                        .ThenByDescending(activity => activity.Id)
                        .Take(1)
                )
                .FirstOrDefault(p => lookupKeys.Contains(p.PermitNumber));

            if (permit == null)
            {
                return null;
            }

            PermitActivitySnapshot? latestActivitySnapshot = null;
            if (permit.Activities.Count > 0)
            {
                var latestActivity = permit
                    .Activities.OrderByDescending(activity => activity.OccurredAt)
                    .ThenByDescending(activity => activity.Id)
                    .FirstOrDefault();
                if (latestActivity != null)
                {
                    latestActivitySnapshot = new PermitActivitySnapshot(
                        latestActivity.ActionType,
                        latestActivity.OccurredAt
                    );
                    CachePermitActivitySnapshot(
                        permit.PermitNumber,
                        latestActivitySnapshot.ActionType,
                        latestActivitySnapshot.OccurredAt
                    );
                }
            }

            if (
                cachedSnapshot != null
                && (
                    latestActivitySnapshot == null
                    || cachedSnapshot.OccurredAt > latestActivitySnapshot.OccurredAt
                )
            )
            {
                latestActivitySnapshot = cachedSnapshot;
            }

            return new PermitOperationContext(permit, latestActivitySnapshot);
        }

        private (bool allowed, string reason) HandleEntry(
            string permitNumber,
            string? scannerUser = null,
            PermitScanAuditContext? auditContext = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    SynchronizePermitStates(db);
                    var operationContext = LoadPermitOperationContext(db, permitNumber);
                    if (operationContext == null)
                    {
                        return (false, "Permit not found");
                    }

                    return HandleEntry(
                        db,
                        operationContext.Permit,
                        scannerUser,
                        _systemClock.LocalNow,
                        "قارئ الباركود",
                        auditContext
                    );
                }
            );
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

        private (bool allowed, string reason) HandleEntry(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            DateTime occurredAt,
            string source,
            PermitScanAuditContext? auditContext
        )
        {
            PreparePermitForAccessMode(permit);
            var workHours = GetWorkHoursSettings();
            var expectedReturnTime = _leavePolicyService.GetExpectedReturn(
                permit,
                permit.OutTime ?? occurredAt,
                workHours.StartTime,
                workHours.EndTime,
                workHours.OfficialWorkDaysCsv
            );
            var isLateReturn = _leavePolicyService.IsLateReturn(
                permit,
                occurredAt,
                workHours.LateReturnGrace,
                workHours.StartTime,
                workHours.EndTime,
                workHours.OfficialWorkDaysCsv
            );
            var lateMinutes = isLateReturn
                ? GetLateMinutes(occurredAt - expectedReturnTime!.Value)
                : (int?)null;

            var isReturningFromExit =
                string.Equals(permit.ApprovalStatus, "Out", StringComparison.OrdinalIgnoreCase)
                || (
                    permit.CurrentState == PermitCurrentStateOutside
                    && permit.OutTime.HasValue
                );

            if (isReturningFromExit)
            {
                if (HasPendingUnauthorizedExit(permit))
                {
                    var pendingSequenceId = permit.PendingUnauthorizedExitSequenceId;
                    permit.UnauthorizedExitWarningCount += 1;
                    permit.LastUnauthorizedExitWarningAt = occurredAt;

                    _permitAuditService.RecordPermitActivity(
                        db,
                        permit,
                        "UnauthorizedExitConfirmed",
                        "مخالفة خروج مؤكدة",
                        $"تم تأكيد خروج غير مصرح لـ {permit.DriverName} بعد اكتمال التسلسل بعودة لاحقة في نفس اليوم.",
                        source,
                        recordedBy,
                        occurredAt,
                        reasonCode: "UnauthorizedExitConfirmed",
                        auditContext: BuildAuditContext(
                            auditContext,
                            source,
                            pendingSequenceId,
                            "ConfirmedViolation"
                        )
                    );

                    permit.ReturnTime = occurredAt;
                    permit.ExpectedReturnTime = null;
                    permit.LeaveWindowStartAt = null;
                    permit.LeaveWindowEndAt = null;
                    permit.PendingExitRequest = false;
                    permit.LeaveReason = string.Empty;
                    permit.LastLateReturnWarningForExpectedReturnTime = null;
                    permit.CurrentState = PermitCurrentStateInside;
                    permit.ApprovalStatus = "Approved";
                    permit.ArchivedAt = null;
                    ClearPendingUnauthorizedExit(permit);

                    _permitAuditService.RecordPermitActivity(
                        db,
                        permit,
                        "ReturnAfterUnauthorizedExit",
                        "عودة بعد خروج غير مصرح",
                        $"تمت عودة {permit.DriverName} بعد خروج غير مصرح خلال نفس اليوم.",
                        source,
                        recordedBy,
                        occurredAt,
                        reasonCode: "ReturnAfterUnauthorizedExit",
                        auditContext: BuildAuditContext(
                            auditContext,
                            source,
                            pendingSequenceId,
                            "ConfirmedViolation"
                        )
                    );

                    _permitAuditService.NotifyPermitManagerOfActivity(
                        db,
                        permit,
                        recordedBy,
                        source,
                        occurredAt,
                        $"تم تأكيد خروج غير مصرح لـ {permit.DriverName} مع تسجيل عودته لاحقًا في نفس اليوم.",
                        "UnauthorizedExitConfirmed",
                        "مخالفة خروج مؤكدة"
                    );

                    db.SaveChanges();
                    _ = PersistUnauthorizedExitViolationSummary(
                        db,
                        permit,
                        recordedBy,
                        source,
                        occurredAt,
                        pendingSequenceId,
                        auditContext,
                        $"تم إيقاف التصريح بعد تسجيل مخالفات خروج غير مصرح في 3 أيام مختلفة لـ {permit.DriverName}."
                    );
                    return (true, "ReturnAfterUnauthorizedExit recorded");
                }

                CompletePermitReturn(
                    db,
                    permit,
                    recordedBy,
                    source,
                    occurredAt,
                    isLateReturn,
                    lateMinutes,
                    expectedReturnTime ?? occurredAt,
                    auditContext
                );
                permit.CurrentState = PermitCurrentStateInside;
                db.SaveChanges();
                return (true, isLateReturn ? "LateReturn recorded" : "Return recorded");
            }

            permit.ApprovalStatus = "Approved";
            permit.ReturnTime = occurredAt;
            permit.ExpectedReturnTime = null;
            permit.CurrentState = PermitCurrentStateInside;
            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "Entry",
                "تسجيل دخول",
                "تم تسجيل دخول التصريح.",
                source,
                recordedBy,
                occurredAt,
                reasonCode: "Entry",
                auditContext: BuildAuditContext(auditContext, source, null, "Completed")
            );
            RecordLateAttendanceIfNeeded(db, permit, recordedBy, source, occurredAt, auditContext);
            db.SaveChanges();
            return (true, "Entry recorded");
        }

        private (bool allowed, string reason) HandleWorkEndEntry(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            DateTime occurredAt,
            string source,
            PermitScanAuditContext? auditContext
        )
        {
            permit.ReturnTime = occurredAt;
            permit.ExpectedReturnTime = null;
            permit.LeaveWindowStartAt = null;
            permit.LeaveWindowEndAt = null;
            permit.PendingExitRequest = false;
            permit.LeaveReason = string.Empty;
            permit.CurrentState = PermitCurrentStateInside;
            permit.ApprovalStatus = "Approved";

            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "Entry",
                "تسجيل دخول",
                $"تم تسجيل دخول {permit.DriverName} بعد إغلاق نهاية الدوام.",
                source,
                recordedBy,
                occurredAt,
                reasonCode: "Entry",
                auditContext: BuildAuditContext(auditContext, source, null, "Completed")
            );

            _permitAuditService.NotifyPermitManagerOfActivity(
                db,
                permit,
                recordedBy,
                source,
                occurredAt,
                $"تم تسجيل دخول {permit.DriverName} بعد إغلاق نهاية الدوام.",
                "WorkEndEntry",
                "دخول بعد إغلاق نهاية الدوام"
            );

            RecordLateAttendanceIfNeeded(db, permit, recordedBy, source, occurredAt, auditContext);
            db.SaveChanges();
            return (true, "WorkEndEntry recorded");
        }

        private static bool IsAutomaticWorkEndExit(Permit permit)
        {
            return permit.LastAutomaticWorkEndExitAt.HasValue
                && permit.OutTime.HasValue
                && permit.LastAutomaticWorkEndExitAt.Value == permit.OutTime.Value;
        }

        private (bool allowed, string reason) HandleExit(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            bool authorizedExit,
            DateTime occurredAt,
            string source,
            PermitScanAuditContext? auditContext
        )
        {
            PreparePermitForAccessMode(permit);
            var currentWorkHours = GetWorkHoursSettings();
            var retainsReturnExpectation =
                authorizedExit
                && _leavePolicyService.RequiresReturn(
                    permit,
                    occurredAt,
                    currentWorkHours.OfficialWorkDaysCsv
                );

            if (authorizedExit && HasPendingUnauthorizedExit(permit))
            {
                ClosePendingUnauthorizedExitWithoutViolation(
                    db,
                    permit,
                    recordedBy,
                    source,
                    occurredAt,
                    "أغلقت محاولة الخروج المعلقة بعد اعتماد خروج نظامي لاحق في نفس اليوم.",
                    auditContext
                );
            }

            permit.OutTime = occurredAt;
            permit.ReturnTime = null;
            permit.ArchivedAt = null;
            permit.CurrentState = PermitCurrentStateOutside;
            permit.PendingExitRequest = false;

            if (!retainsReturnExpectation)
            {
                permit.LeaveWindowStartAt = null;
                permit.LeaveWindowEndAt = null;
                permit.LeaveReason = string.Empty;
            }

            if (permit.IsVisitorPermit)
            {
                permit.ExpectedReturnTime = null;
                permit.ApprovalStatus = "Expired";
                permit.ArchivedAt = occurredAt;
                if (!permit.ExpiresAt.HasValue || permit.ExpiresAt > occurredAt)
                {
                    permit.ExpiresAt = occurredAt;
                }

                _permitAuditService.RecordPermitActivity(
                    db,
                    permit,
                    "ExitFinal",
                    "خروج زائر نهائي",
                    $"تم إنهاء تصريح الزائر الخاص بـ {permit.DriverName} بعد الخروج.",
                    source,
                    recordedBy,
                    occurredAt,
                    reasonCode: "ExitFinal",
                    auditContext: BuildAuditContext(auditContext, source, null, "Completed")
                );

                db.SaveChanges();
                return (true, "VisitorExitFinal recorded");
            }

            if (authorizedExit)
            {
                permit.ExpectedReturnTime ??= _leavePolicyService.GetExpectedReturn(
                    permit,
                    occurredAt,
                    currentWorkHours.StartTime,
                    currentWorkHours.EndTime,
                    currentWorkHours.OfficialWorkDaysCsv
                );
                permit.ApprovalStatus = "Out";
                _permitAuditService.RecordPermitActivity(
                    db,
                    permit,
                    "ExitAuthorized",
                    "خروج بإذن",
                    $"تم تسجيل خروج {permit.DriverName} مع حفظ وقت العودة المتوقع.",
                    source,
                    recordedBy,
                    occurredAt,
                    reasonCode: "ExitAuthorized",
                    auditContext: BuildAuditContext(auditContext, source, null, "Completed")
                );
            }
            else
            {
                permit.ExpectedReturnTime = null;
                permit.ApprovalStatus = "Exited";
                permit.ArchivedAt = occurredAt;
                if (!permit.ExpiresAt.HasValue || permit.ExpiresAt > occurredAt)
                {
                    permit.ExpiresAt = occurredAt;
                }

                _permitAuditService.RecordPermitActivity(
                    db,
                    permit,
                    "ExitUnauthorized",
                    "خروج بدون إذن",
                    $"تم تسجيل خروج {permit.DriverName} بدون إذن عودة.",
                    source,
                    recordedBy,
                    occurredAt,
                    reasonCode: "ExitUnauthorized",
                    auditContext: BuildAuditContext(
                        auditContext,
                        source,
                        null,
                        "ConfirmedViolation"
                    )
                );
            }

            db.SaveChanges();
            return (true, authorizedExit ? "ExitAuthorized recorded" : "ExitUnauthorized recorded");
        }

        private (bool allowed, string reason) HandleUnauthorizedExitAttempt(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            DateTime occurredAt,
            string source,
            PermitScanAuditContext? auditContext
        )
        {
            PreparePermitForAccessMode(permit);
            if (permit.IsFullAccessPermit)
            {
                return HandleFinalExit(db, permit, recordedBy, occurredAt, source, auditContext);
            }

            var stoppedWhilePromoting = PromoteStalePendingUnauthorizedExitSequence(
                db,
                permit,
                recordedBy,
                occurredAt,
                source,
                auditContext
            );
            if (stoppedWhilePromoting)
            {
                return (false, "permit_stopped");
            }

            var sequenceId = EnsurePendingUnauthorizedExit(permit, occurredAt);
            var warningMessage =
                $"تم رفض خروج {permit.DriverName} لعدم وجود استئذان أثناء وقت الدوام الرسمي، وسجلت الحالة كمحاولة معلقة للمراجعة المؤجلة.";

            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "PendingUnauthorizedExit",
                "محاولة خروج معلقة",
                warningMessage,
                source,
                recordedBy,
                occurredAt,
                reasonCode: "PendingUnauthorizedExit",
                auditContext: BuildAuditContext(auditContext, source, sequenceId, "Pending")
            );

            db.SaveChanges();
            return (false, "PendingUnauthorizedExit");
        }

        private bool AutoCloseStalePreviousDaySession(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            DateTime occurredAt,
            PermitScanAuditContext? auditContext
        )
        {
            PreparePermitForAccessMode(permit);

            if (
                !permit.IsPermanentPermit
                || !string.Equals(
                    permit.ApprovalStatus,
                    "Approved",
                    StringComparison.OrdinalIgnoreCase
                )
                || !string.Equals(
                    permit.CurrentState,
                    PermitCurrentStateInside,
                    StringComparison.OrdinalIgnoreCase
                )
                || permit.ArchivedAt.HasValue
            )
            {
                return false;
            }

            var lastInsideAt = permit.ReturnTime ?? permit.PermitDate;
            if (!lastInsideAt.HasValue || lastInsideAt.Value.Date >= occurredAt.Date)
            {
                return false;
            }

            var settings = GetAdministrationSettings(db);
            DateTime workEndTime;
            if (
                !AdministrationWorkSchedule.TryGetShiftWindow(
                    lastInsideAt.Value,
                    settings,
                    out _,
                    out workEndTime
                )
                && !AdministrationWorkSchedule.TryGetMostRecentWorkEnd(
                    lastInsideAt.Value,
                    settings,
                    out workEndTime
                )
            )
            {
                return false;
            }

            var closureTime = workEndTime.Add(GetWorkHoursSettings().WorkEndExitGrace);
            if (permit.IsEntryOnlyPermit && lastInsideAt.Value > closureTime)
            {
                closureTime = lastInsideAt.Value;
            }

            if (occurredAt < closureTime)
            {
                return false;
            }

            if (lastInsideAt.Value > closureTime)
            {
                return false;
            }

            if (permit.LastAutomaticWorkEndExitAt == closureTime)
            {
                return false;
            }

            var closureAuditContext = PermitScanAuditContext.CreateSystemContext(
                "إغلاق نهاية الدوام",
                recordedBy
            );

            var hasStaleEntryOnlyPendingUnauthorizedExit =
                permit.IsEntryOnlyPermit
                && HasPendingUnauthorizedExit(permit)
                && permit.PendingUnauthorizedExitAt.HasValue
                && permit.PendingUnauthorizedExitAt.Value.Date < occurredAt.Date;
            var hasCurrentDayPendingOnStaleEntryOnlySession =
                permit.IsEntryOnlyPermit
                && HasPendingUnauthorizedExit(permit)
                && permit.PendingUnauthorizedExitAt.HasValue
                && permit.PendingUnauthorizedExitAt.Value.Date >= occurredAt.Date;

            if (
                HasPendingUnauthorizedExit(permit)
                && !hasStaleEntryOnlyPendingUnauthorizedExit
                && !hasCurrentDayPendingOnStaleEntryOnlySession
            )
            {
                return false;
            }

            var stoppedByStalePendingReview = false;
            if (hasStaleEntryOnlyPendingUnauthorizedExit)
            {
                stoppedByStalePendingReview = PromoteStalePendingUnauthorizedExitSequence(
                    db,
                    permit,
                    recordedBy,
                    occurredAt,
                    "مزامنة النظام",
                    auditContext ?? closureAuditContext
                );
            }

            if (hasCurrentDayPendingOnStaleEntryOnlySession)
            {
                ClosePendingUnauthorizedExitWithoutViolation(
                    db,
                    permit,
                    recordedBy,
                    "مزامنة النظام",
                    occurredAt,
                    $"أغلقت محاولة الخروج المعلقة لـ {permit.DriverName} دون مخالفة لأنها كانت مرتبطة بجلسة من يوم سابق تم إغلاقها آليًا.",
                    auditContext ?? closureAuditContext
                );
            }

            if (
                permit.IsEntryOnlyPermit
                && !hasStaleEntryOnlyPendingUnauthorizedExit
                && !hasCurrentDayPendingOnStaleEntryOnlySession
                && !string.Equals(
                    permit.ApprovalStatus,
                    "Stopped",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                var stoppedByNoCheckoutViolation = RecordConfirmedPenaltyViolation(
                    db,
                    permit,
                    "NoCheckoutViolation",
                    "عدم تسجيل خروج نهاية الدوام",
                    $"لم يتم تسجيل خروج {permit.DriverName} قبل نهاية الدوام، وسجلت الحالة كمخالفة حضور وانصراف.",
                    "مزامنة النظام",
                    recordedBy,
                    closureTime,
                    auditContext ?? closureAuditContext,
                    HasPendingUnauthorizedExit(permit)
                        ? permit.PendingUnauthorizedExitSequenceId
                        : null,
                    null,
                    $"تم إيقاف التصريح بعد تسجيل 3 مخالفات حضور وانصراف/استئذان في أيام مختلفة لـ {permit.DriverName}.",
                    PermitCurrentStateInside
                );

                if (stoppedByNoCheckoutViolation && !hasStaleEntryOnlyPendingUnauthorizedExit)
                {
                    return true;
                }
            }

            permit.OutTime = closureTime;
            permit.ReturnTime = null;
            permit.ExpectedReturnTime = null;
            permit.LeaveWindowStartAt = null;
            permit.LeaveWindowEndAt = null;
            permit.PendingExitRequest = false;
            permit.LeaveReason = string.Empty;
            permit.CurrentState = PermitCurrentStateOutside;
            if (
                !string.Equals(permit.ApprovalStatus, "Stopped", StringComparison.OrdinalIgnoreCase)
            )
            {
                permit.ApprovalStatus = "Approved";
            }
            permit.LastAutomaticWorkEndExitAt = closureTime;

            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "WorkEndExit",
                "خروج نهاية الدوام",
                $"تم تسجيل خروج {permit.DriverName} تلقائيًا عند إغلاق جلسة اليوم السابق.",
                "مزامنة النظام",
                recordedBy,
                closureTime,
                reasonCode: "WorkEndExit",
                auditContext: closureAuditContext
            );

            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "LateCheckout",
                "تأخر انصراف",
                $"تم إغلاق انصراف {permit.DriverName} تلقائيًا بعد انتهاء فترة سماح الانصراف.",
                "مزامنة النظام",
                recordedBy,
                closureTime,
                reasonCode: "LateCheckout",
                lateMinutes: 0,
                auditContext: closureAuditContext
            );

            _permitAuditService.NotifyPermitManagerOfActivity(
                db,
                permit,
                recordedBy,
                "مزامنة النظام",
                closureTime,
                $"تم تسجيل خروج {permit.DriverName} تلقائيًا عند إغلاق جلسة اليوم السابق.",
                "WorkEndExit",
                "خروج نهاية الدوام"
            );

            db.SaveChanges();
            return stoppedByStalePendingReview;
        }

        private bool RecordOverdueReturnViolationBeforeEntry(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            DateTime occurredAt,
            string source,
            PermitScanAuditContext? auditContext
        )
        {
            PreparePermitForAccessMode(permit);
            if (
                !permit.IsEntryOnlyPermit
                || !permit.ExpectedReturnTime.HasValue
                || permit.ArchivedAt.HasValue
                || !string.Equals(permit.ApprovalStatus, "Out", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    permit.CurrentState,
                    PermitCurrentStateOutside,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            var workHours = GetWorkHoursSettings();
            if (
                !_leavePolicyService.IsLateReturn(
                    permit,
                    occurredAt,
                    workHours.LateReturnGrace,
                    workHours.StartTime,
                    workHours.EndTime,
                    workHours.OfficialWorkDaysCsv
                )
            )
            {
                return false;
            }

            var violationAt = permit.ExpectedReturnTime.Value.Add(workHours.LateReturnGrace);
            var lateMinutes = GetLateMinutes(occurredAt - permit.ExpectedReturnTime.Value);
            return RecordConfirmedPenaltyViolation(
                db,
                permit,
                "NoReturnViolation",
                "عدم العودة من استئذان",
                $"لم يعد {permit.DriverName} من الاستئذان في الوقت المحدد، وسجلت الحالة كمخالفة استئذان.",
                source,
                recordedBy,
                violationAt,
                auditContext,
                null,
                lateMinutes,
                $"تم إيقاف التصريح بعد تسجيل 3 مخالفات حضور وانصراف/استئذان في أيام مختلفة لـ {permit.DriverName}.",
                PermitCurrentStateOutside
            );
        }

        private bool RecordConfirmedPenaltyViolation(
            ApplicationDbContext db,
            Permit permit,
            string actionType,
            string actionLabel,
            string message,
            string source,
            string? recordedBy,
            DateTime occurredAt,
            PermitScanAuditContext? auditContext,
            string? sequenceId,
            int? lateMinutes,
            string stopMessage,
            string stoppedCurrentState
        )
        {
            var violationSequenceId = string.IsNullOrWhiteSpace(sequenceId)
                ? Guid.NewGuid().ToString("N")
                : sequenceId;

            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                actionType,
                actionLabel,
                message,
                source,
                recordedBy,
                occurredAt,
                reasonCode: actionType,
                lateMinutes: lateMinutes,
                auditContext: BuildAuditContext(
                    auditContext,
                    source,
                    violationSequenceId,
                    "ConfirmedViolation"
                )
            );

            _permitAuditService.NotifyPermitManagerOfActivity(
                db,
                permit,
                recordedBy,
                source,
                occurredAt,
                message,
                actionType,
                actionLabel
            );

            ClearPendingUnauthorizedExit(permit);
            db.SaveChanges();
            return PersistUnauthorizedExitViolationSummary(
                db,
                permit,
                recordedBy,
                source,
                occurredAt,
                violationSequenceId,
                auditContext,
                stopMessage,
                stoppedCurrentState
            );
        }

        private void StopPermitForUnauthorizedExitViolation(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            string source,
            DateTime occurredAt,
            string message,
            PermitScanAuditContext? auditContext,
            string? stoppedCurrentState = null
        )
        {
            permit.ApprovalStatus = "Stopped";
            permit.ArchivedAt = occurredAt;
            permit.ExpectedReturnTime = null;
            permit.LeaveWindowStartAt = null;
            permit.LeaveWindowEndAt = null;
            permit.PendingExitRequest = false;
            permit.LeaveReason = string.Empty;
            permit.PendingUnauthorizedExitAt = null;
            permit.PendingUnauthorizedExitSequenceId = string.Empty;
            permit.CurrentState = string.IsNullOrWhiteSpace(stoppedCurrentState)
                ? PermitCurrentStateInside
                : stoppedCurrentState;

            if (!permit.ExpiresAt.HasValue || permit.ExpiresAt > occurredAt)
            {
                permit.ExpiresAt = occurredAt;
            }

            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "UnauthorizedExitStopped",
                "إيقاف بعد الخروج بدون استئذان",
                message,
                source,
                recordedBy,
                occurredAt,
                reasonCode: "UnauthorizedExitStopped",
                auditContext: BuildAuditContext(
                    auditContext,
                    source,
                    auditContext?.SequenceId,
                    "Stopped"
                )
            );
        }

        private (bool allowed, string reason) HandleFinalExit(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            DateTime occurredAt,
            string source,
            PermitScanAuditContext? auditContext
        )
        {
            PreparePermitForAccessMode(permit);
            if (HasPendingUnauthorizedExit(permit))
            {
                ClosePendingUnauthorizedExitWithoutViolation(
                    db,
                    permit,
                    recordedBy,
                    source,
                    occurredAt,
                    "أغلقت محاولة الخروج المعلقة بعد اكتمال الخروج النظامي في نهاية اليوم.",
                    auditContext
                );
            }

            permit.OutTime = occurredAt;
            permit.ReturnTime = null;
            permit.ArchivedAt = null;
            permit.ExpectedReturnTime = null;
            permit.LeaveWindowStartAt = null;
            permit.LeaveWindowEndAt = null;
            permit.PendingExitRequest = false;
            permit.LeaveReason = string.Empty;
            permit.CurrentState = PermitCurrentStateOutside;
            permit.ApprovalStatus = "Approved";

            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "ExitFinal",
                "خروج نهائي",
                $"تم تسجيل خروج نهائي لـ {permit.DriverName}.",
                source,
                recordedBy,
                occurredAt,
                reasonCode: "ExitFinal",
                auditContext: BuildAuditContext(auditContext, source, null, "Completed")
            );
            RecordLateCheckoutIfNeeded(db, permit, recordedBy, source, occurredAt, auditContext);

            db.SaveChanges();
            return (true, "ExitFinal recorded");
        }

        private void CompletePermitReturn(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            string source,
            DateTime occurredAt,
            bool isLateReturn,
            int? lateMinutes,
            DateTime returnDeadline,
            PermitScanAuditContext? auditContext
        )
        {
            var currentWorkHours = GetWorkHoursSettings();
            var archiveAfterReturn =
                _leavePolicyService.RequiresReturn(
                    permit,
                    occurredAt,
                    currentWorkHours.OfficialWorkDaysCsv
                )
                && !string.Equals(
                    permit.PermitType,
                    Permit.PermitTypePermanent,
                    StringComparison.OrdinalIgnoreCase
                );

            permit.ReturnTime = occurredAt;
            permit.ExpectedReturnTime = null;
            permit.LeaveWindowStartAt = null;
            permit.LeaveWindowEndAt = null;
            permit.PendingExitRequest = false;
            permit.LeaveReason = string.Empty;
            permit.LastLateReturnWarningForExpectedReturnTime = null;
            permit.CurrentState = PermitCurrentStateInside;
            permit.ApprovalStatus = archiveAfterReturn ? "Returned" : "Approved";

            if (archiveAfterReturn)
            {
                permit.ArchivedAt = occurredAt;
                if (!permit.ExpiresAt.HasValue || permit.ExpiresAt > occurredAt)
                {
                    permit.ExpiresAt = occurredAt;
                }
            }

            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                isLateReturn ? "LateReturn" : "Entry",
                isLateReturn ? "عودة متأخرة" : "تسجيل دخول",
                isLateReturn
                    ? $"تم تسجيل دخول {permit.DriverName} مع تأخير {DescribeDelay(occurredAt - returnDeadline)}."
                    : $"تم تسجيل دخول {permit.DriverName}.",
                source,
                recordedBy,
                occurredAt,
                reasonCode: isLateReturn ? "LateReturn" : "Entry",
                lateMinutes: lateMinutes,
                auditContext: BuildAuditContext(auditContext, source, null, "Completed")
            );
            if (!isLateReturn)
            {
                RecordLateAttendanceIfNeeded(
                    db,
                    permit,
                    recordedBy,
                    source,
                    occurredAt,
                    auditContext
                );
            }
        }

        private void SynchronizePermitStates(ApplicationDbContext db)
        {
            var now = _systemClock.LocalNow;
            var workHours = GetWorkHoursSettings();
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
                        ResetPenaltyStateForFullAccess(permit);
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
    }
}
