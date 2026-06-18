using VehiclePermitSystemWeb.Models.Entities;

namespace VehiclePermitSystemWeb.Services.Tenants
{
    public interface ITenantContext
    {
        string TenantId { get; }
    }

    public sealed class DefaultTenantContext : ITenantContext
    {
        public string TenantId => TenantDefaults.DefaultTenantId;
    }
}
