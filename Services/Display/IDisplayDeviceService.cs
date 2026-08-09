using Microsoft.AspNetCore.Http;

namespace VehiclePermitSystemWeb.Services.Display
{
    public interface IDisplayDeviceService
    {
        string DeviceCookieName { get; }
        string RequestCookieName { get; }
        DisplaySecuritySettings GetSecuritySettings();
        void UpdateSetupKey(string setupKey);
        void InvalidateDeviceTrust(string actor);
        DisplayDevice? GetApprovedDevice(HttpContext context);
        DisplayDevice? GetDeviceFromCookie(HttpContext context);
        DisplayDevice? GetDeviceFromRequestCookie(HttpContext context);
        void SetRequestCookie(HttpContext context, string requestCode);
        DisplayDevice? GetDeviceById(int id);
        DisplayDevice? RegisterRequest(
            DisplayDeviceRegistrationViewModel model,
            HttpContext context
        );
        (bool Success, string? DeviceToken) ApproveDevice(
            int id,
            string approvedBy,
            HttpContext? context = null
        );
        bool RejectDevice(int id, string actor);
        bool DisableDevice(int id, string actor);
        bool ActivateDevice(int id, string actor);
        bool UpdateDeviceMode(int id, string mode, string actor);
        bool DeleteDevice(int id, string actor);
        (bool Success, string? DeviceToken) RotateDeviceToken(
            int id,
            string actor,
            HttpContext? context = null
        );
        bool TryActivateApprovedRequest(HttpContext context);
        bool RecordHeartbeat(HttpContext context);
        DisplayDeviceManagementViewModel BuildManagementViewModel();
        int GetPendingCount();
        void RecordRejectedAccess(HttpContext context, string reason);
    }
}
