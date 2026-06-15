using VehiclePermitSystemWeb.Models.ViewModels.Monitoring;

namespace VehiclePermitSystemWeb.Services.Monitoring
{
    public interface IMonitoringDashboardService
    {
        MonitoringDashboardViewModel BuildDashboard(string? range);
    }
}
