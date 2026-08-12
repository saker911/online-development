namespace VehiclePermitSystemWeb.Models.ViewModels.Workplace
{
    public sealed class EmergencyDashboardViewModel
    {
        public int? SelectedSiteId { get; set; }
        public IReadOnlyList<WorkplaceLocationOptionViewModel> Sites { get; set; } =
            Array.Empty<WorkplaceLocationOptionViewModel>();
        public EmergencySessionViewModel? ActiveSession { get; set; }
        public IReadOnlyList<EmergencySessionSummaryViewModel> RecentSessions { get; set; } =
            Array.Empty<EmergencySessionSummaryViewModel>();
    }

    public sealed class EmergencySessionViewModel
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string SiteName { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public string StartedBy { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int SafeCount { get; set; }
        public int AssistanceCount { get; set; }
        public int MissingCount { get; set; }
        public int PendingCount { get; set; }
        public IReadOnlyList<EmergencyMemberViewModel> Members { get; set; } =
            Array.Empty<EmergencyMemberViewModel>();
    }

    public sealed class EmergencyMemberViewModel
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string PersonTypeDisplay { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusDisplay { get; set; } = string.Empty;
    }

    public sealed class EmergencySessionSummaryViewModel
    {
        public int Id { get; set; }
        public string SiteName { get; set; } = string.Empty;
        public string StatusDisplay { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? EndedAtUtc { get; set; }
    }
}
