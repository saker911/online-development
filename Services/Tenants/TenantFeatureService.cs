using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;

namespace VehiclePermitSystemWeb.Services.Tenants
{
    public sealed class TenantFeatureService : ITenantFeatureService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ITenantContext _tenantContext;
        private readonly Dictionary<string, TenantServiceAvailability> _requestCache =
            new(StringComparer.OrdinalIgnoreCase);

        public TenantFeatureService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ITenantContext tenantContext
        )
        {
            _dbContextFactory = dbContextFactory;
            _tenantContext = tenantContext;
        }

        public bool IsEnabled(string featureKey) => GetCurrent().IsEnabled(featureKey);

        public bool IsEnabled(string featureKey, string? tenantIdOrSlug) =>
            GetForTenant(tenantIdOrSlug).IsEnabled(featureKey);

        public TenantServiceAvailability GetCurrent() => GetForTenant(_tenantContext.TenantId);

        public TenantServiceAvailability GetForTenant(string? tenantIdOrSlug)
        {
            var tenantKey = (tenantIdOrSlug ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(tenantKey))
            {
                tenantKey = TenantDefaults.DefaultTenantId;
            }

            if (_requestCache.TryGetValue(tenantKey, out var cachedAvailability))
            {
                return cachedAvailability;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var tenant = db
                .Tenants.AsNoTracking()
                .FirstOrDefault(item =>
                    item.TenantId == tenantKey || item.Slug == tenantKey
                );
            if (tenant == null)
            {
                var unavailable = new TenantServiceAvailability(
                    tenantKey,
                    false,
                    false,
                    false,
                    false,
                    false
                );
                _requestCache[tenantKey] = unavailable;
                return unavailable;
            }

            var visitsEnabled = tenant.VisitsServiceEnabled;
            var selfServiceEnabled = visitsEnabled && tenant.SelfServiceEnabled;
            var availability = new TenantServiceAvailability(
                tenant.TenantId,
                tenant.PermitsServiceEnabled,
                visitsEnabled,
                selfServiceEnabled,
                selfServiceEnabled && tenant.QueueServiceEnabled,
                tenant.GateServiceEnabled
            );
            _requestCache[tenantKey] = availability;
            _requestCache[tenant.TenantId] = availability;
            _requestCache[tenant.Slug] = availability;
            return availability;
        }
    }
}
