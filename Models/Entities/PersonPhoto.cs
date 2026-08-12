namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class PersonPhoto : ITenantScopedEntity
    {
        public long PersonProfileId { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "image/jpeg";
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
