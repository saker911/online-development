namespace VehiclePermitSystemWeb.Services.Audit
{
    public interface IAuditLogService
    {
        void Record(
            string username,
            string actionType,
            string actionLabel,
            string entityType,
            string entityId,
            string message,
            string source,
            bool success = true,
            string? ipAddress = null,
            string? actualActorUsername = null,
            bool actedUnderDelegation = false,
            string? delegatedFromUsername = null,
            int? delegationId = null,
            string? beforeJson = null,
            string? afterJson = null
        );
    }
}
