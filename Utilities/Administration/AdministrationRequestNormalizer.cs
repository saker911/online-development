using System.Linq;
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

namespace VehiclePermitSystemWeb.Utilities.Administration
{
    public static class AdministrationRequestNormalizer
    {
        public static void NormalizeDepartment(Department department)
        {
            department.Name = (department.Name ?? string.Empty).Trim();
            department.ManagerUsername = (department.ManagerUsername ?? string.Empty).Trim();
            department.ManagerDisplayName = (department.ManagerDisplayName ?? string.Empty).Trim();
        }

        public static void NormalizeDepartmentManagerHandover(
            DepartmentManagerHandoverRequest handover
        )
        {
            handover.AssignmentType = (handover.AssignmentType ?? string.Empty).Trim();
            handover.ExistingManagerUsername = (
                handover.ExistingManagerUsername ?? string.Empty
            ).Trim();
            handover.NewManagerUsername = (handover.NewManagerUsername ?? string.Empty).Trim();
            handover.NewManagerFullName = (handover.NewManagerFullName ?? string.Empty).Trim();
            handover.NewManagerPhoneNumber = new string(
                (handover.NewManagerPhoneNumber ?? string.Empty).Where(char.IsDigit).ToArray()
            );
            handover.ExitAction = (handover.ExitAction ?? string.Empty).Trim();
            handover.PreviousManagerNewRole = (
                handover.PreviousManagerNewRole ?? string.Empty
            ).Trim();
        }
    }
}
