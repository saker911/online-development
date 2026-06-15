using System;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public class AuditLog
    {
        public int Id { get; set; }

        // The user who performed the action (if applicable)
        public string Username { get; set; } = string.Empty;

        // Action details
        public string ActionType { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;

        // Target entity
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;

        // Optional free-text message
        public string Message { get; set; } = string.Empty;

        // Source (service/controller)
        public string Source { get; set; } = string.Empty;

        // Who recorded this (may be same as Username)
        public string RecordedBy { get; set; } = string.Empty;

        public DateTime OccurredAt { get; set; }

        public string IpAddress { get; set; } = string.Empty;

        // Optional success flag
        public bool Success { get; set; }

        public string ActualActorUsername { get; set; } = string.Empty;
        public bool ActedUnderDelegation { get; set; }
        public string DelegatedFromUsername { get; set; } = string.Empty;
        public int? DelegationId { get; set; }

        // Before/After snapshots (JSON) for edits
        public string BeforeJson { get; set; } = string.Empty;
        public string AfterJson { get; set; } = string.Empty;
    }
}
