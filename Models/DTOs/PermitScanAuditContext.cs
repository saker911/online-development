namespace VehiclePermitSystemWeb.Models.DTOs
{
    public sealed class PermitScanAuditContext
    {
        public string SequenceId { get; set; } = string.Empty;
        public string ClassificationStatus { get; set; } = string.Empty;
        public string GateName { get; set; } = string.Empty;
        public string GateOperatorName { get; set; } = string.Empty;
        public string GateOperatorAccount { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string ExecutionMethod { get; set; } = string.Empty;
        public string ManualOverrideReason { get; set; } = string.Empty;
        public bool IsAutomated { get; set; }

        public static PermitScanAuditContext CreateSystemContext(
            string gateName,
            string? account = null
        )
        {
            return new PermitScanAuditContext
            {
                ClassificationStatus = "System",
                GateName = gateName,
                GateOperatorName = string.IsNullOrWhiteSpace(account) ? "النظام" : account,
                GateOperatorAccount = account ?? string.Empty,
                ExecutionMethod = "system",
                IsAutomated = true,
            };
        }
    }
}
