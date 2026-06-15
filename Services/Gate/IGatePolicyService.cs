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

namespace VehiclePermitSystemWeb.Services.Gate
{
    public interface IGatePolicyService
    {
        GateDecision ResolveDecision(Permit permit, DateTime now);

        bool CanEnter(Permit permit, DateTime now);

        bool CanExit(Permit permit, DateTime now);

        bool ShouldWaitForWorkEndClosure(Permit permit, DateTime now);

        bool IsBlockedByPendingUnauthorizedExit(Permit permit, DateTime now);
    }
}
