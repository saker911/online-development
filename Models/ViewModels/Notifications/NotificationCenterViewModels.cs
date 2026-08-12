using System.ComponentModel.DataAnnotations;
using VehiclePermitSystemWeb.Models.Entities;

namespace VehiclePermitSystemWeb.Models.ViewModels.Notifications
{
    public sealed class NotificationCenterPageViewModel
    {
        public List<NotificationRowViewModel> Items { get; set; } = new();
        public int UnreadCount { get; set; }
        public int TotalCount { get; set; }
        public string Category { get; set; } = string.Empty;
        public string State { get; set; } = "all";
        public bool CanManageSettings { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public sealed class NotificationRowViewModel
    {
        public long Id { get; set; }
        public string Category { get; set; } = NotificationCategories.System;
        public string Severity { get; set; } = NotificationSeverities.Info;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ActionUrl { get; set; } = string.Empty;
        public DateTime OccurredAtUtc { get; set; }
        public bool IsRead { get; set; }
    }

    public sealed class NotificationSettingsViewModel
    {
        public bool NotificationCenterEnabled { get; set; } = true;
        public bool PermitNotificationsEnabled { get; set; } = true;
        public bool VisitNotificationsEnabled { get; set; } = true;
        public bool SecurityAlertsEnabled { get; set; } = true;
        public bool FailedOperationAlertsEnabled { get; set; } = true;
        public bool UnauthorizedMovementAlertsEnabled { get; set; } = true;

        [Range(7, 730, ErrorMessage = "مدة الاحتفاظ بالإشعارات من 7 إلى 730 يومًا.")]
        public int NotificationRetentionDays { get; set; } = 90;

        [Range(7, 365, ErrorMessage = "مدة الاحتفاظ بسجل البريد من 7 إلى 365 يومًا.")]
        public int EmailOutboxRetentionDays { get; set; } = 30;

        [Range(90, 2555, ErrorMessage = "مدة الاحتفاظ بسجل التدقيق من 90 يومًا إلى 7 سنوات.")]
        public int AuditLogRetentionDays { get; set; } = 365;

        public DateTime? LastRetentionRunAtUtc { get; set; }
    }
}
