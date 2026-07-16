using System.ComponentModel.DataAnnotations;
using VehiclePermitSystemWeb.Models.Entities;

namespace VehiclePermitSystemWeb.Models.ViewModels.Tenants
{
    public sealed class TenantPlanViewModel
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public int DurationMonths { get; set; }
        public decimal TotalPrice { get; set; }
        public bool IsFeatured { get; set; }
        public IReadOnlyList<string> Features { get; set; } = [];
        public string DurationText => DurationMonths switch
        {
            1 => "شهر واحد",
            2 => "شهران",
            >= 3 and <= 10 => $"{DurationMonths} أشهر",
            _ => $"{DurationMonths} شهرًا",
        };
        public string PriceText => TotalPrice <= 0 ? "حسب الاتفاق" : $"{TotalPrice:0} ر.س";
    }

    public sealed class TenantSignupLandingViewModel
    {
        public IReadOnlyList<TenantPlanViewModel> Plans { get; set; } = [];
        public string SelectedPlanCode { get; set; } = string.Empty;
    }

    public sealed class ExternalAuthActionsViewModel
    {
        public string Mode { get; set; } = "Register";
        public string Tenant { get; set; } = string.Empty;
        public string PlanCode { get; set; } = string.Empty;
    }

    public sealed class TenantSignupViewModel
    {
        [Required(ErrorMessage = "اختر الباقة المناسبة.")]
        [Display(Name = "الباقة")]
        public string PlanCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "اسم الجهة أو الموقع مطلوب.")]
        [Display(Name = "اسم الجهة أو الموقع")]
        [StringLength(256, ErrorMessage = "اسم الجهة أو الموقع يجب ألا يتجاوز 256 حرفًا.")]
        public string CompanyName { get; set; } = string.Empty;

        [Display(Name = "الرابط المختصر")]
        [StringLength(64, ErrorMessage = "الرابط المختصر يجب ألا يتجاوز 64 حرفًا.")]
        [RegularExpression(
            "^[a-zA-Z0-9][a-zA-Z0-9-_]{1,63}$",
            ErrorMessage = "استخدم حروفًا إنجليزية أو أرقامًا أو شرطة فقط، ويجب أن يبدأ بحرف أو رقم."
        )]
        public string TenantId { get; set; } = string.Empty;

        [Required(ErrorMessage = "اسم المسؤول مطلوب.")]
        [Display(Name = "اسم مسؤول الحساب")]
        [StringLength(256, ErrorMessage = "اسم المسؤول يجب ألا يتجاوز 256 حرفًا.")]
        public string OwnerFullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "اسم المستخدم مطلوب.")]
        [Display(Name = "اسم المستخدم")]
        [RegularExpression(
            SaudiNationalIdOrIqamaValidator.RegularExpressionPattern,
            ErrorMessage = SaudiNationalIdOrIqamaValidator.ErrorMessage
        )]
        public string OwnerUsername { get; set; } = string.Empty;

        [Required(ErrorMessage = "رقم الجوال مطلوب.")]
        [Display(Name = "رقم الجوال")]
        [RegularExpression(
            SaudiMobileNumberValidator.RegularExpressionPattern,
            ErrorMessage = SaudiMobileNumberValidator.ErrorMessage
        )]
        public string OwnerPhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "البريد الإلكتروني مطلوب.")]
        [Display(Name = "البريد الإلكتروني")]
        [EmailAddress(ErrorMessage = "أدخل بريدًا إلكترونيًا صحيحًا.")]
        [StringLength(256, ErrorMessage = "البريد الإلكتروني يجب ألا يتجاوز 256 حرفًا.")]
        public string OwnerEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "كلمة المرور مطلوبة.")]
        [Display(Name = "كلمة المرور")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "تأكيد كلمة المرور مطلوب.")]
        [Display(Name = "تأكيد كلمة المرور")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "كلمة المرور وتأكيدها غير متطابقين.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Range(typeof(bool), "true", "true", ErrorMessage = "يجب الموافقة على سياسة الاشتراك والدفع.")]
        public bool AcceptPolicy { get; set; }

        // Bot trap rendered outside the visible form flow.
        public string Website { get; set; } = string.Empty;

        public IReadOnlyList<TenantPlanViewModel> Plans { get; set; } = [];
    }

    public sealed class TenantCheckoutViewModel
    {
        public string TenantId { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string PlanName { get; set; } = TenantDefaults.DefaultPlanName;
        public string SubscriptionStatus { get; set; } = TenantSubscriptionStatuses.PendingPayment;
        public string SubscriptionStatusDisplayName =>
            TenantSubscriptionStatuses.GetDisplayName(SubscriptionStatus);
        public int DurationMonths { get; set; }
        public decimal TotalPrice { get; set; }
        public string PaymentReference { get; set; } = string.Empty;
        public string LoginUrl { get; set; } = string.Empty;
    }
}
