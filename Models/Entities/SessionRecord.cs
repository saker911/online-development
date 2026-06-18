namespace VehiclePermitSystemWeb.Models.Entities
{
    public class SessionRecord : ITenantScopedEntity
    {
        public string SessionId { get; set; } = string.Empty;
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string Username { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime LastActivityUtc { get; set; }
    }
}
