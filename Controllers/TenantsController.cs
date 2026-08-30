using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.ViewModels.Tenants;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Services.Users;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize]
    public sealed class TenantsController : Controller
    {
        private readonly ITenantManagementService _tenantManagementService;
        private readonly IAccountPasswordResetService? _passwordResetService;

        public TenantsController(
            ITenantManagementService tenantManagementService,
            IAccountPasswordResetService? passwordResetService = null
        )
        {
            _tenantManagementService = tenantManagementService;
            _passwordResetService = passwordResetService;
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
        public async Task<IActionResult> Create(
            TenantEditorViewModel editor,
            CancellationToken cancellationToken
        )
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

            if (_passwordResetService?.IsDeliveryConfigured != true)
            {
                var model = _tenantManagementService.GetDashboard();
                model.Editor = editor;
                ModelState.AddModelError(
                    string.Empty,
                    "يجب تهيئة بريد المنصة قبل إنشاء جهة حتى يصل رابط تعيين كلمة المرور لمسؤولها."
                );
                return View(nameof(Index), model);
            }

            var result = _tenantManagementService.CreateTenant(editor);
            if (
                result.Succeeded
                && !string.IsNullOrWhiteSpace(result.OwnerUsername)
                && _passwordResetService?.IsDeliveryConfigured == true
            )
            {
                var challenge = _passwordResetService.CreateChallenge(
                    result.TenantId,
                    result.OwnerUsername
                );
                var resetUrl = challenge == null
                    ? string.Empty
                    : Url.Action(
                        "ResetPassword",
                        "Account",
                        new { token = challenge.Token },
                        Request.Scheme
                    );
                if (challenge != null && !string.IsNullOrWhiteSpace(resetUrl))
                {
                    await _passwordResetService.SendAsync(
                        challenge,
                        resetUrl,
                        cancellationToken
                    );
                    TempData["SuccessMessage"] =
                        "تمت إضافة الجهة وإرسال رابط تعيين كلمة المرور لمسؤولها.";
                    return RedirectToAction(nameof(Index));
                }
            }
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

            // Owner identity belongs to the creation workflow and is intentionally absent when editing.
            ModelState.Remove(nameof(TenantEditorViewModel.OwnerUsername));
            ModelState.Remove(nameof(TenantEditorViewModel.OwnerFullName));
            ModelState.Remove(nameof(TenantEditorViewModel.OwnerEmail));
            ModelState.Remove(nameof(TenantEditorViewModel.OwnerPhoneNumber));

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
        public IActionResult Reactivate(string id, TenantReactivationViewModel activation)
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = ModelState.Values
                    .SelectMany(value => value.Errors)
                    .Select(error => error.ErrorMessage)
                    .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
                    ?? "أكمل بيانات إعادة التنشيط.";
                return RedirectToAction(nameof(Index));
            }

            var result = _tenantManagementService.ReactivateTenant(
                id,
                activation.SubscriptionStatus,
                activation.AccessEndsAtUtc
            );
            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(
            string id,
            string deletionReason,
            bool permanentDeletionConfirmed
        )
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            var result = _tenantManagementService.DeleteTenant(
                id,
                deletionReason,
                permanentDeletionConfirmed
            );
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
