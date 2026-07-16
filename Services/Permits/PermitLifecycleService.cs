using System.Globalization;
using Microsoft.EntityFrameworkCore;
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
    public sealed class PermitLifecycleService : IPermitLifecycleService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;
        private readonly ILeavePolicyService _leavePolicyService;
        private readonly IPermitAuditService _permitAuditService;

        public PermitLifecycleService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock,
            ILeavePolicyService leavePolicyService,
            IPermitAuditService permitAuditService
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
            _leavePolicyService = leavePolicyService;
            _permitAuditService = permitAuditService;
        }

        public bool StopPermit(string permitNumber, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    var permit = db.Permits.FirstOrDefault(p => p.PermitNumber == permitNumber);
                    if (permit == null || !CanStopPermit(permit, _systemClock.LocalNow))
                    {
                        return false;
                    }

                    permit.NormalizeAccessModeState();

                    permit.ApprovalStatus = "Stopped";
                    permit.TemporaryExitPermissionUntil = null;
                    _leavePolicyService.ResetLeaveState(permit);
                    permit.LateReturnWarningCount = 0;
                    permit.LastLateReturnWarningForExpectedReturnTime = null;

                    _permitAuditService.RecordPermitActivity(
                        db,
                        permit,
                        "Stopped",
                        "إيقاف التصريح",
                        $"تم إيقاف التصريح الخاص بـ {permit.DriverName}",
                        "النظام",
                        performedBy,
                        _systemClock.LocalNow
                    );
                    _permitAuditService.RecordUserActivity(
                        db,
                        performedBy ?? "system",
                        permit.DriverName,
                        "PermitStopped",
                        "إيقاف تصريح",
                        $"تم إيقاف التصريح الخاص بـ {permit.DriverName}.",
                        "النظام",
                        performedBy,
                        _systemClock.LocalNow
                    );

                    db.SaveChanges();
                    return true;
                }
            );
        }

        public bool ReactivatePermit(string permitNumber, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    var permit = db.Permits.FirstOrDefault(p => p.PermitNumber == permitNumber);
                    if (permit == null || !CanReactivatePermit(permit))
                    {
                        return false;
                    }

                    permit.NormalizeAccessModeState();

                    var settings = GetAdministrationSettings(db);
                    permit.ApprovalStatus = "Approved";
                    permit.ArchivedAt = null;
                    permit.CurrentState = "Outside";
                    permit.OutTime = null;
                    permit.ReturnTime = null;
                    permit.TemporaryExitPermissionUntil = null;
                    _leavePolicyService.ResetLeaveState(permit);
                    permit.LateReturnWarningCount = 0;
                    permit.LastLateReturnWarningForExpectedReturnTime = null;
                    permit.LastAutomaticWorkEndExitAt = null;

                    if (
                        !permit.ExpiresAt.HasValue
                        || permit.ExpiresAt.Value <= _systemClock.LocalNow
                    )
                    {
                        permit.ExpiresAt = permit.IsVisitorPermit
                            ? CalculateVisitorPermitExpiration(_systemClock.LocalNow, settings)
                            : _systemClock.LocalNow.AddDays(1);
                    }

                    if (string.IsNullOrWhiteSpace(permit.QrToken))
                    {
                        permit.QrToken = PermitQrTokenGenerator.Generate(permit);
                    }

                    _permitAuditService.RecordPermitActivity(
                        db,
                        permit,
                        "Reactivated",
                        "إعادة تفعيل التصريح",
                        $"تمت إعادة تفعيل التصريح الخاص بـ {permit.DriverName}",
                        "النظام",
                        performedBy,
                        _systemClock.LocalNow
                    );

                    db.SaveChanges();
                    return true;
                }
            );
        }

        public bool ClearDailyLeaveSchedule(string permitNumber, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    var permit = db.Permits.FirstOrDefault(p => p.PermitNumber == permitNumber);
                    if (permit == null || !CanClearDailyLeaveSchedule(permit))
                    {
                        return false;
                    }

                    var scheduleSummary = permit.DailyLeaveScheduleRequiresReturn
                        ? "خروج وعودة يومية"
                        : "خروج يومي بدون عودة";

                    _leavePolicyService.ClearDailyLeaveScheduleState(permit);

                    _permitAuditService.RecordPermitActivity(
                        db,
                        permit,
                        "DailyScheduleCleared",
                        "إلغاء الجدولة اليومية",
                        $"تم إلغاء الجدولة اليومية ({scheduleSummary}) للتصريح الخاص بـ {permit.DriverName}.",
                        "PermitsController",
                        performedBy,
                        _systemClock.LocalNow,
                        reasonCode: "DailyScheduleCleared"
                    );

                    db.SaveChanges();
                    return true;
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
            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    var permit = db.Permits.FirstOrDefault(p => p.PermitNumber == permitNumber);
                    if (permit != null)
                    {
                        permit.NormalizeAccessModeState();
                    }

                    if (
                        permit == null
                        || permit.ArchivedAt.HasValue
                        || !permit.IsPermanentPermit
                        || permit.IsFullAccessPermit
                        || !string.Equals(
                            permit.ApprovalStatus,
                            "Approved",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || !string.Equals(
                            permit.CurrentState,
                            "Inside",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return false;
                    }

                    var normalizedReason = (leaveReason ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(normalizedReason))
                    {
                        return false;
                    }

                    if (isDailySchedule)
                    {
                        if (
                            !scheduleStartDate.HasValue
                            || !scheduleEndDate.HasValue
                            || scheduleEndDate.Value.Date < scheduleStartDate.Value.Date
                            || !dailyExitMinutes.HasValue
                            || dailyExitMinutes.Value < 0
                            || dailyExitMinutes.Value >= (24 * 60)
                        )
                        {
                            return false;
                        }

                        if (
                            requiresReturn
                            && (
                                !dailyReturnMinutes.HasValue
                                || dailyReturnMinutes.Value <= dailyExitMinutes.Value
                                || dailyReturnMinutes.Value >= (24 * 60)
                            )
                        )
                        {
                            return false;
                        }

                        permit.ExpectedReturnTime = null;
                        permit.LeaveWindowStartAt = null;
                        permit.LeaveWindowEndAt = null;
                        permit.PendingExitRequest = false;
                        permit.LeaveReason = string.Empty;
                        permit.HasDailyLeaveSchedule = true;
                        permit.DailyLeaveScheduleStartDate = scheduleStartDate.Value.Date;
                        permit.DailyLeaveScheduleEndDate = scheduleEndDate.Value.Date;
                        permit.DailyLeaveScheduleExitMinutes = dailyExitMinutes;
                        permit.DailyLeaveScheduleReturnMinutes = requiresReturn
                            ? dailyReturnMinutes
                            : null;
                        permit.DailyLeaveScheduleRequiresReturn = requiresReturn;
                        permit.DailyLeaveScheduleReason = normalizedReason;
                        permit.TemporaryExitPermissionUntil = null;
                        permit.LastLateReturnWarningForExpectedReturnTime = null;
                        var scheduleStartLabel = scheduleStartDate.Value.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture
                        );
                        var scheduleEndLabel = scheduleEndDate.Value.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture
                        );
                        var exitTimeLabel = TimeSpan
                            .FromMinutes(dailyExitMinutes.Value)
                            .ToString(@"hh\:mm", CultureInfo.InvariantCulture);
                        var returnTimeLabel =
                            requiresReturn && dailyReturnMinutes.HasValue
                                ? TimeSpan
                                    .FromMinutes(dailyReturnMinutes.Value)
                                    .ToString(@"hh\:mm", CultureInfo.InvariantCulture)
                                : null;

                        _permitAuditService.RecordPermitActivity(
                            db,
                            permit,
                            requiresReturn
                                ? "DailyScheduleWithReturnConfigured"
                                : "DailyScheduleWithoutReturnConfigured",
                            requiresReturn ? "جدولة خروج وعودة يومية" : "جدولة خروج يومي بدون عودة",
                            requiresReturn
                                ? $"تم اعتماد جدولة يومية للتصريح رقم {permit.PermitNumber} من {scheduleStartLabel} إلى {scheduleEndLabel} بخروج يومي عند {exitTimeLabel} وعودة يومية عند {returnTimeLabel} بسبب: {normalizedReason}."
                                : $"تم اعتماد جدولة خروج يومي نهائي للتصريح رقم {permit.PermitNumber} من {scheduleStartLabel} إلى {scheduleEndLabel} ابتداءً من {exitTimeLabel} بسبب: {normalizedReason}.",
                            "PermitsController",
                            performedBy,
                            _systemClock.LocalNow,
                            reasonCode: requiresReturn
                                ? "DailyScheduleWithReturnConfigured"
                                : "DailyScheduleWithoutReturnConfigured"
                        );

                        db.SaveChanges();
                        return true;
                    }

                    if (!leaveStartAt.HasValue || leaveStartAt <= _systemClock.LocalNow)
                    {
                        return false;
                    }

                    if (
                        requiresReturn
                        && (
                            !leaveEndAt.HasValue
                            || leaveEndAt <= leaveStartAt
                            || leaveEndAt <= _systemClock.LocalNow
                        )
                    )
                    {
                        return false;
                    }

                    permit.ExpectedReturnTime = requiresReturn ? leaveEndAt : null;
                    permit.LeaveWindowStartAt = leaveStartAt;
                    permit.LeaveWindowEndAt = requiresReturn ? leaveEndAt : null;
                    permit.PendingExitRequest = true;
                    permit.LeaveReason = normalizedReason;
                    permit.TemporaryExitPermissionUntil = null;
                    permit.LastLateReturnWarningForExpectedReturnTime = null;

                    _permitAuditService.RecordPermitActivity(
                        db,
                        permit,
                        requiresReturn ? "LeaveRequestWithReturn" : "LeaveRequestWithoutReturn",
                        requiresReturn ? "استئذان خروج وعودة" : "استئذان خروج بدون عودة",
                        requiresReturn
                            ? $"تم اعتماد استئذان من {leaveStartAt:O} إلى {leaveEndAt:O} للتصريح رقم {permit.PermitNumber} بسبب: {normalizedReason}."
                            : $"تم اعتماد استئذان خروج نهائي يبدأ من {leaveStartAt:O} للتصريح رقم {permit.PermitNumber} بسبب: {normalizedReason}.",
                        "PermitsController",
                        performedBy,
                        _systemClock.LocalNow,
                        reasonCode: requiresReturn
                            ? "LeaveRequestWithReturn"
                            : "LeaveRequestWithoutReturn"
                    );

                    db.SaveChanges();
                    return true;
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

        private static bool CanStopPermit(Permit permit, DateTime localNow)
        {
            return !permit.ArchivedAt.HasValue
                && string.Equals(
                    permit.ApprovalStatus,
                    "Approved",
                    StringComparison.OrdinalIgnoreCase
                )
                && (!permit.ExpiresAt.HasValue || permit.ExpiresAt.Value > localNow);
        }

        private static bool CanReactivatePermit(Permit permit)
        {
            return string.Equals(
                    permit.ApprovalStatus,
                    "Stopped",
                    StringComparison.OrdinalIgnoreCase
                ) || permit.ArchivedAt.HasValue;
        }

        private static bool CanClearDailyLeaveSchedule(Permit permit)
        {
            return permit.IsPermanentPermit
                && permit.HasDailyLeaveSchedule
                && !permit.ArchivedAt.HasValue
                && !permit.PendingExitRequest
                && string.Equals(
                    permit.ApprovalStatus,
                    "Approved",
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(permit.CurrentState, "Inside", StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime CalculateVisitorPermitExpiration(
            DateTime referenceTime,
            AdministrationSettings settings
        )
        {
            return AdministrationWorkSchedule.GetExpectedShiftEnd(referenceTime, settings);
        }

        private static AdministrationSettings GetAdministrationSettings(ApplicationDbContext db)
        {
            return db
                    .AdministrationSettings.AsNoTracking()
                    .OrderByDescending(x => x.Id == 1)
                    .ThenBy(x => x.Id)
                    .FirstOrDefault()
                ?? new AdministrationSettings
                {
                    WorkStartTime = new TimeOnly(8, 0),
                    WorkEndTime = new TimeOnly(16, 0),
                    LateReturnGraceMinutes = 5,
                    OfficialWorkDaysCsv = AdministrationWorkSchedule.DefaultOfficialWorkDaysCsv,
                };
        }
    }
}
