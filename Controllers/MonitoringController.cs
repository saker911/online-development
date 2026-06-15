using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Monitoring;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ViewDashboard)]
    public sealed class MonitoringController : Controller
    {
        private readonly IMonitoringDashboardService _monitoringDashboardService;

        public MonitoringController(IMonitoringDashboardService monitoringDashboardService)
        {
            _monitoringDashboardService = monitoringDashboardService;
        }

        [HttpGet]
        public IActionResult Index(string range = "today")
        {
            return View(_monitoringDashboardService.BuildDashboard(range));
        }

        [HttpGet]
        public IActionResult Snapshot(string range = "today")
        {
            return PartialView(
                "_MonitoringDashboardContent",
                _monitoringDashboardService.BuildDashboard(range)
            );
        }
    }
}
