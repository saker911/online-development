namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class ExternalUserLogin : ITenantScopedEntity
    {
        public long Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string Username { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string EmailAtLinkTime { get; set; } = string.Empty;
        public DateTime LinkedAtUtc { get; set; }
    }
}
