using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Workplace
{
    public sealed class PeopleDirectoryViewModel
    {
        public string Query { get; set; } = string.Empty;
        public string PersonType { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 12;
        public int TotalCount { get; set; }
        public int TotalPages { get; set; } = 1;
        public int EmployeeCount { get; set; }
        public int VisitorCount { get; set; }
        public int ContractorCount { get; set; }
        public int OnSiteCount { get; set; }
        public IReadOnlyList<PersonDirectoryItemViewModel> People { get; set; } =
            Array.Empty<PersonDirectoryItemViewModel>();
    }

    public sealed class PersonDirectoryItemViewModel
    {
        public long Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string PersonType { get; set; } = string.Empty;
        public string PersonTypeDisplay { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string NationalIdMasked { get; set; } = string.Empty;
        public string PhoneNumberMasked { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public bool IsOnSite { get; set; }
        public bool HasPhoto { get; set; }
        public DateTime? LastActivityAt { get; set; }
    }

    public sealed class PersonDetailsViewModel
    {
        public long Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string PersonType { get; set; } = string.Empty;
        public string PersonTypeDisplay { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string NationalIdMasked { get; set; } = string.Empty;
        public string PhoneNumberMasked { get; set; } = string.Empty;
        public string EmailMasked { get; set; } = string.Empty;
        public string EmployeeNumber { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Organization { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool HasPhoto { get; set; }
        public bool IsOnSite { get; set; }
        public string CurrentLocation { get; set; } = string.Empty;
        public DateTime? CurrentPresenceSince { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public IReadOnlyList<PersonRecordItemViewModel> Records { get; set; } =
            Array.Empty<PersonRecordItemViewModel>();
        public IReadOnlyList<WorkplaceActivityItemViewModel> Timeline { get; set; } =
            Array.Empty<WorkplaceActivityItemViewModel>();
    }

    public sealed class PersonRecordItemViewModel
    {
        public string RecordType { get; set; } = string.Empty;
        public string RecordTypeDisplay { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime? OccurredAt { get; set; }
    }

    public sealed class KnownVisitorPrefillViewModel
    {
        public long PersonId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string NationalId { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public sealed class AttendanceDashboardViewModel
    {
        public string Query { get; set; } = string.Empty;
        public string PersonType { get; set; } = string.Empty;
        public int TotalOnSite { get; set; }
        public int EmployeesOnSite { get; set; }
        public int VisitorsOnSite { get; set; }
        public int ContractorsOnSite { get; set; }
        public int ActiveSiteCount { get; set; }
        public DateTime UpdatedAt { get; set; }
        public IReadOnlyList<PresenceItemViewModel> PresentPeople { get; set; } =
            Array.Empty<PresenceItemViewModel>();
        public IReadOnlyList<WorkplaceActivityItemViewModel> RecentActivity { get; set; } =
            Array.Empty<WorkplaceActivityItemViewModel>();
    }

    public sealed class PresenceItemViewModel
    {
        public long PersonId { get; set; }
        public bool HasPhoto { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string PersonType { get; set; } = string.Empty;
        public string PersonTypeDisplay { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public DateTime? EnteredAt { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    public sealed class PersonPhotoViewModel
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "image/jpeg";
    }

    public sealed class WorkplaceActivityItemViewModel
    {
        public string Reference { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; }
        public bool IsEntry { get; set; }
    }

    public sealed class WorkplaceSitesViewModel
    {
        public WorkplaceSiteInputViewModel Input { get; set; } = new();
        public IReadOnlyList<WorkplaceSiteItemViewModel> Sites { get; set; } =
            Array.Empty<WorkplaceSiteItemViewModel>();
    }

    public sealed class WorkplaceSiteItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int GeofenceRadiusMeters { get; set; }
        public bool IsActive { get; set; }
        public int CurrentPeopleCount { get; set; }
        public int EntranceCount { get; set; }
        public int DeviceCount { get; set; }
        public int OnlineDeviceCount { get; set; }
        public bool PermitsEnabled { get; set; }
        public bool VisitsEnabled { get; set; }
        public bool SelfServiceEnabled { get; set; }
        public bool QueueEnabled { get; set; }
        public bool GateEnabled { get; set; }
    }

    public sealed class WorkplaceSiteInputViewModel
    {
        public int? Id { get; set; }
        public int? CopyFromSiteId { get; set; }

        [Required(ErrorMessage = "اسم الموقع مطلوب.")]
        [StringLength(128)]
        public string Name { get; set; } = string.Empty;

        [StringLength(32)]
        public string Code { get; set; } = string.Empty;

        [StringLength(256)]
        public string Address { get; set; } = string.Empty;

        [Range(25, 5000, ErrorMessage = "نطاق الموقع يجب أن يكون بين 25 و5000 متر.")]
        public int GeofenceRadiusMeters { get; set; } = 150;
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public bool PermitsEnabled { get; set; } = true;
        public bool VisitsEnabled { get; set; } = true;
        public bool SelfServiceEnabled { get; set; } = true;
        public bool QueueEnabled { get; set; }
        public bool GateEnabled { get; set; } = true;
        public bool IsActive { get; set; } = true;
    }

    public sealed class WorkplaceSiteDetailsViewModel
    {
        public WorkplaceSiteItemViewModel Site { get; set; } = new();
        public WorkplaceSiteEntranceInputViewModel EntranceInput { get; set; } = new();
        public WorkplaceSiteDeviceAssignmentViewModel DeviceAssignment { get; set; } = new();
        public IReadOnlyList<WorkplaceSiteEntranceItemViewModel> Entrances { get; set; } =
            Array.Empty<WorkplaceSiteEntranceItemViewModel>();
        public IReadOnlyList<WorkplaceSiteDeviceItemViewModel> Devices { get; set; } =
            Array.Empty<WorkplaceSiteDeviceItemViewModel>();
        public IReadOnlyList<WorkplaceUnassignedDeviceItemViewModel> AvailableDevices { get; set; } =
            Array.Empty<WorkplaceUnassignedDeviceItemViewModel>();
    }

    public sealed class WorkplaceSiteEntranceItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string LocationDescription { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int DeviceCount { get; set; }
    }

    public sealed class WorkplaceSiteEntranceInputViewModel
    {
        public int WorkplaceSiteId { get; set; }

        [Required(ErrorMessage = "اسم المدخل مطلوب.")]
        [StringLength(128)]
        public string Name { get; set; } = string.Empty;

        [StringLength(32)]
        public string Code { get; set; } = string.Empty;

        [StringLength(256)]
        public string LocationDescription { get; set; } = string.Empty;
    }

    public sealed class WorkplaceSiteDeviceAssignmentViewModel
    {
        public int WorkplaceSiteId { get; set; }
        public int DisplayDeviceId { get; set; }
        public int? WorkplaceSiteEntranceId { get; set; }
    }

    public sealed class WorkplaceSiteDeviceItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string ModeDisplay { get; set; } = string.Empty;
        public string EntranceName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusDisplay { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public DateTime? LastSeenUtc { get; set; }
    }

    public sealed class WorkplaceUnassignedDeviceItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string ModeDisplay { get; set; } = string.Empty;
    }

    public sealed class WorkplaceLocationOptionViewModel
    {
        public int SiteId { get; set; }
        public string SiteName { get; set; } = string.Empty;
        public bool PermitsEnabled { get; set; }
        public bool VisitsEnabled { get; set; }
        public bool SelfServiceEnabled { get; set; }
        public bool QueueEnabled { get; set; }
        public bool GateEnabled { get; set; }
        public IReadOnlyList<WorkplaceEntranceOptionViewModel> Entrances { get; set; } =
            Array.Empty<WorkplaceEntranceOptionViewModel>();
    }

    public sealed class WorkplaceEntranceOptionViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
