namespace VehiclePermitSystemWeb.Models.Entities
{
    public class DisplayDevice : ITenantScopedEntity
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string ScreenName { get; set; } = string.Empty;
        public string ScreenLocation { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Mode { get; set; } = DisplayDeviceModes.Gate;
        public string Status { get; set; } = DisplayDeviceStatuses.Pending;
        public string DeviceTokenHash { get; set; } = string.Empty;
        public string RequestCode { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string LastIpAddress { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
        public string ApprovedByUserId { get; set; } = string.Empty;
        public DateTime? LastSeenUtc { get; set; }
        public DateTime? DisabledAtUtc { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public static class DisplayDeviceModes
    {
        public const string Gate = "Gate";
        public const string WaitingBoard = "WaitingBoard";

        public static IReadOnlyList<string> Supported { get; } = new[] { Gate, WaitingBoard };

        public static string Normalize(string? mode) =>
            Supported.Contains(mode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                ? Supported.First(item => string.Equals(item, mode, StringComparison.OrdinalIgnoreCase))
                : Gate;

        public static string GetDisplayName(string? mode) => Normalize(mode) switch
        {
            WaitingBoard => "لوحة الانتظار",
            _ => "مركز البوابة",
        };
    }

    public static class DisplayDeviceStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Disabled = "Disabled";
    }
}
