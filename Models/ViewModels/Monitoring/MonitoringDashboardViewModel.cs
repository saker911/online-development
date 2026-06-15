namespace VehiclePermitSystemWeb.Models.ViewModels.Monitoring
{
    public sealed class MonitoringDashboardViewModel
    {
        public string Range { get; set; } = "today";
        public string RangeLabel { get; set; } = "اليوم";
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int EntryCount { get; set; }
        public int ExitCount { get; set; }
        public int DeniedCount { get; set; }
        public IReadOnlyList<MonitoringActivityItem> RecentActivities { get; set; } =
            Array.Empty<MonitoringActivityItem>();
        public IReadOnlyList<MonitoringSummaryItem> TopOperators { get; set; } =
            Array.Empty<MonitoringSummaryItem>();
        public IReadOnlyList<MonitoringSummaryItem> TopRejectionReasons { get; set; } =
            Array.Empty<MonitoringSummaryItem>();
        public IReadOnlyList<MonitoringSummaryItem> RecentSystemErrors { get; set; } =
            Array.Empty<MonitoringSummaryItem>();
    }

    public sealed class MonitoringActivityItem
    {
        public string Key { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string Actor { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; }
        public bool Success { get; set; }
    }

    public sealed class MonitoringSummaryItem
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTime? OccurredAt { get; set; }
        public bool Success { get; set; }
    }
}
