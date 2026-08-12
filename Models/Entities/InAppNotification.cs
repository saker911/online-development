using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class InAppNotification : ITenantScopedEntity
    {
        public long Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;

        [Required, StringLength(64)]
        public string RecipientUsername { get; set; } = string.Empty;

        [Required, StringLength(32)]
        public string Category { get; set; } = NotificationCategories.System;

        [Required, StringLength(16)]
        public string Severity { get; set; } = NotificationSeverities.Info;

        [Required, StringLength(160)]
        public string Title { get; set; } = string.Empty;

        [StringLength(500)]
        public string Message { get; set; } = string.Empty;

        [StringLength(512)]
        public string ActionUrl { get; set; } = string.Empty;

        [Required, StringLength(256)]
        public string SourceKey { get; set; } = string.Empty;

        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAtUtc { get; set; }
        public DateTime? DismissedAtUtc { get; set; }
    }

    public static class NotificationCategories
    {
        public const string Permit = "Permit";
        public const string Visit = "Visit";
        public const string Security = "Security";
        public const string System = "System";
    }

    public static class NotificationSeverities
    {
        public const string Info = "Info";
        public const string Success = "Success";
        public const string Warning = "Warning";
        public const string Critical = "Critical";
    }
}
