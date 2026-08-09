using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Display
{
    public class DisplaySettingsViewModel
    {
        public int Id { get; set; }

        [Display(Name = "عنوان الوصول الخارجي")]
        public string DisplayBaseUrl { get; set; } = string.Empty;

        [Display(Name = "مفتاح تشغيل شاشات العرض")]
        public string DisplayAccessKey { get; set; } = string.Empty;

        [Display(Name = "عناوين IP المسموح بها")]
        public string AllowedClientIpRanges { get; set; } = string.Empty;

        public string GateDisplayUrl { get; set; } = string.Empty;

        public string WaitingBoardUrl { get; set; } = string.Empty;

        public string VisitsDisplayUrl { get; set; } = string.Empty;

        public string RegistrationUrl { get; set; } = string.Empty;

        public string MaskedDisplayAccessKey { get; set; } = string.Empty;

        public DisplayDeviceManagementViewModel DeviceManagement { get; set; } = new();
    }
}
