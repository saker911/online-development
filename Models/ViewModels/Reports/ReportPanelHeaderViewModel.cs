namespace VehiclePermitSystemWeb.Models.ViewModels.Reports
{
    public sealed class ReportPanelHeaderViewModel
    {
        public string Title { get; init; } = string.Empty;

        public string Subtitle { get; init; } = string.Empty;

        public string? Badge { get; init; }
    }
}
