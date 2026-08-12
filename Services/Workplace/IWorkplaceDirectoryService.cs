using VehiclePermitSystemWeb.Models.ViewModels.Workplace;

namespace VehiclePermitSystemWeb.Services.Workplace
{
    public interface IWorkplaceDirectoryService
    {
        PeopleDirectoryViewModel BuildPeopleDirectory(
            string? query,
            string? personType,
            int page,
            int pageSize
        );

        PersonDetailsViewModel? BuildPersonDetails(long id);
        KnownVisitorPrefillViewModel? BuildKnownVisitorPrefill(long id);
        PersonPhotoViewModel? GetPersonPhoto(long id);
        bool SavePersonPhoto(long id, byte[] data, string contentType, out string error);
        bool DeletePersonPhoto(long id, out string error);

        AttendanceDashboardViewModel BuildAttendanceDashboard(
            string? query,
            string? personType
        );

        WorkplaceSitesViewModel BuildSites(int? editId = null);
        IReadOnlyList<WorkplaceLocationOptionViewModel> GetLocationOptions();
        bool ResolveLocation(
            int? siteId,
            int? entranceId,
            string service,
            out string siteName,
            out string entranceName,
            out string error
        );
        WorkplaceSiteDetailsViewModel? BuildSiteDetails(int id);
        bool SaveSite(WorkplaceSiteInputViewModel model, out string error);
        bool ToggleSite(int id, out string error);
        bool DeleteSite(int id, out string error);
        bool SaveEntrance(WorkplaceSiteEntranceInputViewModel model, out string error);
        bool ToggleEntrance(int siteId, int entranceId, out string error);
        bool DeleteEntrance(int siteId, int entranceId, out string error);
        bool AssignDevice(WorkplaceSiteDeviceAssignmentViewModel model, out string error);
        bool UnassignDevice(int siteId, int deviceId, out string error);
    }
}
