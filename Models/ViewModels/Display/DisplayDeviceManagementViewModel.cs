namespace VehiclePermitSystemWeb.Models.ViewModels.Display
{
    public class DisplayDeviceManagementViewModel
    {
        public IReadOnlyList<DisplayDeviceListItemViewModel> Devices { get; set; } =
            Array.Empty<DisplayDeviceListItemViewModel>();

        public int ApprovedCount { get; set; }
        public int OnlineCount { get; set; }
        public int OfflineCount { get; set; }
        public int PendingCount { get; set; }

        public IEnumerable<DisplayDeviceListItemViewModel> PendingDevices =>
            Devices.Where(item => item.Status == DisplayDeviceStatuses.Pending);

        public IEnumerable<DisplayDeviceListItemViewModel> ApprovedDevices =>
            Devices.Where(item => item.Status == DisplayDeviceStatuses.Approved);

        public IEnumerable<DisplayDeviceListItemViewModel> RejectedDevices =>
            Devices.Where(item => item.Status == DisplayDeviceStatuses.Rejected);

        public IEnumerable<DisplayDeviceListItemViewModel> DisabledDevices =>
            Devices.Where(item => item.Status == DisplayDeviceStatuses.Disabled);
    }

    public class DisplayDeviceListItemViewModel
    {
        public int Id { get; set; }
        public string ScreenName { get; set; } = string.Empty;
        public string ScreenLocation { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Mode { get; set; } = DisplayDeviceModes.Gate;
        public string ModeText { get; set; } = DisplayDeviceModes.GetDisplayName(DisplayDeviceModes.Gate);
        public string Status { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string LastIpAddress { get; set; } = string.Empty;
        public string UserAgentSummary { get; set; } = string.Empty;
        public string RequestCode { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? LastSeenUtc { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
        public string ApprovedByUserId { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string SiteName { get; set; } = string.Empty;
        public string EntranceName { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string NetworkStatus { get; set; } = string.Empty;
        public string CameraStatus { get; set; } = string.Empty;
        public int? BatteryLevel { get; set; }
        public string LastHealthError { get; set; } = string.Empty;
        public DateTime? LastHealthReportedAtUtc { get; set; }
        public int ConfigurationVersion { get; set; }
        public int AppliedConfigurationVersion { get; set; }
        public bool ConfigurationPending => AppliedConfigurationVersion < ConfigurationVersion;
    }

    public sealed class DisplayDeviceHeartbeatViewModel
    {
        public string AppVersion { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string NetworkStatus { get; set; } = string.Empty;
        public string CameraStatus { get; set; } = string.Empty;
        public int? BatteryLevel { get; set; }
        public string LastError { get; set; } = string.Empty;
        public int AppliedConfigurationVersion { get; set; }
    }

    public sealed class DisplayHeartbeatResultViewModel
    {
        public bool Success { get; set; }
        public int ConfigurationVersion { get; set; }
        public bool ReloadRequired { get; set; }
        public string Mode { get; set; } = DisplayDeviceModes.Gate;
    }
}
