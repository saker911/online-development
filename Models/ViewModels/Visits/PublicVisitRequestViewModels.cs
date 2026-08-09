using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Visits
{
    public sealed class PublicVisitRequestViewModel
    {
        public string TenantSlug { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public string WelcomeMessage { get; set; } = string.Empty;
        public bool ShowNationalId { get; set; } = true;
        public bool RequireNationalId { get; set; }
        public bool ShowHostName { get; set; } = true;
        public bool RequireHostName { get; set; } = true;
        public bool ShowVisitLocation { get; set; } = true;
        public bool RequireVisitLocation { get; set; } = true;
        public bool ShowPurpose { get; set; } = true;
        public bool RequirePurpose { get; set; } = true;

        [Display(Name = "الاسم الكامل")]
        [Required(ErrorMessage = "يرجى إدخال الاسم الكامل.")]
        [StringLength(128, ErrorMessage = "الاسم يجب ألا يتجاوز 128 حرفًا.")]
        public string VisitorName { get; set; } = string.Empty;

        [Display(Name = "رقم الجوال")]
        [Required(ErrorMessage = "يرجى إدخال رقم الجوال.")]
        [RegularExpression(SaudiMobileNumberValidator.RegularExpressionPattern, ErrorMessage = SaudiMobileNumberValidator.ErrorMessage)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Display(Name = "البريد الإلكتروني")]
        [Required(ErrorMessage = "يرجى إدخال البريد الإلكتروني لاستلام نتيجة الطلب.")]
        [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صحيحة.")]
        [StringLength(256, ErrorMessage = "البريد الإلكتروني يجب ألا يتجاوز 256 حرفًا.")]
        public string VisitorEmail { get; set; } = string.Empty;

        [Display(Name = "رقم الهوية أو الإقامة")]
        [RegularExpression("^$|^[12][0-9]{9}$", ErrorMessage = "رقم الهوية أو الإقامة يجب أن يتكون من 10 أرقام ويبدأ بـ 1 أو 2.")]
        public string NationalId { get; set; } = string.Empty;

        [Display(Name = "موعد الزيارة")]
        [Required(ErrorMessage = "يرجى تحديد موعد الزيارة.")]
        public DateTime VisitDate { get; set; }

        [Display(Name = "الشخص المراد زيارته")]
        [StringLength(128, ErrorMessage = "اسم الشخص يجب ألا يتجاوز 128 حرفًا.")]
        public string VisitedPersonName { get; set; } = string.Empty;

        [Display(Name = "مكان الزيارة")]
        [StringLength(256, ErrorMessage = "مكان الزيارة يجب ألا يتجاوز 256 حرفًا.")]
        public string VisitLocation { get; set; } = string.Empty;

        [Display(Name = "سبب الزيارة")]
        [StringLength(256, ErrorMessage = "سبب الزيارة يجب ألا يتجاوز 256 حرفًا.")]
        public string Purpose { get; set; } = string.Empty;

        [Range(typeof(bool), "true", "true", ErrorMessage = "يلزم الموافقة على سياسة الخصوصية لإرسال الطلب.")]
        public bool AcceptPrivacy { get; set; }

        [StringLength(200)]
        public string Website { get; set; } = string.Empty;
    }

    public sealed class PublicVisitStatusViewModel
    {
        public string TenantSlug { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string VisitId { get; set; } = string.Empty;
        public string VisitorName { get; set; } = string.Empty;
        public string MaskedPhoneNumber { get; set; } = string.Empty;
        public string VisitLocation { get; set; } = string.Empty;
        public string VisitedPersonName { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public DateTime VisitDate { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsApproved => string.Equals(ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase);
        public bool IsRejected => string.Equals(ApprovalStatus, "Rejected", StringComparison.OrdinalIgnoreCase);
        public bool IsPending => !IsApproved && !IsRejected;
    }
}
