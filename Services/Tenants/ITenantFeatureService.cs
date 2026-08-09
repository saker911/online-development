using VehiclePermitSystemWeb.Models.Entities;

namespace VehiclePermitSystemWeb.Services.Tenants
{
    public interface ITenantFeatureService
    {
        bool IsEnabled(string featureKey);
        bool IsEnabled(string featureKey, string? tenantIdOrSlug);
        TenantServiceAvailability GetCurrent();
        TenantServiceAvailability GetForTenant(string? tenantIdOrSlug);
    }

    public sealed record TenantServiceAvailability(
        string TenantId,
        bool Permits,
        bool Visits,
        bool SelfService,
        bool Queue,
        bool Gate
    )
    {
        public bool IsEnabled(string featureKey) => TenantServiceKeys.Normalize(featureKey) switch
        {
            TenantServiceKeys.Permits => Permits,
            TenantServiceKeys.Visits => Visits,
            TenantServiceKeys.SelfService => SelfService,
            TenantServiceKeys.Queue => Queue,
            TenantServiceKeys.Gate => Gate,
            _ => false,
        };
    }
}
