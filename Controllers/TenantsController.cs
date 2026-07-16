using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.ViewModels.Tenants;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Tenants;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize]
    public sealed class TenantsController : Controller
    {
        private readonly ITenantManagementService _tenantManagementService;

        public TenantsController(ITenantManagementService tenantManagementService)
        {
            _tenantManagementService = tenantManagementService;
        }

        public IActionResult Index(string? edit = null)
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            var model = _tenantManagementService.GetDashboard();
            if (!string.IsNullOrWhiteSpace(edit))
            {
                model.Editor = _tenantManagementService.GetEditor(edit) ?? new TenantEditorViewModel();
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(TenantEditorViewModel editor)
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            if (!ModelState.IsValid)
            {
                var model = _tenantManagementService.GetDashboard();
                model.Editor = editor;
                return View(nameof(Index), model);
            }

            var result = _tenantManagementService.CreateTenant(editor);
            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Update(string id, TenantEditorViewModel editor)
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            if (!ModelState.IsValid)
            {
                var model = _tenantManagementService.GetDashboard();
                model.Editor = editor;
                return View(nameof(Index), model);
            }

            var result = _tenantManagementService.UpdateTenant(id, editor);
            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return result.Succeeded
                ? RedirectToAction(nameof(Index))
                : RedirectToAction(nameof(Index), new { edit = id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetActive(string id, bool isActive)
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            var result = _tenantManagementService.SetTenantActive(id, isActive);
            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ActivatePayment(string id)
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            var result = _tenantManagementService.ActivatePaidSubscription(id);
            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
        }
    }
}
