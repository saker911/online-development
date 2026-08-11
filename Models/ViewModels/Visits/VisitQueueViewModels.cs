namespace VehiclePermitSystemWeb.Models.ViewModels.Visits
{
    public sealed class VisitQueueViewModel
    {
        public IReadOnlyList<VisitQueueItemViewModel> Waiting { get; set; } = [];
        public IReadOnlyList<VisitQueueItemViewModel> Called { get; set; } = [];
        public IReadOnlyList<VisitQueueItemViewModel> Serving { get; set; } = [];
        public IReadOnlyList<VisitQueueItemViewModel> Recent { get; set; } = [];
        public IReadOnlyList<VisitQueueItemViewModel> Available { get; set; } = [];
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class VisitQueueItemViewModel
    {
        public string VisitId { get; set; } = string.Empty;
        public string TicketNumber { get; set; } = string.Empty;
        public string VisitorName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusDisplay { get; set; } = string.Empty;
        public DateTime VisitDate { get; set; }
        public DateTime? QueuedAtUtc { get; set; }
        public DateTime? CalledAtUtc { get; set; }
        public DateTime? ServiceStartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }
}
