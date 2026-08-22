using VehiclePermitSystemWeb.Security;

namespace VehiclePermitSystemWeb.Infrastructure
{
    public sealed class PlatformOwnerIsolationMiddleware
    {
        private static readonly string[] PlatformPaths =
        [
            "/Platform",
            "/Tenants",
            "/Pricing",
            "/PlatformSettings",
        ];

        private static readonly string[] AccountManagementPaths =
        [
            "/Users/Edit",
            "/Users/ActivityLog",
            "/Users/PrintActivityLog",
            "/Users/ResetTemporaryPassword",
            "/Users/Activate",
            "/Users/Deactivate",
            "/Users/ApplyRoleDefaults",
            "/Users/WizardIdentityLookup",
            "/Users/WizardReactivate",
        ];

        private static readonly string[] PublicAndAccountPaths =
        [
            "/Account",
            "/Home/Landing",
            "/about",
            "/privacy",
            "/terms",
            "/security",
            "/refund",
            "/establishment",
            "/healthz",
        ];

        private static readonly PathString PlatformHome = new("/Platform");

        private readonly RequestDelegate _next;

        public PlatformOwnerIsolationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.User.Identity?.IsAuthenticated != true || !context.User.IsSuperAdmin())
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path;
            if (IsAllowed(path))
            {
                await _next(context);
                return;
            }

            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.Redirect(PlatformHome);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        }

        internal static bool IsAllowed(PathString path)
        {
            if (path == "/")
            {
                return false;
            }

            return PlatformPaths.Any(item => path.StartsWithSegments(item, StringComparison.OrdinalIgnoreCase))
                || AccountManagementPaths.Any(item => path.StartsWithSegments(item, StringComparison.OrdinalIgnoreCase))
                || PublicAndAccountPaths.Any(item => path.StartsWithSegments(item, StringComparison.OrdinalIgnoreCase));
        }
    }
}
