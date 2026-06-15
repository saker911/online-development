namespace VehiclePermitSystemWeb.Models.ViewModels.Permits
{
    public class PermitIndexViewModel
    {
        public List<Permit> Permits { get; set; } = new();
        public HashSet<string> ApprovablePermitNumbers { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string SearchTerm { get; set; } = string.Empty;
        public string SearchBy { get; set; } = "Name";
        public int PendingApprovalCount { get; set; }
        public int StoppedPermitsCount { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; }
        public int PageSize { get; set; } = 10;
    }
}
