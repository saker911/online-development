using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Users;

namespace VehiclePermitSystemWeb.Infrastructure
{
    public sealed class OrganizationOnboardingMiddleware
    {
        private readonly RequestDelegate _next;

        public OrganizationOnboardingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!RequiresOrganizationSetup(context))
            {
                await _next(context);
                return;
            }

            var userAdminService = context.RequestServices.GetRequiredService<IUserAdminService>();
            var settings = userAdminService.GetAdministrationSettings();
            var hasLogo = !string.IsNullOrWhiteSpace(settings.LogoPath);
            var hasSignature = settings.SignatureImageData is { Length: > 0 }
                || !string.IsNullOrWhiteSpace(settings.SignatureImagePath)
                || !string.IsNullOrWhiteSpace(settings.SignatureText);
            if (hasLogo && hasSignature)
            {
                await _next(context);
                return;
            }

            context.Response.Redirect("/Administration/Edit?onboarding=true");
        }

        private static bool RequiresOrganizationSetup(HttpContext context)
        {
            if (context.User.Identity?.IsAuthenticated != true
                || context.User.IsSuperAdmin()
                || !context.User.IsInRole(AppRoles.GeneralManager))
            {
                return false;
            }

            var path = context.Request.Path;
            return !path.StartsWithSegments("/o")
                && !path.StartsWithSegments("/Administration/Edit")
                && !path.StartsWithSegments("/Administration/SignaturePreview")
                && !path.StartsWithSegments("/Account/ChangePassword")
                && !path.StartsWithSegments("/Account/Logout")
                && !path.StartsWithSegments("/healthz");
        }
    }
}
