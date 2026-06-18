using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public static class DelegationScopeTypes
    {
        public const string Full = nameof(Full);
        public const string Custom = nameof(Custom);
    }

    public static class DelegationStatuses
    {
        public const string Scheduled = nameof(Scheduled);
        public const string Active = nameof(Active);
        public const string Expired = nameof(Expired);
        public const string Cancelled = nameof(Cancelled);
    }

    public class Delegation : ITenantScopedEntity
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;

        [Required(ErrorMessage = "رقم التفويض مطلوب.")]
        public string DelegationNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "اسم المستخدم المفوِّض مطلوب.")]
        public string DelegatorUsername { get; set; } = string.Empty;

        [Required(ErrorMessage = "اسم المستخدم المفوَّض إليه مطلوب.")]
        public string DelegateeUsername { get; set; } = string.Empty;

        [Required(ErrorMessage = "نوع نطاق التفويض مطلوب.")]
        public string ScopeType { get; set; } = DelegationScopeTypes.Custom;

        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public string TimeZoneId { get; set; } = string.Empty;
        public string Status { get; set; } = DelegationStatuses.Scheduled;
        public string Notes { get; set; } = string.Empty;
        public string CreatedByUsername { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string LastUpdatedByUsername { get; set; } = string.Empty;
        public DateTime? ActivatedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string CancelledByUsername { get; set; } = string.Empty;
        public string CancelReason { get; set; } = string.Empty;

        public ICollection<DelegationPermission> Permissions { get; set; } =
            new List<DelegationPermission>();
    }
}
