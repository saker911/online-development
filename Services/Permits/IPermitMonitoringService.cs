using VehiclePermitSystemWeb.Data;

namespace VehiclePermitSystemWeb.Services.Permits
{
    public interface IPermitMonitoringService
    {
        int HandleReturnMonitoring(
            ApplicationDbContext db,
            DateTime referenceTime,
            string? performedBy,
            string source,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv,
            TimeSpan lateReturnGrace
        );
    }
}
