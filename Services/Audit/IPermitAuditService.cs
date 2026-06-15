using VehiclePermitSystemWeb.Data;
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

namespace VehiclePermitSystemWeb.Services.Audit
{
    public interface IPermitAuditService
    {
        void RecordPermitActivity(
            ApplicationDbContext db,
            Permit permit,
            string actionType,
            string actionLabel,
            string message,
            string source,
            string? recordedBy,
            DateTime occurredAt,
            string? reasonCode = null,
            int? lateMinutes = null,
            PermitScanAuditContext? auditContext = null
        );

        void RecordUserActivity(
            ApplicationDbContext db,
            string username,
            string displayName,
            string actionType,
            string actionLabel,
            string message,
            string source,
            string? recordedBy,
            DateTime occurredAt,
            string? actualActorUsername = null,
            bool actedUnderDelegation = false,
            string? delegatedFromUsername = null,
            int? delegationId = null
        );

        void NotifyPermitManagerOfActivity(
            ApplicationDbContext db,
            Permit permit,
            string? performedBy,
            string source,
            DateTime occurredAt,
            string message,
            string actionType,
            string actionLabel
        );
    }
}
