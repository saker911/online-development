using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public class UserAccount : ITenantScopedEntity
    {
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int? WorkplaceSiteId { get; set; }
        public WorkplaceSite? WorkplaceSite { get; set; }
        public string Department { get; set; } = string.Empty;
        public string EmployeeNumber { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string OperatorBadgeCode { get; set; } = string.Empty;
        public string OperatorPinHash { get; set; } = string.Empty;
        public string OperatorPinSalt { get; set; } = string.Empty;
        public bool MustChangeOperatorPin { get; set; }

        [Required(ErrorMessage = "رقم الجوال مطلوب.")]
        [RegularExpression(
            SaudiMobileNumberValidator.RegularExpressionPattern,
            ErrorMessage = SaudiMobileNumberValidator.ErrorMessage
        )]
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsEmailConfirmed { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public bool IsSuperAdmin { get; set; }
        public bool MustChangePassword { get; set; }
        public bool MfaEnabled { get; set; }
        public string MfaSecretProtected { get; set; } = string.Empty;
        public string MfaRecoveryCodeHashesJson { get; set; } = string.Empty;
        public DateTime? MfaEnrolledAtUtc { get; set; }
        public long? MfaLastVerifiedStep { get; set; }
        public string Role { get; set; } = string.Empty;
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
        public string ManagerUsername { get; set; } = string.Empty;
    }
}
