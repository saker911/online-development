using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace VehiclePermitSystemWeb.Services.Notifications
{
    public interface IToastNotificationService
    {
        void Success(string? message);
        void Error(string? message);
        void Warning(string? message);
        void Info(string? message);
        void Import(string? message, string type = "info");
        void Import(IEnumerable<ToastNotificationItem> notifications);
        void ImportModelState(ModelStateDictionary modelState);
        IReadOnlyList<ToastNotificationItem> ConsumeNotifications();
    }
}
