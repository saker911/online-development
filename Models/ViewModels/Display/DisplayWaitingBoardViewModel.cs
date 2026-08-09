namespace VehiclePermitSystemWeb.Models.ViewModels.Display
{
    public sealed class DisplayWaitingBoardViewModel
    {
        public IReadOnlyList<DisplayWaitingTicketViewModel> Waiting { get; set; } =
            Array.Empty<DisplayWaitingTicketViewModel>();
        public IReadOnlyList<DisplayWaitingTicketViewModel> Inside { get; set; } =
            Array.Empty<DisplayWaitingTicketViewModel>();
        public IReadOnlyList<DisplayWaitingTicketViewModel> Completed { get; set; } =
            Array.Empty<DisplayWaitingTicketViewModel>();
        public string UpdatedAtText { get; set; } = string.Empty;
    }

    public sealed class DisplayWaitingTicketViewModel
    {
        public string TicketNumber { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string TimeText { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
    }
}
