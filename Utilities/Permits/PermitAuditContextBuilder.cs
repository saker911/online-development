using VehiclePermitSystemWeb.Models.DTOs;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Account;
using VehiclePermitSystemWeb.Models.ViewModels.Backup;
using VehiclePermitSystemWeb.Models.ViewModels.Delegations;
using VehiclePermitSystemWeb.Models.ViewModels.Departments;
using VehiclePermitSystemWeb.Models.ViewModels.Display;
using VehiclePermitSystemWeb.Models.ViewModels.Permits;
using VehiclePermitSystemWeb.Models.ViewModels.Reports;
using VehiclePermitSystemWeb.Models.ViewModels.Scan;
using VehiclePermitSystemWeb.Models.ViewModels.Users;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;

namespace VehiclePermitSystemWeb.Utilities.Permits
{
    public static class PermitAuditContextBuilder
    {
        public static PermitScanAuditContext BuildAuditContext(
            PermitScanAuditContext? auditContext,
            string source,
            string? sequenceId,
            string classificationStatus
        )
        {
            return new PermitScanAuditContext
            {
                SequenceId = sequenceId ?? auditContext?.SequenceId ?? string.Empty,
                ClassificationStatus = classificationStatus,
                GateName = string.IsNullOrWhiteSpace(auditContext?.GateName)
                    ? source
                    : auditContext.GateName,
                GateOperatorName = auditContext?.GateOperatorName ?? string.Empty,
                GateOperatorAccount = auditContext?.GateOperatorAccount ?? string.Empty,
                DeviceId = auditContext?.DeviceId ?? string.Empty,
                IpAddress = auditContext?.IpAddress ?? string.Empty,
                ExecutionMethod = auditContext?.ExecutionMethod ?? string.Empty,
                IsAutomated = auditContext?.IsAutomated ?? false,
            };
        }
    }
}
