namespace VehiclePermitSystemWeb.Models.Entities
{
    public class DisplaySecuritySettings : ITenantScopedEntity
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string SetupKeyHash { get; set; } = string.Empty;
        public bool RequireAdminApproval { get; set; } = true;
        public int HeartbeatSeconds { get; set; } = 30;
        public int DeviceCookieDays { get; set; } = 365;
    }
}
