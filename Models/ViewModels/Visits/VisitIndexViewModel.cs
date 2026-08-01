namespace VehiclePermitSystemWeb.Models.ViewModels.Visits
{
    public class VisitIndexViewModel
    {
        public List<Visit> Visits { get; set; } = new();
        public HashSet<string> ApprovableVisitIds { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string SearchTerm { get; set; } = string.Empty;
        public string StatusFilter { get; set; } = "All";
        public bool SuspendedOnly { get; set; }
        public int PendingApprovalCount { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string PublicRequestUrl { get; set; } = string.Empty;
    }
}
