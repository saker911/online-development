namespace VehiclePermitSystemWeb.Models.Entities
{
    public class DelegationPermission
    {
        public int Id { get; set; }
        public int DelegationId { get; set; }
        public string PermissionKey { get; set; } = string.Empty;

        public Delegation? Delegation { get; set; }
    }
}
