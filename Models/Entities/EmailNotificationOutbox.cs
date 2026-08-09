namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class EmailNotificationOutbox : ITenantScopedEntity
    {
        public const string StatusPending = "Pending";
        public const string StatusProcessing = "Processing";
        public const string StatusSent = "Sent";
        public const string StatusFailed = "Failed";

        public long Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string NotificationType { get; set; } = string.Empty;
        public string ReferenceType { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty;
        public string DeduplicationKey { get; set; } = string.Empty;
        public string RecipientEmail { get; set; } = string.Empty;
        public string RecipientName { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string HtmlBody { get; set; } = string.Empty;
        public string TextBody { get; set; } = string.Empty;
        public string Status { get; set; } = StatusPending;
        public int AttemptCount { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime NextAttemptAtUtc { get; set; }
        public DateTime? LastAttemptAtUtc { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public string LockToken { get; set; } = string.Empty;
        public DateTime? LockedAtUtc { get; set; }
        public string LastError { get; set; } = string.Empty;
    }
}
