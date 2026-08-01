using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Controllers;

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static Task ScenarioAuthenticatedScanIdentityCannotBeSpoofed(TestFixture fixture)
    {
        var controller = new ScanController(
            fixture.PermitService,
            fixture.VisitService,
            fixture.UserAdminService,
            fixture.DisplayDeviceService
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.Name, "actual-scanner") },
                        "security-test"
                    )
                ),
            },
        };

        using var document = JsonDocument.Parse("{\"scannerUserId\":\"spoofed-scanner\"}");
        var resolver = typeof(ScanController).GetMethod(
            "ResolveEffectiveScannerUserId",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        var resolved = resolver?.Invoke(controller, new object[] { document.RootElement }) as string;

        Require(
            string.Equals(resolved, "actual-scanner", StringComparison.Ordinal),
            "authenticated scan audit identity must come from the server-side principal"
        );
        return Task.CompletedTask;
    }
}
