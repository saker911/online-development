namespace VehiclePermitSystemWeb.Models.ViewModels.Display
{
    public class DisplayGateViewModel
    {
        public DisplayOperatorSessionInfo? ActiveOperator { get; set; }
        public List<Permit> ApprovedEmployees { get; set; } = new();
        public List<Visit> VisitorsAwaitingArrival { get; set; } = new();
        public List<Permit> EmployeesOut { get; set; } = new();
        public List<Visit> VisitorsInside { get; set; } = new();
        public List<PermitActivity> RecentPermitActivities { get; set; } = new();
        public PermitActivity? LatestPermitActivity { get; set; }
    }
}
