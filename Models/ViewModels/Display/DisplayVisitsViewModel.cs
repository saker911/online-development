namespace VehiclePermitSystemWeb.Models.ViewModels.Display
{
    public class DisplayVisitsViewModel
    {
        public List<Visit> VisitorsAwaitingArrival { get; set; } = new();
        public List<Visit> VisitorsInside { get; set; } = new();
        public List<Visit> VisitorsCompleted { get; set; } = new();
    }
}
