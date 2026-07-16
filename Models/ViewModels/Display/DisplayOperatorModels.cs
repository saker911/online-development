namespace VehiclePermitSystemWeb.Models.ViewModels.Display
{
    public sealed class DisplayOperatorSessionInfo
    {
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string BadgeCode { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public DateTime SignedInAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public bool MustChangePin { get; set; }
    }

    public sealed class DisplayOperatorSwitchResult
    {
        public bool Success { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool RequiresPinChange { get; set; }
        public DisplayOperatorSessionInfo? PreviousOperator { get; set; }
        public DisplayOperatorSessionInfo? CurrentOperator { get; set; }
    }

    public sealed class DisplayOperatorSignInRequest
    {
        public string BadgeCode { get; set; } = string.Empty;
        public string Pin { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
    }

    public sealed class DisplayOperatorDeviceRequest
    {
        public string DeviceId { get; set; } = string.Empty;
    }

    public sealed class DisplayOperatorPinChangeRequest
    {
        public string DeviceId { get; set; } = string.Empty;
        public string CurrentPin { get; set; } = string.Empty;
        public string NewPin { get; set; } = string.Empty;
    }

    public sealed class DisplayOperatorNoteRequest
    {
        public string DeviceId { get; set; } = string.Empty;
        public string PermitNumber { get; set; } = string.Empty;
        public string NoteText { get; set; } = string.Empty;
    }
}
