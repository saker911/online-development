namespace VehiclePermitSystemWeb.Models.ViewModels.Reports
{
    public class UserActivityReportViewModel
    {
        public List<UserActivity> Activities { get; set; } = new();
        public List<string> AvailableUsers { get; set; } = new();
        public List<string> AvailableActionTypes { get; set; } = new();
        public string? Query { get; set; }
        public string? ActionType { get; set; }
        public string? Username { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int TotalCount { get; set; }
    }
}
