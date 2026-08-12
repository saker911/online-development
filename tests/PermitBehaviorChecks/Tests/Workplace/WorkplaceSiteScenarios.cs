using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Workplace;

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static Task ScenarioWorkplaceSiteOwnsEntrancesAndDeviceAssignments(
        TestFixture fixture
    )
    {
        var directory = fixture.WorkplaceDirectoryService;
        Require(
            directory.SaveSite(
                new WorkplaceSiteInputViewModel
                {
                    Name = "المقر التشغيلي",
                    Code = "OPS-HQ",
                    Address = "الرياض",
                    GeofenceRadiusMeters = 200,
                    PermitsEnabled = true,
                    VisitsEnabled = true,
                    SelfServiceEnabled = true,
                    QueueEnabled = true,
                    GateEnabled = true,
                    IsActive = true,
                },
                out var siteError
            ),
            $"site should be created: {siteError}"
        );

        var site = directory.BuildSites().Sites.Single(item => item.Code == "OPS-HQ");
        Require(
            directory.SaveEntrance(
                new WorkplaceSiteEntranceInputViewModel
                {
                    WorkplaceSiteId = site.Id,
                    Name = "البوابة الرئيسية",
                    Code = "GATE-A",
                    LocationDescription = "الواجهة الشمالية",
                },
                out var entranceError
            ),
            $"entrance should be created: {entranceError}"
        );

        int deviceId;
        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var device = new DisplayDevice
            {
                ScreenName = "جهاز الحارس",
                ScreenLocation = "غير مسند",
                Mode = DisplayDeviceModes.Gate,
                Status = DisplayDeviceStatuses.Approved,
                RequestCode = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = fixture.Clock.UtcNow,
                LastSeenUtc = fixture.Clock.UtcNow,
            };
            db.DisplayDevices.Add(device);
            db.SaveChanges();
            deviceId = device.Id;
        }

        var details = directory.BuildSiteDetails(site.Id)!;
        var entrance = details.Entrances.Single();
        Require(details.AvailableDevices.Any(device => device.Id == deviceId), "device should be available");
        Require(
            directory.AssignDevice(
                new WorkplaceSiteDeviceAssignmentViewModel
                {
                    WorkplaceSiteId = site.Id,
                    WorkplaceSiteEntranceId = entrance.Id,
                    DisplayDeviceId = deviceId,
                },
                out var assignmentError
            ),
            $"device assignment should succeed: {assignmentError}"
        );

        details = directory.BuildSiteDetails(site.Id)!;
        Require(details.Devices.Count == 1, "site should expose assigned device");
        Require(details.Devices[0].EntranceName == entrance.Name, "device should inherit entrance");
        Require(details.Site.OnlineDeviceCount == 1, "recent approved device should be online");
        Require(
            directory.ResolveLocation(
                site.Id,
                entrance.Id,
                "self-service-visits",
                out var siteName,
                out var entranceName,
                out _
            ) && siteName == site.Name && entranceName == entrance.Name,
            "location resolver should return the selected site and entrance"
        );
        Require(
            directory.SaveSite(
                new WorkplaceSiteInputViewModel
                {
                    Name = "الفرع المنسوخ",
                    Code = "OPS-COPY",
                    CopyFromSiteId = site.Id,
                    IsActive = true,
                },
                out var copyError
            ),
            $"site template should be copied: {copyError}"
        );
        var copied = directory.BuildSites().Sites.Single(item => item.Code == "OPS-COPY");
        var copiedDetails = directory.BuildSiteDetails(copied.Id)!;
        Require(copied.QueueEnabled, "copied site should inherit service switches");
        Require(copiedDetails.Entrances.Count == 1, "copied site should inherit entrances");
        Require(directory.DeleteEntrance(copied.Id, copiedDetails.Entrances[0].Id, out _), "copied entrance should be removable");
        Require(directory.DeleteSite(copied.Id, out _), "copied site should be removable");
        Require(
            !directory.DeleteSite(site.Id, out _),
            "site with assigned devices must not be deleted"
        );
        Require(directory.UnassignDevice(site.Id, deviceId, out _), "device should be unassigned");
        Require(directory.DeleteEntrance(site.Id, entrance.Id, out _), "entrance should be deleted");
        Require(directory.DeleteSite(site.Id, out _), "empty site should be deleted");

        return Task.CompletedTask;
    }

    private static Task ScenarioEmergencySessionTracksSitePresence(TestFixture fixture)
    {
        var directory = fixture.WorkplaceDirectoryService;
        Require(directory.SaveSite(new WorkplaceSiteInputViewModel
        {
            Name = "موقع الطوارئ",
            Code = "EMR",
            IsActive = true,
        }, out var siteError), $"emergency site should be created: {siteError}");
        var site = directory.BuildSites().Sites.Single(item => item.Code == "EMR");

        Require(
            fixture.EmergencyService.StartSession(site.Id, "tester", out var error),
            $"emergency session should start: {error}"
        );
        var dashboard = fixture.EmergencyService.BuildDashboard(site.Id);
        Require(dashboard.ActiveSession != null, "active emergency session should be visible");
        Require(
            !fixture.EmergencyService.StartSession(site.Id, "tester", out _),
            "a site must not have two active emergency sessions"
        );
        Require(
            fixture.EmergencyService.CompleteSession(
                dashboard.ActiveSession!.Id,
                "tester",
                out error
            ),
            $"emergency session should complete: {error}"
        );
        Require(
            fixture.EmergencyService.BuildDashboard(site.Id).ActiveSession == null,
            "completed session should leave no active session"
        );
        return Task.CompletedTask;
    }
}
