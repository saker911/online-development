using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public static class TenantDefaults
    {
        public const string DefaultTenantId = "default";
        public const string DefaultTenantName = "الجهة الافتراضية";
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

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
