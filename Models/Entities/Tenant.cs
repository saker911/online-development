using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public static class TenantDefaults
    {
        public const string DefaultTenantId = "default";
        public const string DefaultTenantName = "الجهة الافتراضية";
        public const string DefaultSubscriptionStatus = TenantSubscriptionStatuses.Active;
        public const string DefaultPlanName = "أساسية";
    }

    public static class TenantSubscriptionStatuses
    {
        public const string PendingPayment = "PendingPayment";
        public const string Trial = "Trial";
        public const string Active = "Active";
        public const string Suspended = "Suspended";
        public const string Expired = "Expired";

        public static readonly IReadOnlyList<string> All =
        [
            PendingPayment,
            Trial,
            Active,
            Suspended,
            Expired,
        ];

        public static string GetDisplayName(string? status)
        {
            return status switch
            {
                PendingPayment => "بانتظار الدفع",
                Trial => "تجريبي",
                Active => "نشط",
                Suspended => "موقوف",
                Expired => "منتهي",
                _ => "نشط",
            };
        }

        public static string Normalize(string? status)
        {
            var normalized = (status ?? string.Empty).Trim();
            return All.Contains(normalized, StringComparer.OrdinalIgnoreCase)
                ? All.First(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
                : Active;
        }
    }

    public interface ITenantScopedEntity
    {
        string TenantId { get; set; }
    }

    public class Tenant
    {
        [Required]
        [StringLength(64)]
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;

        [Required]
        [StringLength(256)]
        public string Name { get; set; } = TenantDefaults.DefaultTenantName;

        [StringLength(256)]
        public string Slug { get; set; } = TenantDefaults.DefaultTenantId;

        [StringLength(32)]
        public string OrganizationReference { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(32)]
        public string SubscriptionStatus { get; set; } =
            TenantDefaults.DefaultSubscriptionStatus;

        [Required]
        [StringLength(128)]
        public string PlanName { get; set; } = TenantDefaults.DefaultPlanName;

        [StringLength(64)]
        public string SignupPlanCode { get; set; } = string.Empty;
        public decimal? SignupPlanPrice { get; set; }
        public int? SignupPlanDurationMonths { get; set; }

        public DateTime? TrialEndsAtUtc { get; set; }
        public DateTime? SubscriptionEndsAtUtc { get; set; }
        public DateTime? SignupExpiresAtUtc { get; set; }
        public int? MaxUsers { get; set; }
        public int? MaxPermitsPerMonth { get; set; }
        public int? MaxVisitsPerMonth { get; set; }

        public bool PermitsServiceEnabled { get; set; } = true;
        public bool VisitsServiceEnabled { get; set; } = true;
        public bool SelfServiceEnabled { get; set; } = true;
        public bool QueueServiceEnabled { get; set; }
        public bool GateServiceEnabled { get; set; } = true;

        public bool NotificationCenterEnabled { get; set; } = true;
        public bool PermitNotificationsEnabled { get; set; } = true;
        public bool VisitNotificationsEnabled { get; set; } = true;
        public bool SecurityAlertsEnabled { get; set; } = true;
        public bool FailedOperationAlertsEnabled { get; set; } = true;
        public bool UnauthorizedMovementAlertsEnabled { get; set; } = true;
        public int NotificationRetentionDays { get; set; } = 90;
        public int EmailOutboxRetentionDays { get; set; } = 30;
        public int AuditLogRetentionDays { get; set; } = 365;
        public DateTime? LastRetentionRunAtUtc { get; set; }
    }

    public static class TenantServiceKeys
    {
        public const string Permits = "Permits";
        public const string Visits = "Visits";
        public const string SelfService = "SelfService";
        public const string Queue = "Queue";
        public const string Gate = "Gate";

        public static IReadOnlyList<string> All { get; } =
            [Permits, Visits, SelfService, Queue, Gate];

        public static string GetDisplayName(string? key) => key switch
        {
            Permits => "التصاريح",
            Visits => "الزيارات",
            SelfService => "الخدمة الذاتية",
            Queue => "تنظيم الطابور",
            Gate => "البوابات والشاشات",
            _ => "الخدمة المطلوبة",
        };

        public static string Normalize(string? key)
        {
            var value = (key ?? string.Empty).Trim();
            return All.FirstOrDefault(item =>
                    string.Equals(item, value, StringComparison.OrdinalIgnoreCase)
                )
                ?? string.Empty;
        }
    }
}
