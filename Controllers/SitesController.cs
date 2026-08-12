using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.ViewModels.Workplace;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Workplace;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ManageAdministration)]
    public sealed class SitesController : Controller
    {
        private readonly IWorkplaceDirectoryService _workplaceDirectoryService;
        private readonly ITimeLimitedDataProtector _siteAccessProtector;

        public SitesController(
            IWorkplaceDirectoryService workplaceDirectoryService,
            IDataProtectionProvider dataProtectionProvider
        )
        {
            _workplaceDirectoryService = workplaceDirectoryService;
            _siteAccessProtector = dataProtectionProvider
                .CreateProtector("VehiclePermitSystem.SiteAccess.v1")
                .ToTimeLimitedDataProtector();
        }

        [HttpGet]
        public IActionResult Index(int? edit = null)
        {
            return View(_workplaceDirectoryService.BuildSites(edit));
        }

        [HttpGet]
        public IActionResult Details(int id)
        {
            var model = _workplaceDirectoryService.BuildSiteDetails(id);
            return model == null ? NotFound() : View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Save(WorkplaceSiteInputViewModel input)
        {
            if (!ModelState.IsValid)
            {
                return View("Index", new WorkplaceSitesViewModel
                {
                    Input = input,
                    Sites = _workplaceDirectoryService.BuildSites().Sites,
                });
            }

            if (!_workplaceDirectoryService.SaveSite(input, out var error))
            {
                ModelState.AddModelError(string.Empty, error);
                return View("Index", new WorkplaceSitesViewModel
                {
                    Input = input,
                    Sites = _workplaceDirectoryService.BuildSites().Sites,
                });
            }

            TempData["SuccessMessage"] = input.Id.HasValue
                ? "تم تحديث الموقع."
                : "تمت إضافة الموقع.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Toggle(int id)
        {
            if (_workplaceDirectoryService.ToggleSite(id, out var error))
            {
                TempData["SuccessMessage"] = "تم تحديث حالة الموقع.";
            }
            else
            {
                TempData["ErrorMessage"] = error;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            if (_workplaceDirectoryService.DeleteSite(id, out var error))
            {
                TempData["SuccessMessage"] = "تم حذف الموقع.";
            }
            else
            {
                TempData["ErrorMessage"] = error;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddEntrance(
            [Bind(Prefix = "EntranceInput")] WorkplaceSiteEntranceInputViewModel input
        )
        {
            if (!ModelState.IsValid)
            {
                var model = _workplaceDirectoryService.BuildSiteDetails(input.WorkplaceSiteId);
                if (model == null)
                {
                    return NotFound();
                }

                model.EntranceInput = input;
                return View("Details", model);
            }

            if (!_workplaceDirectoryService.SaveEntrance(input, out var error))
            {
                TempData["ErrorMessage"] = error;
            }
            else
            {
                TempData["SuccessMessage"] = "تمت إضافة المدخل.";
            }

            return RedirectToAction(nameof(Details), new { id = input.WorkplaceSiteId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleEntrance(int siteId, int entranceId)
        {
            if (_workplaceDirectoryService.ToggleEntrance(siteId, entranceId, out var error))
            {
                TempData["SuccessMessage"] = "تم تحديث حالة المدخل.";
            }
            else
            {
                TempData["ErrorMessage"] = error;
            }

            return RedirectToAction(nameof(Details), new { id = siteId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteEntrance(int siteId, int entranceId)
        {
            if (_workplaceDirectoryService.DeleteEntrance(siteId, entranceId, out var error))
            {
                TempData["SuccessMessage"] = "تم حذف المدخل.";
            }
            else
            {
                TempData["ErrorMessage"] = error;
            }

            return RedirectToAction(nameof(Details), new { id = siteId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AssignDevice(
            [Bind(Prefix = "DeviceAssignment")] WorkplaceSiteDeviceAssignmentViewModel input
        )
        {
            if (_workplaceDirectoryService.AssignDevice(input, out var error))
            {
                TempData["SuccessMessage"] = "تم ربط الجهاز بالموقع.";
            }
            else
            {
                TempData["ErrorMessage"] = error;
            }

            return RedirectToAction(nameof(Details), new { id = input.WorkplaceSiteId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UnassignDevice(int siteId, int deviceId)
        {
            if (_workplaceDirectoryService.UnassignDevice(siteId, deviceId, out var error))
            {
                TempData["SuccessMessage"] = "تم إلغاء إسناد الجهاز.";
            }
            else
            {
                TempData["ErrorMessage"] = error;
            }

            return RedirectToAction(nameof(Details), new { id = siteId });
        }

        [HttpGet]
        public IActionResult RotatingQr(int id)
        {
            var site = _workplaceDirectoryService.BuildSiteDetails(id);
            if (site == null || !site.Site.IsActive || !site.Site.SelfServiceEnabled)
            {
                return NotFound();
            }

            var payload = $"{id}|{Guid.NewGuid():N}";
            var token = _siteAccessProtector.Protect(payload, TimeSpan.FromMinutes(2));
            var target = Url.Action(
                nameof(ValidateAccess),
                "Sites",
                new { token },
                Request.Scheme
            ) ?? token;
            return File(
                VehiclePermitSystemWeb.Utilities.Barcodes.BarcodePngRenderer.Render(
                    target,
                    ZXing.BarcodeFormat.QR_CODE,
                    360,
                    360,
                    2
                ),
                "image/png"
            );
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult ValidateAccess(string token)
        {
            try
            {
                var payload = _siteAccessProtector.Unprotect(token, out var expiresAt);
                var separator = payload.IndexOf('|');
                if (separator <= 0 || !int.TryParse(payload[..separator], out var siteId))
                {
                    return BadRequest(new { valid = false });
                }
                var site = _workplaceDirectoryService.BuildSiteDetails(siteId);
                if (site == null || !site.Site.IsActive || !site.Site.SelfServiceEnabled)
                {
                    return NotFound(new { valid = false });
                }
                return Json(new
                {
                    valid = true,
                    siteId,
                    siteName = site.Site.Name,
                    expiresAtUtc = expiresAt.UtcDateTime,
                });
            }
            catch
            {
                return BadRequest(new { valid = false, message = "انتهت صلاحية الرمز." });
            }
        }
    }
}
