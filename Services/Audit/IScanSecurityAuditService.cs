using VehiclePermitSystemWeb.Models.DTOs;

namespace VehiclePermitSystemWeb.Services.Audit
{
    public interface IScanSecurityAuditService
    {
        string RecordScanResult(
            string entityType,
            string entityId,
            bool allowed,
            string reason,
            PermitScanAuditContext auditContext,
            string? recordedBy = null
        );
    }
}
