using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Models.Entities;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.VehicleScan)]
public sealed class GateDeviceIsolationTests
{
    [Fact]
    public async Task RecentGateActivityIsIsolatedByDeviceId()
    {
        await using var fixture = new TestFixture();
        using (var db = fixture.DbFactory.CreateDbContext())
        {
            db.Permits.Add(new Permit
            {
                PermitNumber = "PERMIT-ISOLATION",
                DriverName = "مستخدم الاختبار",
                ApprovalStatus = "Approved",
            });
            db.PermitActivities.AddRange(
                new PermitActivity
                {
                    PermitNumber = "PERMIT-ISOLATION",
                    DriverName = "مستخدم الاختبار",
                    DeviceId = "user:tester:device-a",
                    ActionLabel = "دخول",
                    OccurredAt = fixture.Clock.UtcNow,
                },
                new PermitActivity
                {
                    PermitNumber = "PERMIT-ISOLATION",
                    DriverName = "مستخدم الاختبار",
                    DeviceId = "user:tester:device-b",
                    ActionLabel = "خروج",
                    OccurredAt = fixture.Clock.UtcNow.AddSeconds(1),
                }
            );
            db.SaveChanges();
        }

        var deviceA = fixture.PermitService
            .GetRecentPermitActivitiesForDevice("user:tester:device-a", 10, "tester")
            .ToList();
        var deviceB = fixture.PermitService
            .GetRecentPermitActivitiesForDevice("user:tester:device-b", 10, "tester")
            .ToList();

        Assert.Single(deviceA);
        Assert.Equal("دخول", deviceA[0].ActionLabel);
        Assert.Single(deviceB);
        Assert.Equal("خروج", deviceB[0].ActionLabel);
    }
}
