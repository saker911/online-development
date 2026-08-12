using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class WorkplaceSiteEntrance : ITenantScopedEntity
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public int WorkplaceSiteId { get; set; }

        [Required(ErrorMessage = "اسم المدخل مطلوب.")]
        [StringLength(128, ErrorMessage = "اسم المدخل يجب ألا يتجاوز 128 حرفًا.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(32, ErrorMessage = "رمز المدخل يجب ألا يتجاوز 32 حرفًا.")]
        public string Code { get; set; } = string.Empty;

        [StringLength(256, ErrorMessage = "وصف الموقع يجب ألا يتجاوز 256 حرفًا.")]
        public string LocationDescription { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public WorkplaceSite? WorkplaceSite { get; set; }
        public ICollection<DisplayDevice> DisplayDevices { get; set; } = new List<DisplayDevice>();
    }
}
