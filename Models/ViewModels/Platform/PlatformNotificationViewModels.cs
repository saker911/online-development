namespace VehiclePermitSystemWeb.Models.ViewModels.Platform
{
    public sealed class PlatformNotificationCenterViewModel
    {
        public List<PlatformNotificationItemViewModel> Items { get; set; } = new();
    }

    public sealed class PlatformNotificationItemViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Tone { get; set; } = "info";
        public string ActionUrl { get; set; } = string.Empty;
        public DateTime OccurredAtUtc { get; set; }
    }
}
