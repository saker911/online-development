namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class TrustedLoginDevice : ITenantScopedEntity
    {
        public long Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string Username { get; set; } = string.Empty;
        public string TokenHash { get; set; } = string.Empty;
        public string DeviceDescription { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime LastUsedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
    }
}
