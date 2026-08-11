using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Tenants;

namespace VehiclePermitSystemWeb.Infrastructure
{
    public sealed class TenantFeatureAccessMiddleware
    {
        private readonly RequestDelegate _next;

        public TenantFeatureAccessMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantFeatureService tenantFeatureService
        )
        {
            var requirements = ResolveRequirements(context.Request.Path);
            if (requirements.Count == 0)
            {
                await _next(context);
                return;
            }

            var tenantHint = context.Request.RouteValues["tenant"]?.ToString();
            var availability = string.IsNullOrWhiteSpace(tenantHint)
                ? tenantFeatureService.GetCurrent()
                : tenantFeatureService.GetForTenant(tenantHint);
            var disabledFeature = requirements.FirstOrDefault(feature =>
                !availability.IsEnabled(feature)
            );
            if (string.IsNullOrWhiteSpace(disabledFeature))
            {
                await _next(context);
                return;
            }

            if (context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (IsApiRequest(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new { error = "tenant_feature_disabled", feature = disabledFeature }
                );
                return;
            }

            var featureName = Uri.EscapeDataString(disabledFeature);
            context.Response.Redirect($"/Home/FeatureUnavailable?feature={featureName}");
        }

        private static IReadOnlyList<string> ResolveRequirements(PathString path)
        {
            if (path.StartsWithSegments("/o", StringComparison.OrdinalIgnoreCase)
                && path.Value?.Contains("/visit-request", StringComparison.OrdinalIgnoreCase) == true)
            {
                var normalizedPath = path.Value.TrimEnd('/');
                return normalizedPath.EndsWith(
                    "/visit-request",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? [TenantServiceKeys.Visits, TenantServiceKeys.SelfService]
                    : [TenantServiceKeys.Visits];
            }

            if (path.StartsWithSegments("/Permits", StringComparison.OrdinalIgnoreCase))
            {
                return [TenantServiceKeys.Permits];
            }

            if (path.StartsWithSegments("/Visits", StringComparison.OrdinalIgnoreCase))
            {
                return [TenantServiceKeys.Visits];
            }

            if (path.StartsWithSegments("/Reports", StringComparison.OrdinalIgnoreCase))
            {
                var reportPath = path.Value?.TrimEnd('/') ?? string.Empty;
                if (
                    reportPath.StartsWith(
                        "/Reports/Visits",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || reportPath.StartsWith(
                        "/Reports/PrintVisitsReport",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return [TenantServiceKeys.Visits];
                }

                if (
                    !string.Equals(reportPath, "/Reports", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        reportPath,
                        "/Reports/Index",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return [TenantServiceKeys.Permits];
                }
            }

            if (path.StartsWithSegments("/VisitorWorkflow", StringComparison.OrdinalIgnoreCase))
            {
                return [TenantServiceKeys.Visits, TenantServiceKeys.SelfService];
            }

            if (path.StartsWithSegments("/Queue", StringComparison.OrdinalIgnoreCase))
            {
                return [TenantServiceKeys.Visits, TenantServiceKeys.Queue];
            }

            if (
                path.StartsWithSegments(
                    "/Display/WaitingBoard",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return [TenantServiceKeys.Visits, TenantServiceKeys.Queue, TenantServiceKeys.Gate];
            }

            if (
                path.StartsWithSegments("/Display", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/ScanConsole", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/api/Scan", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments(
                    "/Administration/Display",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return [TenantServiceKeys.Gate];
            }

            return [];
        }

        private static bool IsApiRequest(PathString path) =>
            path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
    }
}
