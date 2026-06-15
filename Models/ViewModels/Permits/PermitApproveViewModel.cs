using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Permits
{
    public class PermitApproveViewModel
    {
        [Required(ErrorMessage = "رقم التصريح مطلوب.")]
        public string PermitNumber { get; set; } = string.Empty;

        public string DriverName { get; set; } = string.Empty;

        public string PermitTypeDisplay { get; set; } = string.Empty;

        public string ReturnRequirementDisplay { get; set; } = string.Empty;
    }
}
