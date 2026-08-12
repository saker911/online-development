using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Workplace;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ManageAdministration)]
    public sealed class EmergencyController : Controller
    {
        private readonly IEmergencyService _emergencyService;

        public EmergencyController(IEmergencyService emergencyService)
        {
            _emergencyService = emergencyService;
        }

        [HttpGet]
        public IActionResult Index(int? siteId = null) =>
            View(_emergencyService.BuildDashboard(siteId));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Start(int siteId)
        {
            if (!_emergencyService.StartSession(siteId, User.Identity?.Name ?? string.Empty, out var error))
            {
                TempData["ErrorMessage"] = error;
            }
            else
            {
                TempData["SuccessMessage"] = "تم بدء جلسة الطوارئ وحفظ قائمة الموجودين.";
            }
            return RedirectToAction(nameof(Index), new { siteId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateMember(int sessionId, int memberId, string status, int siteId)
        {
            var success = _emergencyService.UpdateMember(
                sessionId,
                memberId,
                status,
                User.Identity?.Name ?? string.Empty,
                out var error
            );
            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success, message = error });
            }
            if (!success)
            {
                TempData["ErrorMessage"] = error;
            }
            return RedirectToAction(nameof(Index), new { siteId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Complete(int sessionId, int siteId)
        {
            if (!_emergencyService.CompleteSession(
                    sessionId,
                    User.Identity?.Name ?? string.Empty,
                    out var error))
            {
                TempData["ErrorMessage"] = error;
            }
            else
            {
                TempData["SuccessMessage"] = "تم إنهاء جلسة الطوارئ وحفظ نتائجها.";
            }
            return RedirectToAction(nameof(Index), new { siteId });
        }
    }
}
