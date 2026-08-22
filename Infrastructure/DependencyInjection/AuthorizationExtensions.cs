using Microsoft.AspNetCore.Authentication.Cookies;
using VehiclePermitSystemWeb.Security;

namespace VehiclePermitSystemWeb.Infrastructure.DependencyInjection
{
    public static class AuthorizationExtensions
    {
        public static IServiceCollection AddVehiclePermitAuthorization(
            this IServiceCollection services
        )
        {
            services.AddAuthorization(options =>
            {
                options.AddPolicy(
                    AppPolicies.ViewDashboard,
                    policy =>
                        policy.RequireClaim(AppPermissions.ClaimType, AppPermissions.ViewDashboard)
                );
                options.AddPolicy(
                    AppPolicies.ViewPermits,
                    policy =>
                        policy.RequireAssertion(context =>
                            context.User.HasPermission(AppPermissions.ViewPermits)
                            || context.User.HasPermission(AppPermissions.ViewVisitorPermits)
                            || context.User.HasPermission(AppPermissions.ApprovePermit)
                            || context.User.HasPermission(AppPermissions.EditPermit)
                            || context.User.HasPermission(AppPermissions.EditVisitorPermit)
                            || context.User.HasPermission(AppPermissions.StopPermit)
                            || context.User.HasPermission(AppPermissions.ReviewUnauthorizedExit)
                        )
                );
                options.AddPolicy(
                    AppPolicies.ViewVisitorPermits,
                    policy =>
                        policy.RequireClaim(
                            AppPermissions.ClaimType,
                            AppPermissions.ViewVisitorPermits
                        )
                );
                options.AddPolicy(
                    AppPolicies.CreatePermits,
                    policy =>
                        policy.RequireAssertion(context =>
                            context.User.HasPermission(AppPermissions.CreatePermit)
                            || context.User.HasPermission(AppPermissions.CreateVisitorPermit)
                        )
                );
                options.AddPolicy(
                    AppPolicies.CreateVisitorPermits,
                    policy =>
                        policy.RequireClaim(
                            AppPermissions.ClaimType,
                            AppPermissions.CreateVisitorPermit
                        )
                );
                options.AddPolicy(
                    AppPolicies.EditPermits,
                    policy =>
                        policy.RequireAssertion(context =>
                            context.User.HasPermission(AppPermissions.EditPermit)
                            || context.User.HasPermission(AppPermissions.EditVisitorPermit)
                        )
                );
                options.AddPolicy(
                    AppPolicies.EditVisitorPermits,
                    policy =>
                        policy.RequireClaim(
                            AppPermissions.ClaimType,
                            AppPermissions.EditVisitorPermit
                        )
                );
                options.AddPolicy(
                    AppPolicies.ApprovePermits,
                    policy =>
                        policy.RequireClaim(AppPermissions.ClaimType, AppPermissions.ApprovePermit)
                );
                options.AddPolicy(
                    AppPolicies.ApproveLeaveRequests,
                    policy =>
                        policy.RequireClaim(
                            AppPermissions.ClaimType,
                            AppPermissions.ApproveLeaveRequest
                        )
                );
                options.AddPolicy(
                    AppPolicies.StopPermits,
                    policy =>
                        policy.RequireAssertion(context =>
                            context.User.HasPermission(AppPermissions.StopPermit)
                        )
                );
                options.AddPolicy(
                    AppPolicies.ReviewUnauthorizedExits,
                    policy =>
                        policy.RequireAssertion(context =>
                            context.User.HasPermission(AppPermissions.ReviewUnauthorizedExit)
                        )
                );
                options.AddPolicy(
                    AppPolicies.ViewVisits,
                    policy =>
                        policy.RequireAssertion(context =>
                            context.User.HasPermission(AppPermissions.ViewVisits)
                            || context.User.HasPermission(AppPermissions.ApproveVisits)
                            || context.User.HasPermission(AppPermissions.ApproveDetainedVisit)
                        )
                );
                options.AddPolicy(
                    AppPolicies.CreateVisits,
                    policy =>
                        policy.RequireClaim(AppPermissions.ClaimType, AppPermissions.CreateVisit)
                );
                options.AddPolicy(
                    AppPolicies.EditVisits,
                    policy =>
                        policy.RequireClaim(AppPermissions.ClaimType, AppPermissions.EditVisit)
                );
                options.AddPolicy(
                    AppPolicies.ApproveDetainedVisits,
                    policy =>
                        policy.RequireAssertion(context =>
                            context.User.HasPermission(AppPermissions.ApproveDetainedVisit)
                        )
                );
                options.AddPolicy(
                    AppPolicies.ManageVisitQueue,
                    policy =>
                        policy.RequireAssertion(context =>
                            context.User.HasPermission(AppPermissions.ApproveVisits)
                            || context.User.HasPermission(AppPermissions.ScanOperations)
                            || context.User.HasPermission(AppPermissions.CreateVisit)
                        )
                );
                options.AddPolicy(
                    AppPolicies.ViewDisplays,
                    policy =>
                        policy.RequireClaim(AppPermissions.ClaimType, AppPermissions.ViewDisplays)
                );
                options.AddPolicy(
                    AppPolicies.ScanOperations,
                    policy =>
                        policy.RequireClaim(AppPermissions.ClaimType, AppPermissions.ScanOperations)
                );
                options.AddPolicy(
                    AppPolicies.ManageUsers,
                    policy =>
                        policy.RequireClaim(AppPermissions.ClaimType, AppPermissions.ManageUsers)
                );
                options.AddPolicy(
                    AppPolicies.ManageDepartments,
                    policy =>
                        policy.RequireClaim(
                            AppPermissions.ClaimType,
                            AppPermissions.ManageDepartments
                        )
                );
                options.AddPolicy(
                    AppPolicies.ManageAdministration,
                    policy =>
                        policy.RequireClaim(
                            AppPermissions.ClaimType,
                            AppPermissions.ManageAdministration
                        )
                );
                options.AddPolicy(
                    AppPolicies.ManageDelegations,
                    policy =>
                        policy.RequireClaim(
                            AppPermissions.ClaimType,
                            AppPermissions.ManageDelegations
                        )
                );
            });

            return services;
        }

        public static IServiceCollection AddVehiclePermitCookieAuthentication(
            this IServiceCollection services,
            CookieSecurePolicy cookieSecurePolicy,
            IConfiguration configuration
        )
        {
            var authentication = services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Account/Login";
                    options.AccessDeniedPath = "/Home/AccessDenied";
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
                    options.SlidingExpiration = true;
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.Cookie.SecurePolicy = cookieSecurePolicy;
                })
                .AddCookie(ExternalAuthenticationDefaults.CookieScheme, options =>
                {
                    options.Cookie.Name = "VehiclePermit.External";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.Cookie.SecurePolicy = cookieSecurePolicy;
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                });

            var googleClientId = configuration["Authentication:Google:ClientId"]?.Trim();
            var googleClientSecret = configuration["Authentication:Google:ClientSecret"]?.Trim();
            if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
            {
                authentication.AddGoogle(ExternalAuthenticationDefaults.GoogleScheme, options =>
                {
                    options.SignInScheme = ExternalAuthenticationDefaults.CookieScheme;
                    options.ClientId = googleClientId;
                    options.ClientSecret = googleClientSecret;
                    options.CallbackPath = "/signin-google";
                    options.SaveTokens = false;
                    options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                    options.CorrelationCookie.SecurePolicy = cookieSecurePolicy;
                });
            }

            return services;
        }
    }
}
