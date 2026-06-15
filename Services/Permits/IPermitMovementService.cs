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

namespace VehiclePermitSystemWeb.Services.Permits
{
    public interface IPermitMovementService
    {
        (bool allowed, string reason) RecordPermitScan(
            string permitNumber,
            string? recordedBy = null,
            bool overrideEntry = false,
            PermitScanAuditContext? auditContext = null
        );

        bool ResolveUnauthorizedExitReview(
            string sequenceId,
            bool confirmViolation,
            string? recordedBy = null
        );

        bool MarkPermitDeparted(
            string permitNumber,
            string? recordedBy = null,
            PermitScanAuditContext? auditContext = null
        );

        bool MarkPermitReturned(
            string permitNumber,
            string? recordedBy = null,
            PermitScanAuditContext? auditContext = null
        );
    }
}
