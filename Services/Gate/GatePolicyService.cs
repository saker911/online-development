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

namespace VehiclePermitSystemWeb.Services.Gate
{
    public sealed class GatePolicyService : IGatePolicyService
    {
        private const string PermitCurrentStateInside = "Inside";
        private const string PermitCurrentStateOutside = "Outside";

        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ILeavePolicyService _leavePolicyService;

        public GatePolicyService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ILeavePolicyService leavePolicyService
        )
        {
            _dbContextFactory = dbContextFactory;
            _leavePolicyService = leavePolicyService;
        }

        public GateDecision ResolveDecision(Permit permit, DateTime now)
        {
            var workHours = GetWorkHoursSettings();

            if (string.Equals(permit.ApprovalStatus, "Stopped", StringComparison.OrdinalIgnoreCase))
            {
                return new GateDecision(
                    GateDecisionOutcome.DenyStopped,
                    "permit_stopped",
                    "permit_stopped"
                );
            }

            if (string.Equals(permit.ApprovalStatus, "Expired", StringComparison.OrdinalIgnoreCase))
            {
                return new GateDecision(
                    GateDecisionOutcome.DenyExpired,
                    "permit_expired",
                    "permit_expired"
                );
            }

            if (
                string.Equals(permit.ApprovalStatus, "Out", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    permit.CurrentState,
                    PermitCurrentStateOutside,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return new GateDecision(
                    GateDecisionOutcome.AllowEntry,
                    "AllowEntry",
                    "entry_allowed"
                );
            }

            if (
                !string.Equals(
                    permit.CurrentState,
                    PermitCurrentStateInside,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return new GateDecision(
                    GateDecisionOutcome.AllowEntry,
                    "AllowEntry",
                    "entry_allowed"
                );
            }

            if (permit.IsVisitorPermit)
            {
                return new GateDecision(
                    GateDecisionOutcome.AllowFinalExit,
                    "ExitFinal",
                    "final_exit_allowed"
                );
            }

            if (permit.IsPermanentPermit)
            {
                permit.NormalizeAccessModeState();
                if (permit.IsFullAccessPermit)
                {
                    return new GateDecision(
                        GateDecisionOutcome.AllowFinalExit,
                        "ExitFinal",
                        "final_exit_allowed"
                    );
                }

                if (!workHours.LeaveRequestsEnabled)
                {
                    return new GateDecision(
                        GateDecisionOutcome.AllowFinalExit,
                        "ExitRecorded",
                        "exit_allowed"
                    );
                }
            }

            if (permit.PendingExitRequest)
            {
                if (_leavePolicyService.IsLeaveWindowBeforeStart(permit, now))
                {
                    return new GateDecision(
                        GateDecisionOutcome.DenyNoPermission,
                        "LeaveWindowNotStarted",
                        "leave_window_not_started"
                    );
                }

                if (_leavePolicyService.IsLeaveWindowActive(permit, now))
                {
                    return _leavePolicyService.DoesPendingLeaveRequestRequireReturn(permit)
                        ? new GateDecision(
                            GateDecisionOutcome.AllowExit,
                            "ExitAuthorized",
                            "exit_allowed"
                        )
                        : new GateDecision(
                            GateDecisionOutcome.AllowFinalExit,
                            "ExitFinal",
                            "final_exit_allowed"
                        );
                }

                if (_leavePolicyService.IsLeaveWindowExpired(permit, now))
                {
                    return new GateDecision(
                        GateDecisionOutcome.DenyPendingReview,
                        "PendingUnauthorizedExit",
                        "pending_review",
                        CreatePendingUnauthorizedExit: true
                    );
                }
            }

            if (
                _leavePolicyService.IsDailyLeaveScheduleActive(
                    permit,
                    now,
                    workHours.OfficialWorkDaysCsv
                )
            )
            {
                return _leavePolicyService.DoesDailyLeaveScheduleRequireReturn(permit)
                    ? new GateDecision(
                        GateDecisionOutcome.AllowExit,
                        "ExitAuthorized",
                        "exit_allowed"
                    )
                    : new GateDecision(
                        GateDecisionOutcome.AllowFinalExit,
                        "ExitFinal",
                        "final_exit_allowed"
                    );
            }

            if (permit.IsPermanentPermit)
            {
                var hasExitPermission =
                    permit.TemporaryExitPermissionUntil.HasValue
                    && permit.TemporaryExitPermissionUntil.Value > now;

                if (
                    _leavePolicyService.IsWithinWorkHours(
                        now,
                        workHours.WorkStartTime,
                        workHours.WorkEndTime,
                        workHours.OfficialWorkDaysCsv
                    ) && !hasExitPermission
                )
                {
                    return HasBlockedPendingUnauthorizedExit(permit)
                        ? new GateDecision(
                            GateDecisionOutcome.DenyPendingReview,
                            "PendingUnauthorizedExit",
                            "pending_review",
                            CreatePendingUnauthorizedExit: true
                        )
                        : new GateDecision(
                            GateDecisionOutcome.WaitForWorkEndClosure,
                            "PendingUnauthorizedExit",
                            "wait_for_work_end_closure",
                            CreatePendingUnauthorizedExit: true
                        );
                }

                return new GateDecision(
                    GateDecisionOutcome.AllowFinalExit,
                    "ExitFinal",
                    "final_exit_allowed"
                );
            }

            if (permit.RequiresReturn)
            {
                return new GateDecision(
                    GateDecisionOutcome.AllowExit,
                    "ExitAuthorized",
                    "exit_allowed"
                );
            }

            return new GateDecision(
                GateDecisionOutcome.AllowFinalExit,
                "ExitFinal",
                "final_exit_allowed"
            );
        }

        public bool CanEnter(Permit permit, DateTime now)
        {
            return ResolveDecision(permit, now).Outcome == GateDecisionOutcome.AllowEntry;
        }

        public bool CanExit(Permit permit, DateTime now)
        {
            var outcome = ResolveDecision(permit, now).Outcome;
            return outcome == GateDecisionOutcome.AllowExit
                || outcome == GateDecisionOutcome.AllowFinalExit;
        }

        public bool ShouldWaitForWorkEndClosure(Permit permit, DateTime now)
        {
            return ResolveDecision(permit, now).Outcome
                == GateDecisionOutcome.WaitForWorkEndClosure;
        }

        public bool IsBlockedByPendingUnauthorizedExit(Permit permit, DateTime now)
        {
            return HasBlockedPendingUnauthorizedExit(permit)
                && permit.CurrentState == PermitCurrentStateInside;
        }

        private static bool HasBlockedPendingUnauthorizedExit(Permit permit)
        {
            return permit.PendingUnauthorizedExitAt.HasValue
                && !string.IsNullOrWhiteSpace(permit.PendingUnauthorizedExitSequenceId);
        }

        private WorkHoursSettings GetWorkHoursSettings()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var settings =
                db
                    .AdministrationSettings.AsNoTracking()
                    .OrderByDescending(x => x.Id == 1)
                    .ThenBy(x => x.Id)
                    .FirstOrDefault()
                ?? new AdministrationSettings
                {
                    WorkStartTime = new TimeOnly(8, 0),
                    WorkEndTime = new TimeOnly(16, 0),
                    LateReturnGraceMinutes = 5,
                    LeaveRequestsEnabled = false,
                    OfficialWorkDaysCsv = AdministrationWorkSchedule.DefaultOfficialWorkDaysCsv,
                };

            return new WorkHoursSettings(
                settings.WorkStartTime,
                settings.WorkEndTime,
                TimeSpan.FromMinutes(Math.Max(0, settings.LateReturnGraceMinutes)),
                settings.LeaveRequestsEnabled,
                settings.OfficialWorkDaysCsv
            );
        }

        private sealed record WorkHoursSettings(
            TimeOnly WorkStartTime,
            TimeOnly WorkEndTime,
            TimeSpan LateReturnGrace,
            bool LeaveRequestsEnabled,
            string OfficialWorkDaysCsv
        );
    }
}
