using System.Text.Json;
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

        [HttpGet]
        public IActionResult Snapshot()
        {
            var center = _notificationCenterService.GetCenter(User, state: "unread", limit: 8);
            return Json(new
            {
                enabled = center.IsEnabled,
                unreadCount = center.UnreadCount,
                items = center.Items.Select(item => new
                {
                    item.Id,
                    item.Category,
                    item.Severity,
                    item.Title,
                    item.Message,
                    item.ActionUrl,
                }),
            });
        }

        [HttpGet]
        public async Task Stream(CancellationToken cancellationToken)
        {
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache, no-store";
            Response.Headers.Append("X-Accel-Buffering", "no");

            var lastSignature = string.Empty;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var center = _notificationCenterService.GetCenter(
                        User,
                        state: "unread",
                        limit: 8
                    );
                    var signature = $"{center.IsEnabled}:{center.UnreadCount}:{string.Join(',', center.Items.Select(item => item.Id))}";
                    if (!string.Equals(signature, lastSignature, StringComparison.Ordinal))
                    {
                        var payload = JsonSerializer.Serialize(new
                        {
                            enabled = center.IsEnabled,
                            unreadCount = center.UnreadCount,
                            latest = center.Items.FirstOrDefault() is { } latest
                                ? new
                                {
                                    latest.Id,
                                    latest.Title,
                                    latest.Message,
                                    latest.ActionUrl,
                                    latest.Severity,
                                }
                                : null,
                        });
                        await Response.WriteAsync($"event: notifications\ndata: {payload}\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                        lastSignature = signature;
                    }
                    else
                    {
                        await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The browser closed or navigated away; EventSource reconnects automatically.
            }
        }

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
