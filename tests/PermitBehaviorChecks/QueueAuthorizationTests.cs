using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using VehiclePermitSystemWeb.Infrastructure.DependencyInjection;
using VehiclePermitSystemWeb.Security;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Security)]
public sealed class QueueAuthorizationTests
{
    [Fact]
    public async Task QueueManagementAllowsApprovalGateOrReceptionPermission()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVehiclePermitAuthorization();
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var viewer = CreatePrincipal(AppPermissions.ViewVisits);
        var approver = CreatePrincipal(AppPermissions.ApproveVisits);
        var gateOperator = CreatePrincipal(AppPermissions.ScanOperations);
        var receptionist = CreatePrincipal(AppPermissions.CreateVisit);

        Assert.False(
            (await authorization.AuthorizeAsync(viewer, null, AppPolicies.ManageVisitQueue))
                .Succeeded
        );
        Assert.True(
            (await authorization.AuthorizeAsync(approver, null, AppPolicies.ManageVisitQueue))
                .Succeeded
        );
        Assert.True(
            (await authorization.AuthorizeAsync(gateOperator, null, AppPolicies.ManageVisitQueue))
                .Succeeded
        );
        Assert.True(
            (await authorization.AuthorizeAsync(receptionist, null, AppPolicies.ManageVisitQueue))
                .Succeeded
        );
    }

    private static ClaimsPrincipal CreatePrincipal(string permission) =>
        new(
            new ClaimsIdentity(
                [new Claim(AppPermissions.ClaimType, permission)],
                "Test"
            )
        );
}
