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
    public sealed class LeavePolicyService : ILeavePolicyService
    {
        public bool CanExitNow(
            Permit permit,
            DateTime referenceTime,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv
        )
        {
            if (
                !permit.IsPermanentPermit
                || !string.Equals(permit.CurrentState, "Inside", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            permit.NormalizeAccessModeState();

            if (permit.IsFullAccessPermit)
            {
                return true;
            }

            if (RequiresReturn(permit, referenceTime, officialWorkDaysCsv))
            {
                return true;
            }

            return HasActiveLeave(permit, referenceTime, officialWorkDaysCsv)
                || !IsWithinWorkHours(
                    referenceTime,
                    workStartTime,
                    workEndTime,
                    officialWorkDaysCsv
                );
        }

        public bool RequiresReturn(
            Permit permit,
            DateTime referenceTime,
            string officialWorkDaysCsv
        )
        {
            var leaveSource = ResolveLeaveSource(permit, referenceTime, officialWorkDaysCsv);
            return string.Equals(leaveSource, "LeaveRequest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(leaveSource, "DailySchedule", StringComparison.OrdinalIgnoreCase)
                || (!permit.IsPermanentPermit && permit.RequiresReturn);
        }

        public DateTime? GetExpectedReturn(
            Permit permit,
            DateTime referenceTime,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv
        )
        {
            if (permit.ExpectedReturnTime.HasValue)
            {
                return permit.ExpectedReturnTime;
            }

            if (!RequiresReturn(permit, referenceTime, officialWorkDaysCsv))
            {
                return null;
            }

            var leaveSource = ResolveLeaveSource(permit, referenceTime, officialWorkDaysCsv);
            if (string.Equals(leaveSource, "DailySchedule", StringComparison.OrdinalIgnoreCase))
            {
                return BuildDailyLeaveExpectedReturnTime(permit, referenceTime);
            }

            return BuildExpectedReturnDeadline(
                referenceTime,
                workStartTime,
                workEndTime,
                officialWorkDaysCsv
            );
        }

        public bool HasActiveLeave(
            Permit permit,
            DateTime referenceTime,
            string officialWorkDaysCsv
        )
        {
            var leaveSource = ResolveLeaveSource(permit, referenceTime, officialWorkDaysCsv);
            return !string.Equals(leaveSource, "WorkHours", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(leaveSource, "None", StringComparison.OrdinalIgnoreCase);
        }

        public string ResolveLeaveSource(
            Permit permit,
            DateTime referenceTime,
            string officialWorkDaysCsv
        )
        {
            if (permit.PendingExitRequest && IsLeaveWindowActive(permit, referenceTime))
            {
                return "LeaveRequest";
            }

            if (IsDailyLeaveScheduleActive(permit, referenceTime, officialWorkDaysCsv))
            {
                return "DailySchedule";
            }

            if (
                permit.TemporaryExitPermissionUntil.HasValue
                && permit.TemporaryExitPermissionUntil.Value > referenceTime
            )
            {
                return "TemporaryPermission";
            }

            if (permit.IsPermanentPermit)
            {
                return "None";
            }

            if (permit.RequiresReturn)
            {
                return "WorkHours";
            }

            return "None";
        }

        public bool IsLateReturn(
            Permit permit,
            DateTime referenceTime,
            TimeSpan lateReturnGrace,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv
        )
        {
            if (!permit.ExpectedReturnTime.HasValue)
            {
                return false;
            }

            var expectedReturn = GetExpectedReturn(
                permit,
                referenceTime,
                workStartTime,
                workEndTime,
                officialWorkDaysCsv
            );

            return expectedReturn.HasValue
                && referenceTime > expectedReturn.Value.Add(lateReturnGrace)
                && IsWithinWorkHours(
                    referenceTime,
                    workStartTime,
                    workEndTime,
                    officialWorkDaysCsv
                );
        }

        public bool IsLeaveWindowBeforeStart(Permit permit, DateTime referenceTime)
        {
            return permit.LeaveWindowStartAt.HasValue
                && referenceTime < permit.LeaveWindowStartAt.Value;
        }

        public bool IsLeaveWindowActive(Permit permit, DateTime referenceTime)
        {
            if (!permit.LeaveWindowStartAt.HasValue)
            {
                return false;
            }

            if (!permit.LeaveWindowEndAt.HasValue)
            {
                return referenceTime >= permit.LeaveWindowStartAt.Value;
            }

            return referenceTime >= permit.LeaveWindowStartAt.Value
                && referenceTime <= permit.LeaveWindowEndAt.Value;
        }

        public bool IsLeaveWindowExpired(Permit permit, DateTime referenceTime)
        {
            return permit.LeaveWindowEndAt.HasValue
                && referenceTime > permit.LeaveWindowEndAt.Value;
        }

        public bool HasDailyLeaveScheduleConfigured(Permit permit)
        {
            return permit.HasDailyLeaveSchedule
                && permit.DailyLeaveScheduleStartDate.HasValue
                && permit.DailyLeaveScheduleEndDate.HasValue
                && permit.DailyLeaveScheduleExitMinutes.HasValue;
        }

        public bool IsDailyLeaveScheduleActive(
            Permit permit,
            DateTime referenceTime,
            string officialWorkDaysCsv
        )
        {
            if (!HasDailyLeaveScheduleConfigured(permit))
            {
                return false;
            }

            if (!AdministrationWorkSchedule.IsOfficialWorkDay(referenceTime, officialWorkDaysCsv))
            {
                return false;
            }

            var currentDate = referenceTime.Date;
            if (
                currentDate < permit.DailyLeaveScheduleStartDate!.Value.Date
                || currentDate > permit.DailyLeaveScheduleEndDate!.Value.Date
            )
            {
                return false;
            }

            var dailyExitTime = TimeSpan.FromMinutes(permit.DailyLeaveScheduleExitMinutes!.Value);
            if (referenceTime.TimeOfDay < dailyExitTime)
            {
                return false;
            }

            if (!permit.DailyLeaveScheduleRequiresReturn)
            {
                return true;
            }

            if (!permit.DailyLeaveScheduleReturnMinutes.HasValue)
            {
                return false;
            }

            var dailyReturnTime = TimeSpan.FromMinutes(
                permit.DailyLeaveScheduleReturnMinutes.Value
            );
            return referenceTime.TimeOfDay <= dailyReturnTime;
        }

        public bool DoesPendingLeaveRequestRequireReturn(Permit permit)
        {
            return permit.PendingExitRequest && permit.ExpectedReturnTime.HasValue;
        }

        public bool DoesDailyLeaveScheduleRequireReturn(Permit permit)
        {
            return HasDailyLeaveScheduleConfigured(permit)
                && permit.DailyLeaveScheduleRequiresReturn;
        }

        public bool DoesCurrentOutingRequireReturn(Permit permit)
        {
            return permit.ExpectedReturnTime.HasValue;
        }

        public bool IsWithinWorkHours(
            DateTime time,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv
        )
        {
            return AdministrationWorkSchedule.IsWithinWorkHours(
                time,
                workStartTime,
                workEndTime,
                officialWorkDaysCsv
            );
        }

        public DateTime BuildExpectedReturnDeadline(
            DateTime occurredAt,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv
        )
        {
            return AdministrationWorkSchedule.GetExpectedShiftEnd(
                occurredAt,
                workStartTime,
                workEndTime,
                officialWorkDaysCsv
            );
        }

        public DateTime? BuildDailyLeaveExpectedReturnTime(Permit permit, DateTime referenceTime)
        {
            if (
                !permit.DailyLeaveScheduleRequiresReturn
                || !permit.DailyLeaveScheduleReturnMinutes.HasValue
            )
            {
                return null;
            }

            return referenceTime.Date.AddMinutes(permit.DailyLeaveScheduleReturnMinutes.Value);
        }

        public void ClearLeaveWindowState(Permit permit)
        {
            permit.ExpectedReturnTime = null;
            permit.LeaveWindowStartAt = null;
            permit.LeaveWindowEndAt = null;
            permit.PendingExitRequest = false;
            permit.LeaveReason = string.Empty;
        }

        public void ClearLeaveRequestState(Permit permit)
        {
            ClearLeaveWindowState(permit);
            permit.TemporaryExitPermissionUntil = null;
            permit.LastLateReturnWarningForExpectedReturnTime = null;
        }

        public void ClearDailyLeaveScheduleState(Permit permit)
        {
            permit.HasDailyLeaveSchedule = false;
            permit.DailyLeaveScheduleStartDate = null;
            permit.DailyLeaveScheduleEndDate = null;
            permit.DailyLeaveScheduleExitMinutes = null;
            permit.DailyLeaveScheduleReturnMinutes = null;
            permit.DailyLeaveScheduleRequiresReturn = false;
            permit.DailyLeaveScheduleReason = string.Empty;
        }

        public void ResetLeaveState(Permit permit)
        {
            ClearLeaveRequestState(permit);
            ClearDailyLeaveScheduleState(permit);
        }
    }
}
