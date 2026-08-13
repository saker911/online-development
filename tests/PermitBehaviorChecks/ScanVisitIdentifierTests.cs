using System.Reflection;
using VehiclePermitSystemWeb.Controllers;
using VehiclePermitSystemWeb.Models.Entities;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.VehicleScan)]
public sealed class ScanVisitIdentifierTests
{
    [Theory]
    [InlineData("60")]
    [InlineData("٦٠")]
    public async Task BareVisitNumberResolvesToExistingVisit(string scannedValue)
    {
        await using var fixture = new TestFixture();
        using (var db = fixture.DbFactory.CreateDbContext())
        {
            db.Visits.Add(
                new Visit
                {
                    VisitId = "V60",
                    VisitorName = "زائر اختبار المسح",
                    VisitLocation = "البوابة الرئيسية",
                    VisitDate = fixture.Clock.LocalNow.AddMinutes(10),
                    ApprovalStatus = "Approved",
                    Status = "Active",
                }
            );
            db.SaveChanges();
        }

        var controller = new ScanController(
            fixture.PermitService,
            fixture.VisitService,
            fixture.UserAdminService,
            fixture.DisplayDeviceService
        );
        var resolver = typeof(ScanController).GetMethod(
            "ResolveAutoIdentifier",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        var result = ((string Identifier, string ScanMode))resolver!.Invoke(
            controller,
            [scannedValue]
        )!;

        Assert.Equal("V60", result.Identifier);
        Assert.Equal("visit", result.ScanMode);
    }

    [Theory]
    [InlineData("V60")]
    [InlineData("V-060")]
    [InlineData("V٦٠")]
    public void VisitPrefixIsNormalizedConsistently(string scannedValue)
    {
        var normalizer = typeof(ScanController).GetMethod(
            "NormalizeVisitIdentifier",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        Assert.Equal("V60", normalizer!.Invoke(null, [scannedValue]));
    }
}
