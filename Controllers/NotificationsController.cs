using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.ViewModels.Notifications;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Notifications;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize]
    public sealed class NotificationsController : Controller
    {
        private readonly INotificationCenterService _notificationCenterService;

        public NotificationsController(INotificationCenterService notificationCenterService)
        {
            _notificationCenterService = notificationCenterService;
        }

        [HttpGet]
        public IActionResult Index(string? category = null, string? state = null) =>
            View(_notificationCenterService.GetCenter(User, category, state));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarkRead(long id, string? returnUrl = null)
        {
            _notificationCenterService.MarkRead(id, User.Identity?.Name ?? string.Empty);
            return RedirectSafely(returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarkAllRead(string? returnUrl = null)
        {
            _notificationCenterService.MarkAllRead(User.Identity?.Name ?? string.Empty);
            return RedirectSafely(returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Dismiss(long id, string? returnUrl = null)
        {
            _notificationCenterService.Dismiss(id, User.Identity?.Name ?? string.Empty);
            return RedirectSafely(returnUrl);
        }

        [HttpGet]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult Settings() => View(_notificationCenterService.GetSettings());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult Settings(NotificationSettingsViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            _notificationCenterService.UpdateSettings(
                model,
                User.Identity?.Name ?? string.Empty
            );
            TempData["SuccessMessage"] = "تم حفظ إعدادات الإشعارات والاحتفاظ بالبيانات.";
            return RedirectToAction(nameof(Settings));
        }

        private IActionResult RedirectSafely(string? returnUrl) =>
            !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction(nameof(Index));
    }
}
