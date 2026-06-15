namespace VehiclePermitSystemWeb.Models.ViewModels.Delegations
{
    public sealed class DelegationEditViewModel
    {
        public int Id { get; set; }
        public bool IsEditMode { get; set; }
        public string DelegatorUsername { get; set; } = string.Empty;
        public string DelegateeUsername { get; set; } = string.Empty;
        public string ScopeType { get; set; } = DelegationScopeTypes.Custom;
        public List<string> SelectedPermissions { get; set; } = new();
        public DateTime StartAt { get; set; } = DateTime.Today.AddHours(8);
        public DateTime EndAt { get; set; } = DateTime.Today.AddDays(1).AddHours(16);
        public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
        public string Notes { get; set; } = string.Empty;
    }

    public sealed class DelegationManagementViewModel
    {
        public DelegationEditViewModel Editor { get; set; } = new();
        public List<Delegation> Delegations { get; set; } = new();
        public List<UserAccount> AvailableUsers { get; set; } = new();
        public Dictionary<string, List<string>> DelegatorPermissionKeys { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> UserDisplayNames { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> StatusCounts { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string StatusFilter { get; set; } = string.Empty;
        public string DelegatorFilter { get; set; } = string.Empty;
        public string DelegateeFilter { get; set; } = string.Empty;
    }

    public sealed class DelegatedActionReportViewModel
    {
        public List<AuditLog> Entries { get; set; } = new();
        public List<UserAccount> AvailableUsers { get; set; } = new();
        public Dictionary<string, string> UserDisplayNames { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string ActualActorUsername { get; set; } = string.Empty;
        public string DelegatedFromUsername { get; set; } = string.Empty;
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }
}
