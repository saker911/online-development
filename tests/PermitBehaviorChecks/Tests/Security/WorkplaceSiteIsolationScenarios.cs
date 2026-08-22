using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Security;

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static Task ScenarioWorkplaceSiteScopeIsolatesPermitsAndVisits(TestFixture fixture)
    {
        using var db = fixture.DbFactory.CreateDbContext();
        var rawdah = new WorkplaceSite { Name = "فرع الروضة", Code = "RUH-RWD" };
        var naseem = new WorkplaceSite { Name = "فرع النسيم", Code = "RUH-NSM" };
        db.WorkplaceSites.AddRange(rawdah, naseem);
        db.SaveChanges();

        var securityManager = new UserAccount
        {
            Username = "site-security",
            DisplayName = "مدير أمن الروضة",
            FullName = "مدير أمن الروضة",
            PhoneNumber = "0500000001",
            Role = AppRoles.SecurityManager,
            WorkplaceSiteId = rawdah.Id,
            IsActive = true,
            CanViewPermits = true,
            CanApprovePermit = true,
            CanViewVisits = true,
            CanApproveVisits = true,
        };
        var generalManager = new UserAccount
        {
            Username = "site-general",
            DisplayName = "المدير العام",
            FullName = "المدير العام",
            PhoneNumber = "0500000002",
            Role = AppRoles.GeneralManager,
            IsActive = true,
        };
        var rawdahPermit = BuildSitePermit("SITE-RWD-1", rawdah.Id);
        var naseemPermit = BuildSitePermit("SITE-NSM-1", naseem.Id);
        var rawdahVisit = BuildSiteVisit("V-SITE-RWD-1", rawdah.Id);
        var naseemVisit = BuildSiteVisit("V-SITE-NSM-1", naseem.Id);

        Require(fixture.AccessControlService.CanAccessPermit(rawdahPermit, securityManager), "site security manager should see permits from their site");
        Require(fixture.AccessControlService.CanApprovePermit(rawdahPermit, securityManager), "site security manager should approve permits from their site");
        Require(!fixture.AccessControlService.CanAccessPermit(naseemPermit, securityManager), "site security manager must not see permits from another site");
        Require(!fixture.AccessControlService.CanApprovePermit(naseemPermit, securityManager), "site security manager must not approve permits from another site");
        Require(fixture.AccessControlService.CanAccessVisit(rawdahVisit, securityManager), "site security manager should see visits from their site");
        Require(fixture.AccessControlService.CanApproveVisit(rawdahVisit, securityManager), "site security manager should approve visits from their site");
        Require(!fixture.AccessControlService.CanAccessVisit(naseemVisit, securityManager), "site security manager must not see visits from another site");
        Require(!fixture.AccessControlService.CanApproveVisit(naseemVisit, securityManager), "site security manager must not approve visits from another site");
        Require(fixture.AccessControlService.CanAccessPermit(naseemPermit, generalManager), "general manager should retain cross-site access");
        Require(fixture.AccessControlService.CanAccessVisit(naseemVisit, generalManager), "general manager should retain cross-site visit access");

        return Task.CompletedTask;
    }

    private static Task ScenarioPermitListsExcludeOtherWorkplaceSites(TestFixture fixture)
    {
        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var rawdah = new WorkplaceSite { Name = "الروضة للقوائم", Code = "LIST-RWD" };
            var naseem = new WorkplaceSite { Name = "النسيم للقوائم", Code = "LIST-NSM" };
            db.WorkplaceSites.AddRange(rawdah, naseem);
            db.SaveChanges();
            db.UserAccounts.Add(
                new UserAccount
                {
                    Username = "site-list-user",
                    DisplayName = "مستخدم الروضة",
                    FullName = "مستخدم الروضة",
                    PhoneNumber = "0500000003",
                    Role = AppRoles.SecurityManager,
                    WorkplaceSiteId = rawdah.Id,
                    IsActive = true,
                    CanViewPermits = true,
                }
            );
            db.Permits.AddRange(
                BuildSitePermit("LIST-RWD-1", rawdah.Id),
                BuildSitePermit("LIST-NSM-1", naseem.Id)
            );
            db.SaveChanges();
        }

        var visibleNumbers = fixture.PermitService.GetAllPermits("site-list-user")
            .Select(permit => permit.PermitNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(visibleNumbers.Contains("LIST-RWD-1"), "permit list should contain the assigned site record");
        Require(!visibleNumbers.Contains("LIST-NSM-1"), "permit list must exclude another site's record");
        return Task.CompletedTask;
    }

    private static Permit BuildSitePermit(string permitNumber, int workplaceSiteId) =>
        new()
        {
            PermitNumber = permitNumber,
            PermitType = Permit.PermitTypePermanent,
            DriverName = "صاحب تصريح",
            NationalId = "1234567890",
            VehicleType = "Sedan",
            PlateNumber = permitNumber,
            EmployeePhone = "0500000004",
            PermitDate = AppClock.LocalNow,
            ExpiresAt = AppClock.LocalNow.AddDays(1),
            ApprovalStatus = "Pending",
            CurrentState = "Outside",
            WorkplaceSiteId = workplaceSiteId,
        };

    private static Visit BuildSiteVisit(string visitId, int workplaceSiteId) =>
        new()
        {
            VisitId = visitId,
            VisitorName = "زائر",
            NationalId = "1234567890",
            PhoneNumber = "0500000005",
            VisitDate = AppClock.LocalNow.AddHours(1),
            ApprovalStatus = "Pending",
            Status = "Active",
            WorkplaceSiteId = workplaceSiteId,
        };
}
