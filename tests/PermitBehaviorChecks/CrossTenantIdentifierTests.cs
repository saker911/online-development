using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Security;
using Xunit;

namespace PermitBehaviorChecks;

public sealed class CrossTenantIdentifierTests
{
    [Fact]
    public async Task PermitNumbersRemainUniqueAcrossTenants()
    {
        await using var fixture = new TestFixture();
        SeedOtherTenant(fixture, permitNumber: "PERMIT-00001");

        var permit = new Permit
        {
            DriverName = "Cross tenant permit",
            PermitType = Permit.PermitTypePermanent,
            PermitDate = fixture.Clock.LocalNow,
            ExpiresAt = fixture.Clock.LocalNow.AddDays(30),
        };

        fixture.PermitService.AddPermit(permit, "tester");

        Assert.Equal("PERMIT-00002", permit.PermitNumber);
    }

    [Fact]
    public async Task VisitNumbersRemainUniqueAcrossTenants()
    {
        await using var fixture = new TestFixture();
        SeedOtherTenant(fixture, visitId: "V1");

        var visit = new Visit
        {
            VisitorName = "Cross tenant visitor",
            VisitDate = fixture.Clock.LocalNow,
            Status = "Active",
        };

        fixture.VisitService.AddVisit(visit, "tester");

        Assert.Equal("V2", visit.VisitId);
    }

    private static void SeedOtherTenant(
        TestFixture fixture,
        string? permitNumber = null,
        string? visitId = null
    )
    {
        using var db = fixture.DbFactory.CreateDbContext();
        const string tenantId = "identifier-test-tenant";
        if (!db.Tenants.IgnoreQueryFilters().Any(item => item.TenantId == tenantId))
        {
            db.Tenants.Add(
                new Tenant
                {
                    TenantId = tenantId,
                    Name = "Identifier test tenant",
                    Slug = tenantId,
                    IsActive = true,
                    SubscriptionStatus = TenantSubscriptionStatuses.Active,
                }
            );
        }

        if (!string.IsNullOrWhiteSpace(permitNumber))
        {
            db.Permits.Add(
                new Permit
                {
                    TenantId = tenantId,
                    PermitNumber = permitNumber,
                    DriverName = "Existing permit",
                }
            );
        }

        if (!string.IsNullOrWhiteSpace(visitId))
        {
            db.Visits.Add(
                new Visit
                {
                    TenantId = tenantId,
                    VisitId = visitId,
                    VisitorName = "Existing visitor",
                    VisitDate = fixture.Clock.LocalNow,
                }
            );
        }

        db.SaveChanges();
    }
}
