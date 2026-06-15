using System.Collections.Generic;

namespace VehiclePermitSystemWeb.Models.ViewModels.Permits
{
    public class PermitConflictViewModel
    {
        public string PermitNumber { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string NationalId { get; set; } = string.Empty;
        public string PlateNumber { get; set; } = string.Empty;
        public string PermitTypeDisplay { get; set; } = string.Empty;
        public string ApprovalStatusDisplay { get; set; } = string.Empty;
        public List<string> Reasons { get; set; } = new();

        public string ReasonsDisplay => string.Join("، ", Reasons);
    }
}
