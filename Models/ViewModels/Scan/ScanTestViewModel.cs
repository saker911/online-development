using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Scan
{
    public class ScanTestViewModel
    {
        public const string ModePermit = "permit";
        public const string ModeVisit = "visit";

        [Display(Name = "نوع المسح")]
        public string Mode { get; set; } = ModePermit;

        [Display(Name = "القيمة المقروءة")]
        [Required(ErrorMessage = "أدخل القيمة المقروءة من جهاز الباركود.")]
        public string Identifier { get; set; } = string.Empty;

        public string ResultTitle { get; set; } = string.Empty;
        public string ResultMessage { get; set; } = string.Empty;
        public string ResultClass { get; set; } = string.Empty;
        public string LastIdentifier { get; set; } = string.Empty;
        public string ScannerUserName { get; set; } = string.Empty;
        public string SamplePermitNumber { get; set; } = string.Empty;
        public string SamplePermitPublicCode { get; set; } = string.Empty;
        public string SamplePermitDisplayName { get; set; } = string.Empty;
        public bool IsSpecificPermitBarcode { get; set; }
        public string SamplePermitNotice { get; set; } = string.Empty;
        public Permit? ScannedPermit { get; set; }
        public List<PermitActivity> RecentActivities { get; set; } = new();
    }
}
