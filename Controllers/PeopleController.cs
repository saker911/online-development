using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Uploads;
using VehiclePermitSystemWeb.Services.Workplace;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize]
    public sealed class PeopleController : Controller
    {
        private readonly IWorkplaceDirectoryService _workplaceDirectoryService;
        private readonly IUploadThreatScanner? _uploadThreatScanner;

        public PeopleController(
            IWorkplaceDirectoryService workplaceDirectoryService,
            IUploadThreatScanner? uploadThreatScanner = null
        )
        {
            _workplaceDirectoryService = workplaceDirectoryService;
            _uploadThreatScanner = uploadThreatScanner;
        }

        [HttpGet]
        public IActionResult Index(
            string? query = null,
            string? personType = null,
            int page = 1,
            int pageSize = 12
        )
        {
            if (!CanViewPeople())
            {
                return Forbid();
            }

            return View(
                _workplaceDirectoryService.BuildPeopleDirectory(
                    query,
                    personType,
                    page,
                    pageSize
                )
            );
        }

        [HttpGet]
        public IActionResult Details(long id)
        {
            if (!CanViewPeople())
            {
                return Forbid();
            }

            var model = _workplaceDirectoryService.BuildPersonDetails(id);
            return model == null ? NotFound() : View(model);
        }

        [HttpGet]
        public IActionResult Photo(long id)
        {
            if (!CanViewPeople())
            {
                return Forbid();
            }

            var photo = _workplaceDirectoryService.GetPersonPhoto(id);
            if (photo == null || photo.Data.Length == 0)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(photo.Data, photo.ContentType);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
        public async Task<IActionResult> UploadPhoto(
            long id,
            IFormFile? photo,
            CancellationToken cancellationToken
        )
        {
            if (!CanManagePeoplePhotos())
            {
                return Forbid();
            }

            if (photo == null)
            {
                TempData["ErrorMessage"] = "اختر صورة صالحة أولاً.";
                return RedirectToAction(nameof(Details), new { id });
            }

            try
            {
                var processed = await AdministrationImageStorage.ProcessAsync(
                    photo,
                    _uploadThreatScanner,
                    cancellationToken
                );
                var saved = _workplaceDirectoryService.SavePersonPhoto(
                    id,
                    processed.Data,
                    processed.ContentType,
                    out var error
                );
                TempData[saved ? "SuccessMessage" : "ErrorMessage"] = saved
                    ? "تم تحديث صورة الشخص."
                    : error;
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeletePhoto(long id)
        {
            if (!CanManagePeoplePhotos())
            {
                return Forbid();
            }

            var deleted = _workplaceDirectoryService.DeletePersonPhoto(id, out var error);
            TempData[deleted ? "SuccessMessage" : "ErrorMessage"] = deleted
                ? "تم حذف صورة الشخص."
                : error;
            return RedirectToAction(nameof(Details), new { id });
        }

        private bool CanViewPeople() =>
            User.HasPermission(AppPermissions.ViewDashboard)
            || User.HasPermission(AppPermissions.ViewPermits)
            || User.HasPermission(AppPermissions.ViewVisitorPermits)
            || User.HasPermission(AppPermissions.ViewVisits)
            || User.HasPermission(AppPermissions.ManageUsers);

        private bool CanManagePeoplePhotos() =>
            User.HasPermission(AppPermissions.ManageUsers)
            || User.HasPermission(AppPermissions.ManageAdministration)
            || User.HasPermission(AppPermissions.CreateVisit)
            || User.HasPermission(AppPermissions.EditVisit);
    }
}
