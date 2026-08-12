using System.Security.Claims;
using VehiclePermitSystemWeb.Models.ViewModels.Notifications;

namespace VehiclePermitSystemWeb.Services.Notifications
{
    public interface INotificationCenterService
    {
        NotificationCenterPageViewModel GetCenter(
            ClaimsPrincipal principal,
            string? category = null,
            string? state = null,
            int limit = 60
        );

        bool MarkRead(long id, string username);
        int MarkAllRead(string username);
        bool Dismiss(long id, string username);
        NotificationSettingsViewModel GetSettings();
        void UpdateSettings(NotificationSettingsViewModel model, string username);
    }
}
