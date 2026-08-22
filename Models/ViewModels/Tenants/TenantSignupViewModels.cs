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
        public decimal? OriginalPrice { get; set; }
        public string OfferLabel { get; set; } = string.Empty;
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
        public string OriginalPriceText =>
            OriginalPrice.HasValue && OriginalPrice.Value > TotalPrice
                ? $"{OriginalPrice.Value:0} ر.س"
                : string.Empty;
        public string BadgeText => !string.IsNullOrWhiteSpace(OfferLabel)
            ? OfferLabel
            : IsFeatured ? "الأكثر استخدامًا" : string.Empty;
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

        // Kept for backwards-compatible form binding. Public links are generated server-side.
        public string TenantId { get; set; } = string.Empty;

        [Display(Name = "رقم الجهة (اختياري)")]
        [StringLength(32, ErrorMessage = "رقم الجهة يجب ألا يتجاوز 32 رقمًا.")]
        [RegularExpression("^[0-9]*$", ErrorMessage = "رقم الجهة يقبل الأرقام فقط.")]
        public string OrganizationReference { get; set; } = string.Empty;

        [Required(ErrorMessage = "اسم المسؤول مطلوب.")]
        [Display(Name = "اسم مسؤول الحساب")]
        [StringLength(256, ErrorMessage = "اسم المسؤول يجب ألا يتجاوز 256 حرفًا.")]
        public string OwnerFullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "اسم المستخدم مطلوب.")]
        [Display(Name = "معرف الحساب")]
        [RegularExpression(
            AccountUsernameValidator.RegularExpressionPattern,
            ErrorMessage = AccountUsernameValidator.ErrorMessage
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
        public string ExternalProvider { get; set; } = string.Empty;
        public string CheckoutToken { get; set; } = string.Empty;
        public string OwnerUsername { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public bool IsEmailConfirmed { get; set; }
        public string MaskedOwnerEmail
        {
            get
            {
                var parts = OwnerEmail.Split('@', 2);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                {
                    return OwnerEmail;
                }

                var visible = parts[0].Length <= 2 ? parts[0][..1] : parts[0][..2];
                return $"{visible}***@{parts[1]}";
            }
        }
    }
}
