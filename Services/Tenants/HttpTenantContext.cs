using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Security;

namespace VehiclePermitSystemWeb.Services.Tenants
{
    public sealed class HttpTenantContext : ITenantContext
    {
        public const string ResolvedTenantItemKey = "ResolvedTenantId";

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

                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    return tenantId.Trim();
                }

                var httpContext = _httpContextAccessor.HttpContext;
                tenantId = httpContext?.Items[ResolvedTenantItemKey]?.ToString();

                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    tenantId = httpContext?.Request?.HasFormContentType == true
                    ? httpContext.Request.Form["tenant"].FirstOrDefault()
                    : null;
                }

                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    tenantId = httpContext?.Request?.Query["tenant"].FirstOrDefault();
                }

                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    tenantId = httpContext?.Request?.Cookies["TenantId"];
                }

                return string.IsNullOrWhiteSpace(tenantId)
                    ? TenantDefaults.DefaultTenantId
                    : tenantId.Trim();
            }
        }
    }
}
