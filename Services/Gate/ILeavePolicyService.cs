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
    public interface ILeavePolicyService
    {
        bool CanExitNow(
            Permit permit,
            DateTime referenceTime,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv
        );

        bool RequiresReturn(Permit permit, DateTime referenceTime, string officialWorkDaysCsv);

        DateTime? GetExpectedReturn(
            Permit permit,
            DateTime referenceTime,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv
        );

        bool HasActiveLeave(Permit permit, DateTime referenceTime, string officialWorkDaysCsv);

        string ResolveLeaveSource(
            Permit permit,
            DateTime referenceTime,
            string officialWorkDaysCsv
        );

        bool IsLateReturn(
            Permit permit,
            DateTime referenceTime,
            TimeSpan lateReturnGrace,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv
        );

        bool IsLeaveWindowBeforeStart(Permit permit, DateTime referenceTime);

        bool IsLeaveWindowActive(Permit permit, DateTime referenceTime);

        bool IsLeaveWindowExpired(Permit permit, DateTime referenceTime);

        bool HasDailyLeaveScheduleConfigured(Permit permit);

        bool IsDailyLeaveScheduleActive(
            Permit permit,
            DateTime referenceTime,
            string officialWorkDaysCsv
        );

        bool DoesPendingLeaveRequestRequireReturn(Permit permit);

        bool DoesDailyLeaveScheduleRequireReturn(Permit permit);

        bool DoesCurrentOutingRequireReturn(Permit permit);

        bool IsWithinWorkHours(
            DateTime time,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv
        );

        DateTime BuildExpectedReturnDeadline(
            DateTime occurredAt,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv
        );

        DateTime? BuildDailyLeaveExpectedReturnTime(Permit permit, DateTime referenceTime);

        void ClearLeaveWindowState(Permit permit);

        void ClearLeaveRequestState(Permit permit);

        void ClearDailyLeaveScheduleState(Permit permit);

        void ResetLeaveState(Permit permit);
    }
}
