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

namespace VehiclePermitSystemWeb.Services.Administration
{
    public interface IAccessControlService
    {
        bool CanAccessPermit(Permit permit, UserAccount? user);
        bool CanApprovePermit(Permit permit, UserAccount? user);
        bool CanAccessVisit(Visit visit, UserAccount? user);
        bool CanApproveVisit(Visit visit, UserAccount? user);
        bool IsCurrentDepartmentManager(UserAccount? user, string? departmentName = null);
        bool CanViewPermits(UserAccount? user);
        bool CanViewVisits(UserAccount? user);
        IEnumerable<Permit> FilterPermitsForUser(IEnumerable<Permit> permits, UserAccount? user);
        IEnumerable<Visit> FilterVisitsForUser(IEnumerable<Visit> visits, UserAccount? user);
    }
}
