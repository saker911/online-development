using System.ComponentModel.DataAnnotations;
using VehiclePermitSystemWeb.Models.Entities;

namespace VehiclePermitSystemWeb.Models.ViewModels.Users
{
    public class UserEditViewModel : IValidatableObject
    {
        [Display(Name = "معرف الحساب")]
        [Required(ErrorMessage = "معرف الحساب مطلوب.")]
        public string Username { get; set; } = string.Empty;

        public string? OriginalUsername { get; set; }

        [Display(Name = "كلمة المرور")]
        public string? NewPassword { get; set; }

        [Display(Name = "رمز مشغل البوابة")]
        [RegularExpression(
            @"^$|^[A-Z0-9\-]{6,64}$",
            ErrorMessage = "رمز مشغل البوابة يجب أن يحتوي على أحرف إنجليزية كبيرة وأرقام وعلامة واصلة (-) فقط."
        )]
        public string? OperatorBadgeCode { get; set; }

        [Display(Name = "الرمز السري المؤقت للبوابة")]
        [RegularExpression(
            @"^$|^\d{6}$",
            ErrorMessage = "الرمز السري المؤقت يجب أن يتكون من 6 أرقام."
        )]
        public string? TemporaryOperatorPin { get; set; }

        [Display(Name = "إجبار تغيير رمز البوابة عند أول دخول")]
        public bool MustChangeOperatorPin { get; set; } = true;

        [Display(Name = "الاسم")]
        [Required(ErrorMessage = "اسم المستخدم مطلوب.")]
        public string FullName { get; set; } = string.Empty;

        [Display(Name = "البريد الإلكتروني")]
        [RegularExpression(
            @"^$|^[^\s@]+@[^\s@]+\.[^\s@]+$",
            ErrorMessage = "البريد الإلكتروني غير صحيح."
        )]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "رقم الموظف")]
        public string EmployeeNumber { get; set; } = string.Empty;

        [Display(Name = "نوع المستخدم")]
        public string UserType { get; set; } = string.Empty;

        public int WizardStep { get; set; } = 1;

        public string LookupStatus { get; set; } = string.Empty;

        public string LookupMessage { get; set; } = string.Empty;

        public string LookupRedirectUrl { get; set; } = string.Empty;

        public string LookupReactivateUrl { get; set; } = string.Empty;

        public int PendingPermitsCount { get; set; }

        [Display(Name = "القسم")]
        public string? Department { get; set; }

        public List<string> DepartmentOptions { get; set; } = new List<string>();

        [Display(Name = "الموقع / الفرع")]
        public int? WorkplaceSiteId { get; set; }

        public List<UserWorkplaceSiteOptionViewModel> WorkplaceSiteOptions { get; set; } = new();

        [Display(Name = "الجهة")]
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;

        public bool CanChooseTenant { get; set; }

        public List<UserTenantOptionViewModel> TenantOptions { get; set; } = new();

        public Dictionary<string, string> DepartmentManagerSummaryByName { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [Display(Name = "المسمى الوظيفي")]
        public string? JobTitle { get; set; }

        [Display(Name = "الجوال")]
        [Required(ErrorMessage = "رقم الجوال مطلوب.")]
        [RegularExpression(
            SaudiMobileNumberValidator.RegularExpressionPattern,
            ErrorMessage = SaudiMobileNumberValidator.ErrorMessage
        )]
        public string PhoneNumber { get; set; } = string.Empty;

        [Display(Name = "الحالة")]
        public bool IsActive { get; set; } = true;

        public bool IsSuperAdmin { get; set; }

        [Display(Name = "الدور")]
        [Required(ErrorMessage = "يرجى اختيار الدور.")]
        public string Role { get; set; } = string.Empty;

        public bool ApplyRoleDefaults { get; set; } = true;
        public bool CanViewDashboard { get; set; }
        public bool CanViewPermits { get; set; }
        public bool CanViewVisitorPermits { get; set; }
        public bool CanCreatePermit { get; set; }
        public bool CanCreateVisitorPermit { get; set; }
        public bool CanEditPermit { get; set; }
        public bool CanEditVisitorPermit { get; set; }
        public bool CanApprovePermit { get; set; }
        public bool CanApproveLeaveRequest { get; set; }
        public bool CanStopPermit { get; set; }
        public bool CanReviewUnauthorizedExit { get; set; }
        public bool CanViewVisits { get; set; }
        public bool CanCreateVisit { get; set; }
        public bool CanEditVisit { get; set; }
        public bool CanApproveDetainedVisit { get; set; }
        public bool CanApproveVisits { get; set; }
        public bool CanScanOperations { get; set; }
        public bool CanViewDisplays { get; set; }
        public bool CanManageUsers { get; set; }
        public bool CanManageDepartments { get; set; }
        public bool CanManageAdministration { get; set; }
        public bool CanManageDelegations { get; set; }

        [Display(Name = "اسم مستخدم المدير المباشر")]
        public string? ManagerUsername { get; set; } = string.Empty;

        [Display(Name = "ربط تلقائيًا بمدير القسم")]
        public bool AutoBindManager { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var normalizedUsername = (Username ?? string.Empty).Trim();
            var normalizedOriginalUsername = (OriginalUsername ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(normalizedOriginalUsername))
            {
                if (!AccountUsernameValidator.IsValid(normalizedUsername))
                {
                    yield return new ValidationResult(
                        AccountUsernameValidator.ErrorMessage,
                        new[] { nameof(Username) }
                    );
                }

                yield break;
            }

            if (IsSuperAdmin)
            {
                if (string.IsNullOrWhiteSpace(normalizedUsername))
                {
                    yield return new ValidationResult(
                        "اسم مستخدم مالك النظام مطلوب.",
                        new[] { nameof(Username) }
                    );
                }
                else if (
                    normalizedUsername.Length > 64
                    || normalizedUsername.Any(character => char.IsWhiteSpace(character))
                )
                {
                    yield return new ValidationResult(
                        "اسم مستخدم مالك النظام يجب أن يكون بدون مسافات وبحد أقصى 64 حرفًا.",
                        new[] { nameof(Username) }
                    );
                }

                yield break;
            }

            if (
                !string.Equals(
                    normalizedUsername,
                    normalizedOriginalUsername,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                yield return new ValidationResult(
                    "لا يمكن تعديل اسم المستخدم بعد إنشاء الحساب.",
                    new[] { nameof(Username) }
                );
            }
        }
    }

    public sealed class UserTenantOptionViewModel
    {
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public sealed class UserWorkplaceSiteOptionViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }
}
