using System.ComponentModel.DataAnnotations;
using VehiclePermitSystemWeb.Models.Entities;

namespace VehiclePermitSystemWeb.Models.ViewModels.Tenants
{
    public sealed class TenantManagementViewModel
    {
        public List<TenantSummaryViewModel> Tenants { get; set; } = new();
        public TenantEditorViewModel Editor { get; set; } = new();
        public int TotalTenants => Tenants.Count;
        public int ActiveTenants => Tenants.Count(tenant => tenant.IsActive);
        public int BlockedTenants =>
            Tenants.Count(tenant => tenant.IsBlockedBySubscription || !tenant.IsActive);
        public int TotalUsers => Tenants.Sum(tenant => tenant.UserCount);
    }

    public sealed class TenantSummaryViewModel
    {
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string OrganizationReference { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public int UserCount { get; set; }
        public int PermitCount { get; set; }
        public int VisitCount { get; set; }
        public List<TenantUserSummaryViewModel> Users { get; set; } = new();
        public int ActiveUserCount => Users.Count(user => user.IsActive);
        public int InactiveUserCount => Users.Count(user => !user.IsActive);
        public string OrganizationName { get; set; } = string.Empty;
        public string SubscriptionStatus { get; set; } = TenantDefaults.DefaultSubscriptionStatus;
        public string SubscriptionStatusDisplayName =>
            TenantSubscriptionStatuses.GetDisplayName(SubscriptionStatus);
        public string PlanName { get; set; } = TenantDefaults.DefaultPlanName;
        public DateTime? TrialEndsAtUtc { get; set; }
        public DateTime? SubscriptionEndsAtUtc { get; set; }
        public int? MaxUsers { get; set; }
        public int? MaxPermitsPerMonth { get; set; }
        public int? MaxVisitsPerMonth { get; set; }
        public bool IsBlockedBySubscription { get; set; }
        public bool PermitsServiceEnabled { get; set; }
        public bool VisitsServiceEnabled { get; set; }
        public bool SelfServiceEnabled { get; set; }
        public bool QueueServiceEnabled { get; set; }
        public bool GateServiceEnabled { get; set; }
        public int EnabledServiceCount => new[]
        {
            PermitsServiceEnabled,
            VisitsServiceEnabled,
            SelfServiceEnabled,
            QueueServiceEnabled,
            GateServiceEnabled,
        }.Count(enabled => enabled);
    }

    public sealed class TenantUserSummaryViewModel
    {
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string RoleDisplayName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsTenantManager { get; set; }
    }

    public sealed class TenantEditorViewModel
    {
        [Display(Name = "معرف الجهة")]
        [StringLength(64, ErrorMessage = "معرف الجهة يجب ألا يتجاوز 64 حرفًا.")]
        [RegularExpression(
            "^[\\p{L}\\p{N}][\\p{L}\\p{N}-_]{1,63}$",
            ErrorMessage = "استخدم حروفًا أو أرقامًا أو شرطة فقط، ويجب أن يبدأ المعرف بحرف أو رقم."
        )]
        public string TenantId { get; set; } = string.Empty;

        [Required(ErrorMessage = "اسم الجهة مطلوب.")]
        [Display(Name = "اسم الجهة")]
        [StringLength(256, ErrorMessage = "اسم الجهة يجب ألا يتجاوز 256 حرفًا.")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "الرابط المختصر")]
        [StringLength(256, ErrorMessage = "الرابط المختصر يجب ألا يتجاوز 256 حرفًا.")]
        [RegularExpression(
            "^[\\p{L}\\p{N}][\\p{L}\\p{N}-_]{1,255}$",
            ErrorMessage = "استخدم حروفًا أو أرقامًا أو شرطة فقط، ويجب أن يبدأ الرابط بحرف أو رقم."
        )]
        public string Slug { get; set; } = string.Empty;

        [Display(Name = "رقم الجهة (اختياري)")]
        [StringLength(32, ErrorMessage = "رقم الجهة يجب ألا يتجاوز 32 رقمًا.")]
        [RegularExpression("^[0-9]*$", ErrorMessage = "رقم الجهة يقبل الأرقام فقط.")]
        public string OrganizationReference { get; set; } = string.Empty;

        [Display(Name = "اسم مستخدم مسؤول الجهة")]
        [RegularExpression(
            AccountUsernameValidator.RegularExpressionPattern,
            ErrorMessage = AccountUsernameValidator.ErrorMessage
        )]
        public string OwnerUsername { get; set; } = string.Empty;

        [Display(Name = "اسم مسؤول الجهة")]
        [StringLength(256)]
        public string OwnerFullName { get; set; } = string.Empty;

        [Display(Name = "البريد الإلكتروني لمسؤول الجهة")]
        [EmailAddress(ErrorMessage = "أدخل بريدًا إلكترونيًا صحيحًا.")]
        [StringLength(256)]
        public string OwnerEmail { get; set; } = string.Empty;

        [Display(Name = "جوال مسؤول الجهة")]
        [RegularExpression(
            SaudiMobileNumberValidator.RegularExpressionPattern,
            ErrorMessage = SaudiMobileNumberValidator.ErrorMessage
        )]
        public string OwnerPhoneNumber { get; set; } = string.Empty;

        [Display(Name = "اسم الإدارة الافتراضي")]
        [StringLength(256, ErrorMessage = "اسم الإدارة يجب ألا يتجاوز 256 حرفًا.")]
        public string DepartmentName { get; set; } = string.Empty;

        [Required(ErrorMessage = "حالة الاشتراك مطلوبة.")]
        [Display(Name = "حالة الاشتراك")]
        public string SubscriptionStatus { get; set; } = TenantDefaults.DefaultSubscriptionStatus;

        [Required(ErrorMessage = "اسم الباقة مطلوب.")]
        [Display(Name = "الباقة")]
        [StringLength(128, ErrorMessage = "اسم الباقة يجب ألا يتجاوز 128 حرفًا.")]
        public string PlanName { get; set; } = TenantDefaults.DefaultPlanName;

        [Display(Name = "نهاية التجربة")]
        public DateTime? TrialEndsAtUtc { get; set; }

        [Display(Name = "نهاية الاشتراك")]
        public DateTime? SubscriptionEndsAtUtc { get; set; }

        [Display(Name = "حد المستخدمين")]
        [Range(1, int.MaxValue, ErrorMessage = "حد المستخدمين يجب أن يكون رقمًا موجبًا.")]
        public int? MaxUsers { get; set; }

        [Display(Name = "حد التصاريح شهريًا")]
        [Range(1, int.MaxValue, ErrorMessage = "حد التصاريح يجب أن يكون رقمًا موجبًا.")]
        public int? MaxPermitsPerMonth { get; set; }

        [Display(Name = "حد الزيارات شهريًا")]
        [Range(1, int.MaxValue, ErrorMessage = "حد الزيارات يجب أن يكون رقمًا موجبًا.")]
        public int? MaxVisitsPerMonth { get; set; }

        [Display(Name = "خدمة التصاريح")]
        public bool PermitsServiceEnabled { get; set; } = true;

        [Display(Name = "خدمة الزيارات")]
        public bool VisitsServiceEnabled { get; set; } = true;

        [Display(Name = "الخدمة الذاتية")]
        public bool SelfServiceEnabled { get; set; } = true;

        [Display(Name = "تنظيم الطابور")]
        public bool QueueServiceEnabled { get; set; }

        [Display(Name = "البوابات والشاشات")]
        public bool GateServiceEnabled { get; set; } = true;

        [Display(Name = "مركز الإشعارات")]
        public bool NotificationCenterEnabled { get; set; } = true;

        [Display(Name = "تنبيهات التصاريح")]
        public bool PermitNotificationsEnabled { get; set; } = true;

        [Display(Name = "تنبيهات الزيارات")]
        public bool VisitNotificationsEnabled { get; set; } = true;

        [Display(Name = "التنبيهات الأمنية")]
        public bool SecurityAlertsEnabled { get; set; } = true;

        [Display(Name = "العمليات الفاشلة")]
        public bool FailedOperationAlertsEnabled { get; set; } = true;

        [Display(Name = "حركات الدخول والخروج غير المصرح بها")]
        public bool UnauthorizedMovementAlertsEnabled { get; set; } = true;

        [Display(Name = "الاحتفاظ بالإشعارات (يوم)")]
        [Range(7, 730, ErrorMessage = "مدة الاحتفاظ بالإشعارات من 7 إلى 730 يومًا.")]
        public int NotificationRetentionDays { get; set; } = 90;

        [Display(Name = "الاحتفاظ بسجل البريد (يوم)")]
        [Range(7, 365, ErrorMessage = "مدة الاحتفاظ بسجل البريد من 7 إلى 365 يومًا.")]
        public int EmailOutboxRetentionDays { get; set; } = 30;

        [Display(Name = "الاحتفاظ بسجل التدقيق (يوم)")]
        [Range(90, 2555, ErrorMessage = "مدة الاحتفاظ بسجل التدقيق من 90 يومًا إلى 7 سنوات.")]
        public int AuditLogRetentionDays { get; set; } = 365;

        public IReadOnlyList<string> SubscriptionStatusOptions =>
            TenantSubscriptionStatuses.All;
    }
}
