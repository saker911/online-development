using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using VehiclePermitSystemWeb.Infrastructure;
using VehiclePermitSystemWeb.Security;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Security)]
public sealed class PlatformOwnerIsolationTests
{
    [Theory]
    [InlineData("/People")]
    [InlineData("/Permits")]
    [InlineData("/Visits")]
    [InlineData("/Attendance")]
    [InlineData("/Display/Gate")]
    [InlineData("/Users")]
    [InlineData("/Users/Create")]
    [InlineData("/Users/PrintUserBadge/tenant.manager")]
    [InlineData("/Users/PrintOperatorBadge/tenant.manager")]
    [InlineData("/")]
    public async Task PlatformOwnerGetRequestsCannotEnterOperationalApplication(string path)
    {
        var nextCalled = false;
        var middleware = new PlatformOwnerIsolationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = BuildContext(path, HttpMethods.Get, isSuperAdmin: true);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/Platform", context.Response.Headers.Location);
    }

    [Fact]
    public async Task PlatformOwnerPostRequestsCannotModifyOperationalData()
    {
        var middleware = new PlatformOwnerIsolationMiddleware(_ => Task.CompletedTask);
        var context = BuildContext("/Permits/Create", HttpMethods.Post, isSuperAdmin: true);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/Platform")]
    [InlineData("/Tenants")]
    [InlineData("/Pricing")]
    [InlineData("/PlatformSettings")]
    [InlineData("/Users/Edit/tenant.manager")]
    [InlineData("/Users/ActivityLog")]
    [InlineData("/Account/Profile")]
    public async Task PlatformOwnerCanUsePlatformAndAccountManagementPaths(string path)
    {
        var nextCalled = false;
        var middleware = new PlatformOwnerIsolationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = BuildContext(path, HttpMethods.Get, isSuperAdmin: true);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task TenantUsersRemainUnaffectedByPlatformIsolation()
    {
        var nextCalled = false;
        var middleware = new PlatformOwnerIsolationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = BuildContext("/People", HttpMethods.Get, isSuperAdmin: false);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private static DefaultHttpContext BuildContext(string path, string method, bool isSuperAdmin)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "isolation-test") };
        if (isSuperAdmin)
        {
            claims.Add(new Claim(AppClaimTypes.SuperAdmin, "true"));
        }

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
            Request = { Path = path, Method = method },
        };
    }
}
