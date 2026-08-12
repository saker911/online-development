using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Workplace;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize]
    public sealed class AttendanceController : Controller
    {
        private readonly IWorkplaceDirectoryService _workplaceDirectoryService;

        public AttendanceController(IWorkplaceDirectoryService workplaceDirectoryService)
        {
            _workplaceDirectoryService = workplaceDirectoryService;
        }

        [HttpGet]
        public IActionResult Index(string? query = null, string? personType = null)
        {
            if (!CanViewAttendance())
            {
                return Forbid();
            }

            return View(
                _workplaceDirectoryService.BuildAttendanceDashboard(query, personType)
            );
        }

        [HttpGet]
        public IActionResult Snapshot(string? query = null, string? personType = null)
        {
            if (!CanViewAttendance())
            {
                return Forbid();
            }

            return PartialView(
                "_AttendanceContent",
                _workplaceDirectoryService.BuildAttendanceDashboard(query, personType)
            );
        }

        private bool CanViewAttendance() =>
            User.HasPermission(AppPermissions.ViewDashboard)
            || User.HasPermission(AppPermissions.ScanOperations)
            || User.HasPermission(AppPermissions.ViewPermits)
            || User.HasPermission(AppPermissions.ViewVisits);
    }
}
