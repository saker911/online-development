using Microsoft.AspNetCore.Http;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Audit;
using VehiclePermitSystemWeb.Services.Backup;
using VehiclePermitSystemWeb.Services.Bootstrap;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Delegations;
using VehiclePermitSystemWeb.Services.Gate;
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Infrastructure
{
    public sealed class FirstRunSetupMiddleware
    {
        private static readonly PathString InitialSetupPath = new("/Account/InitialSetup");
        private static readonly PathString LoginPath = new("/Account/Login");

        private readonly RequestDelegate _next;

        public FirstRunSetupMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IUserAdminService userAdminService)
        {
            if (!userAdminService.IsInitialSetupRequired() || IsExcludedPath(context.Request.Path))
            {
                await _next(context);
                return;
            }

            context.Response.Redirect(InitialSetupPath.Value!);
        }

        private static bool IsExcludedPath(PathString path)
        {
            if (!path.HasValue)
            {
                return true;
            }

            return path.StartsWithSegments(InitialSetupPath, StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments(LoginPath, StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/healthz", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/favicon.ico", StringComparison.OrdinalIgnoreCase);
        }
    }
}
