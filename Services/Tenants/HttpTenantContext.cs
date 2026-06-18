using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Security;

namespace VehiclePermitSystemWeb.Services.Tenants
{
    public sealed class HttpTenantContext : ITenantContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpTenantContext(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string TenantId
        {
            get
            {
                var tenantId = _httpContextAccessor
                    .HttpContext
                    ?.User
                    ?.FindFirst(AppClaimTypes.TenantId)
                    ?.Value;

                return string.IsNullOrWhiteSpace(tenantId)
                    ? TenantDefaults.DefaultTenantId
                    : tenantId.Trim();
            }
        }
    }
}
