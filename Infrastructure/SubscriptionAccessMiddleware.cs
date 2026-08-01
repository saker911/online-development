using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Users;

namespace VehiclePermitSystemWeb.Infrastructure
{
    public sealed class SubscriptionAccessMiddleware
    {
        private static readonly string[] AllowedAccountPaths =
        [
            "/Account/Profile",
            "/Account/ChangePassword",
            "/Account/Logout",
            "/Account/LinkExternalLogin",
            "/Account/LinkExternalLoginCallback",
        ];

        private readonly RequestDelegate _next;

        public SubscriptionAccessMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IUserAdminService userAdminService)
        {
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            var username = context.User.Identity.Name ?? string.Empty;
            var user = userAdminService.GetUserAccount(username);
            if (user == null || AppRoles.IsSuperAdmin(user))
            {
                await _next(context);
                return;
            }

            var tenant = userAdminService
                .GetTenants(includeInactive: true)
                .FirstOrDefault(item =>
                    string.Equals(item.TenantId, user.TenantId, StringComparison.OrdinalIgnoreCase)
            );
            if (
                (tenant != null && !IsSubscriptionRestricted(tenant, DateTime.UtcNow))
                || IsAllowedPath(context.Request.Path)
            )
            {
                await _next(context);
                return;
            }

            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            context.Response.Redirect("/Account/Profile?subscription=required");
        }

        public static bool IsSubscriptionRestricted(Tenant tenant, DateTime utcNow)
        {
            if (!tenant.IsActive)
            {
                return true;
            }

            var status = TenantSubscriptionStatuses.Normalize(tenant.SubscriptionStatus);
            if (
                string.Equals(status, TenantSubscriptionStatuses.PendingPayment, StringComparison.Ordinal)
                || string.Equals(status, TenantSubscriptionStatuses.Suspended, StringComparison.Ordinal)
                || string.Equals(status, TenantSubscriptionStatuses.Expired, StringComparison.Ordinal)
            )
            {
                return true;
            }

            if (
                string.Equals(status, TenantSubscriptionStatuses.Trial, StringComparison.Ordinal)
                && tenant.TrialEndsAtUtc.HasValue
                && tenant.TrialEndsAtUtc.Value < utcNow
            )
            {
                return true;
            }

            return tenant.SubscriptionEndsAtUtc.HasValue
                && tenant.SubscriptionEndsAtUtc.Value < utcNow;
        }

        private static bool IsAllowedPath(PathString path)
        {
            if (path.StartsWithSegments("/Subscription", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return AllowedAccountPaths.Any(allowedPath =>
                path.StartsWithSegments(allowedPath, StringComparison.OrdinalIgnoreCase)
            );
        }
    }
}
