using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
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

namespace VehiclePermitSystemWeb.Security
{
    public sealed class DelegationClaimsTransformation : IClaimsTransformation
    {
        private readonly IUserAdminService _userAdminService;
        private readonly IDelegationService _delegationService;

        public DelegationClaimsTransformation(
            IUserAdminService userAdminService,
            IDelegationService delegationService
        )
        {
            _userAdminService = userAdminService;
            _delegationService = delegationService;
        }

        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (!(principal.Identity?.IsAuthenticated ?? false))
            {
                return Task.FromResult(principal);
            }

            var username = principal.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
            {
                return Task.FromResult(principal);
            }

            var currentUser = _userAdminService.GetUserAccount(username);
            if (currentUser == null)
            {
                return Task.FromResult(principal);
            }

            var effectivePermissions = _delegationService.GetEffectivePermissions(currentUser);
            var missingPermissions = effectivePermissions
                .Where(permission => !principal.HasClaim(AppPermissions.ClaimType, permission))
                .ToList();
            if (missingPermissions.Count == 0)
            {
                return Task.FromResult(principal);
            }

            var clonedPrincipal = new ClaimsPrincipal(
                principal.Identities.Select(identity => new ClaimsIdentity(identity))
            );
            if (clonedPrincipal.Identity is not ClaimsIdentity identityClone)
            {
                return Task.FromResult(principal);
            }

            foreach (var permission in missingPermissions)
            {
                identityClone.AddClaim(new Claim(AppPermissions.ClaimType, permission));
            }

            return Task.FromResult<ClaimsPrincipal>(clonedPrincipal);
        }
    }
}
