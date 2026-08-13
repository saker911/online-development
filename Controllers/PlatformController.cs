using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Tenants;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize]
    public sealed class PlatformController : Controller
    {
        private readonly ITenantManagementService _tenantManagementService;

        public PlatformController(ITenantManagementService tenantManagementService)
        {
            _tenantManagementService = tenantManagementService;
        }

        [HttpGet("/Platform")]
        public IActionResult Index()
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            return View(_tenantManagementService.GetDashboard());
        }
    }
}
