namespace VehiclePermitSystemWeb.Services.Permits
{
    public interface IPermitLifecycleService
    {
        bool StopPermit(string permitNumber, string? performedBy = null);

        bool ReactivatePermit(string permitNumber, string? performedBy = null);

        bool ClearDailyLeaveSchedule(string permitNumber, string? performedBy = null);

        bool SubmitLeaveRequest(
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
        );
    }
}
