using System.Collections.Generic;
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
using VehiclePermitSystemWeb.Security;

namespace VehiclePermitSystemWeb.Utilities.Administration
{
    public static class AdministrationCandidateOptionsBuilder
    {
        public static List<UserAccount> GetGeneralManagerCandidateOptions(
            IEnumerable<UserAccount> allUsers,
            string? currentGeneralManagerUsername
        )
        {
            var normalizedCurrentGeneralManagerUsername = (
                currentGeneralManagerUsername ?? string.Empty
            ).Trim();

            return allUsers
                .Where(user =>
                    !user.IsSuperAdmin
                    && !string.Equals(
                        user.Username,
                        normalizedCurrentGeneralManagerUsername,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .OrderByDescending(user => user.IsActive)
                .ThenBy(user => user.DisplayName)
                .ThenBy(user => user.Username)
                .ToList();
        }

        public static List<UserAccount> GetDepartmentManagerHandoverOptions(
            Department? selectedDepartment,
            IEnumerable<Department> departments,
            IEnumerable<UserAccount> allUsers
        )
        {
            var selectedDepartmentId = selectedDepartment?.Id ?? 0;
            var currentManagerUsername = (
                selectedDepartment?.ManagerUsername ?? string.Empty
            ).Trim();
            var managedUsers = departments
                .Where(department =>
                    department.Id != selectedDepartmentId
                    && !string.IsNullOrWhiteSpace(department.ManagerUsername)
                )
                .Select(department => department.ManagerUsername!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return allUsers
                .Where(user =>
                    user.IsActive
                    && !user.IsSuperAdmin
                    && !string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !string.Equals(
                        user.Username,
                        currentManagerUsername,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !managedUsers.Contains(user.Username)
                )
                .OrderBy(user => user.DisplayName)
                .ThenBy(user => user.Username)
                .ToList();
        }

        public static List<UserAccount> GetTransferReplacementOptions(
            Department? transferSourceDepartment,
            IEnumerable<Department> departments,
            IEnumerable<UserAccount> allUsers
        )
        {
            var currentManagerUsername = (
                transferSourceDepartment?.ManagerUsername ?? string.Empty
            ).Trim();
            var managedDepartmentIdsByUsername = departments
                .Where(department => !string.IsNullOrWhiteSpace(department.ManagerUsername))
                .GroupBy(
                    department => department.ManagerUsername!,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(department => department.Id).ToList(),
                    StringComparer.OrdinalIgnoreCase
                );

            return allUsers
                .Where(user =>
                    user.IsActive
                    && string.Equals(
                        user.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !string.Equals(
                        user.Username,
                        currentManagerUsername,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && (
                        !managedDepartmentIdsByUsername.TryGetValue(
                            user.Username,
                            out var managedDepartmentIds
                        )
                        || managedDepartmentIds.Count == 0
                    )
                )
                .OrderBy(user => user.DisplayName)
                .ToList();
        }

        public static List<UserAccount> GetDepartmentManagerOptions(
            IEnumerable<UserAccount> allUsers,
            Department? department = null
        )
        {
            var options = allUsers
                .Where(user =>
                    string.Equals(
                        user.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .OrderBy(user => user.DisplayName)
                .ToList();

            if (!string.IsNullOrWhiteSpace(department?.ManagerUsername))
            {
                var currentManager = allUsers.FirstOrDefault(user =>
                    string.Equals(
                        user.Username,
                        department.ManagerUsername,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (
                    currentManager != null
                    && !options.Any(user =>
                        string.Equals(
                            user.Username,
                            currentManager.Username,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                {
                    options.Insert(0, currentManager);
                }
            }

            return options;
        }
    }
}
