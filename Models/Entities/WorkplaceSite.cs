using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class WorkplaceSite : ITenantScopedEntity
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;

        [Required(ErrorMessage = "اسم الموقع مطلوب.")]
        [StringLength(128, ErrorMessage = "اسم الموقع يجب ألا يتجاوز 128 حرفًا.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(32, ErrorMessage = "رمز الموقع يجب ألا يتجاوز 32 حرفًا.")]
        public string Code { get; set; } = string.Empty;

        [StringLength(256, ErrorMessage = "العنوان يجب ألا يتجاوز 256 حرفًا.")]
        public string Address { get; set; } = string.Empty;

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public int GeofenceRadiusMeters { get; set; } = 150;
        public bool PermitsEnabled { get; set; } = true;
        public bool VisitsEnabled { get; set; } = true;
        public bool SelfServiceEnabled { get; set; } = true;
        public bool QueueEnabled { get; set; }
        public bool GateEnabled { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<WorkplaceSiteEntrance> Entrances { get; set; } =
            new List<WorkplaceSiteEntrance>();
        public ICollection<DisplayDevice> DisplayDevices { get; set; } = new List<DisplayDevice>();
        public ICollection<Permit> Permits { get; set; } = new List<Permit>();
        public ICollection<Visit> Visits { get; set; } = new List<Visit>();
        public ICollection<UserAccount> UserAccounts { get; set; } = new List<UserAccount>();
    }
}
