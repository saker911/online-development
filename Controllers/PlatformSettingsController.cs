using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.ViewModels.Platform;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize]
    public sealed class PlatformSettingsController : Controller
    {
        private readonly IPlatformSettingsService _platformSettingsService;

        public PlatformSettingsController(IPlatformSettingsService platformSettingsService)
        {
            _platformSettingsService = platformSettingsService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            return View(_platformSettingsService.Get());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(PlatformSettingsViewModel model)
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            _platformSettingsService.Update(model);
            TempData["SuccessMessage"] = "تم حفظ بيانات المنشأة والتواصل.";
            return RedirectToAction(nameof(Index));
        }
    }
}
