using System.ComponentModel.DataAnnotations;
using VehiclePermitSystemWeb.Models.ViewModels.Workplace;

namespace VehiclePermitSystemWeb.Models.ViewModels.Display
{
    public class DisplayDeviceRegistrationViewModel
    {
        [Required(ErrorMessage = "اسم الشاشة مطلوب.")]
        public string ScreenName { get; set; } = string.Empty;

        public string ScreenLocation { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int? WorkplaceSiteId { get; set; }

        public int? WorkplaceSiteEntranceId { get; set; }
        public IReadOnlyList<WorkplaceLocationOptionViewModel> Locations { get; set; } =
            Array.Empty<WorkplaceLocationOptionViewModel>();

        [Required(ErrorMessage = "مفتاح التهيئة مطلوب.")]
        public string SetupKey { get; set; } = string.Empty;

        public bool Submitted { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
