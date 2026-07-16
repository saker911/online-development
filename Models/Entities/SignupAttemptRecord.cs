namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class SignupAttemptRecord
    {
        public string KeyHash { get; set; } = string.Empty;
        public int SuccessCount { get; set; }
        public DateTime WindowStartedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
