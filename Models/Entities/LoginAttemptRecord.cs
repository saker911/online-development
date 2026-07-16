namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class LoginAttemptRecord
    {
        public string KeyHash { get; set; } = string.Empty;
        public int FailureCount { get; set; }
        public DateTime WindowStartedAtUtc { get; set; }
        public DateTime? BlockedUntilUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
