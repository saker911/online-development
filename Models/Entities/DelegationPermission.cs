namespace VehiclePermitSystemWeb.Models.Entities
{
    public class DelegationPermission : ITenantScopedEntity
    {
        public int Id { get; set; }
        public int DelegationId { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string PermissionKey { get; set; } = string.Empty;

        public Delegation? Delegation { get; set; }
    }
}
