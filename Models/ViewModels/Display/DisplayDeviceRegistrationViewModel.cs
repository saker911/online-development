using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Display
{
    public class DisplayDeviceRegistrationViewModel
    {
        [Required(ErrorMessage = "اسم الشاشة مطلوب.")]
        public string ScreenName { get; set; } = string.Empty;

        [Required(ErrorMessage = "موقع الشاشة مطلوب.")]
        public string ScreenLocation { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "مفتاح التهيئة مطلوب.")]
        public string SetupKey { get; set; } = string.Empty;

        public bool Submitted { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
