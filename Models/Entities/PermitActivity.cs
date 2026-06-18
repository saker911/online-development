namespace VehiclePermitSystemWeb.Models.Entities
{
    public class PermitActivity : ITenantScopedEntity
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string PermitNumber { get; set; } = string.Empty;
        public Permit? Permit { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public string NationalId { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string RecordedBy { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public string SequenceId { get; set; } = string.Empty;
        public string ClassificationStatus { get; set; } = string.Empty;
        public string GateName { get; set; } = string.Empty;
        public string GateOperatorName { get; set; } = string.Empty;
        public string GateOperatorAccount { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string ExecutionMethod { get; set; } = string.Empty;
        public bool IsAutomated { get; set; }
        public DateTime OccurredAt { get; set; }
        public int? LateMinutes { get; set; }
    }
}
