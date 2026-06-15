using Microsoft.EntityFrameworkCore;
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
using VehiclePermitSystemWeb.Security;

namespace VehiclePermitSystemWeb.Services.Management
{
    public static class ManagerTransitionService
    {
        public static bool IsSaudiMobileNumber(string? value)
        {
            return SaudiMobileNumberValidator.IsValidRequired(value);
        }

        public static string NormalizeManagerAssignmentType(string? value)
        {
            return string.Equals(
                value,
                DepartmentManagerTransitionTypes.Acting,
                StringComparison.OrdinalIgnoreCase
            )
                ? DepartmentManagerTransitionTypes.Acting
                : DepartmentManagerTransitionTypes.Permanent;
        }

        public static string NormalizeManagerExitAction(string? value)
        {
            return value switch
            {
                DepartmentManagerExitActions.Retirement => DepartmentManagerExitActions.Retirement,
                DepartmentManagerExitActions.Resignation =>
                    DepartmentManagerExitActions.Resignation,
                DepartmentManagerExitActions.EndAssignment =>
                    DepartmentManagerExitActions.EndAssignment,
                DepartmentManagerExitActions.ExternalTransfer =>
                    DepartmentManagerExitActions.ExternalTransfer,
                DepartmentManagerExitActions.InternalTransfer =>
                    DepartmentManagerExitActions.InternalTransfer,
                _ => DepartmentManagerExitActions.EndAssignment,
            };
        }

        public static string NormalizeHandoverRole(string? value, bool allowManager)
        {
            var normalizedValue = (value ?? string.Empty).Trim();
            if (
                allowManager
                && string.Equals(
                    normalizedValue,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return AppRoles.DepartmentManager;
            }

            return AppRoles.OrderedRoles.FirstOrDefault(role =>
                    !string.Equals(
                        role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !string.Equals(
                        role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(role, normalizedValue, StringComparison.OrdinalIgnoreCase)
                ) ?? AppRoles.Employee;
        }

        public static string GetDepartmentManagerJobTitle(string assignmentType)
        {
            return string.Equals(
                assignmentType,
                DepartmentManagerTransitionTypes.Acting,
                StringComparison.OrdinalIgnoreCase
            )
                ? "مدير قسم مكلف"
                : "مدير قسم";
        }

        public static string NormalizeGeneralManagerSelectionMode(string? value)
        {
            return string.Equals(
                value,
                GeneralManagerSelectionModes.CreateNew,
                StringComparison.OrdinalIgnoreCase
            )
                ? GeneralManagerSelectionModes.CreateNew
                : GeneralManagerSelectionModes.ExistingUser;
        }

        public static string NormalizeGeneralManagerAssignmentType(string? value)
        {
            return string.Equals(
                value,
                GeneralManagerAssignmentTypes.Acting,
                StringComparison.OrdinalIgnoreCase
            )
                ? GeneralManagerAssignmentTypes.Acting
                : GeneralManagerAssignmentTypes.Permanent;
        }

        public static string NormalizeGeneralManagerPreviousAction(string? value)
        {
            return value switch
            {
                GeneralManagerPreviousActions.InternalTransfer =>
                    GeneralManagerPreviousActions.InternalTransfer,
                GeneralManagerPreviousActions.ExternalTransfer =>
                    GeneralManagerPreviousActions.ExternalTransfer,
                GeneralManagerPreviousActions.Retirement =>
                    GeneralManagerPreviousActions.Retirement,
                GeneralManagerPreviousActions.Resignation =>
                    GeneralManagerPreviousActions.Resignation,
                GeneralManagerPreviousActions.Archive => GeneralManagerPreviousActions.Archive,
                GeneralManagerPreviousActions.ReturnToEmployee =>
                    GeneralManagerPreviousActions.ReturnToEmployee,
                _ => GeneralManagerPreviousActions.EndAssignment,
            };
        }

        public static string ResolveGeneralManagerJobTitle(
            string assignmentType,
            string? requestedJobTitle
        )
        {
            var normalizedRequestedJobTitle = (requestedJobTitle ?? string.Empty).Trim();
            if (
                string.Equals(
                    assignmentType,
                    GeneralManagerAssignmentTypes.Acting,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return string.IsNullOrWhiteSpace(normalizedRequestedJobTitle)
                    ? "مدير عام مكلف"
                    : normalizedRequestedJobTitle;
            }

            return string.IsNullOrWhiteSpace(normalizedRequestedJobTitle)
                ? "مدير عام"
                : normalizedRequestedJobTitle;
        }

        public static string GetGeneralManagerAssignmentTypeLabel(string assignmentType)
        {
            return string.Equals(
                assignmentType,
                GeneralManagerAssignmentTypes.Acting,
                StringComparison.OrdinalIgnoreCase
            )
                ? "تكليف"
                : "تثبيت";
        }

        public static string GetGeneralManagerPreviousActionLabel(string previousAction)
        {
            return previousAction switch
            {
                GeneralManagerPreviousActions.InternalTransfer => "النقل الداخلي",
                GeneralManagerPreviousActions.ExternalTransfer => "النقل الخارجي",
                GeneralManagerPreviousActions.Retirement => "التقاعد",
                GeneralManagerPreviousActions.Resignation => "الاستقالة",
                GeneralManagerPreviousActions.Archive => "الأرشفة",
                GeneralManagerPreviousActions.ReturnToEmployee => "العودة لوظيفة موظف",
                _ => "إنهاء التكليف",
            };
        }

        public static string ResolveDepartmentName(ApplicationDbContext db, int departmentId)
        {
            if (departmentId <= 0)
            {
                return string.Empty;
            }

            return db.Departments.FirstOrDefault(department => department.Id == departmentId)?.Name
                ?? string.Empty;
        }

        public static string GetManagerAssignmentTypeLabel(string assignmentType)
        {
            return string.Equals(
                assignmentType,
                DepartmentManagerTransitionTypes.Acting,
                StringComparison.OrdinalIgnoreCase
            )
                ? "تكليف"
                : "تثبيت";
        }

        public static string GetManagerExitActionLabel(string exitAction)
        {
            return exitAction switch
            {
                DepartmentManagerExitActions.Retirement => "التقاعد",
                DepartmentManagerExitActions.Resignation => "الاستقالة",
                DepartmentManagerExitActions.EndAssignment => "إنهاء التكليف",
                DepartmentManagerExitActions.ExternalTransfer => "النقل الخارجي",
                DepartmentManagerExitActions.InternalTransfer => "النقل الداخلي",
                _ => "إنهاء التكليف",
            };
        }

        public static string GetRoleJobTitle(string role)
        {
            return role switch
            {
                AppRoles.DepartmentManager => "مدير قسم",
                AppRoles.SystemAdmin => "مشرف نظام",
                AppRoles.GateSecurity => "أمن بوابة",
                AppRoles.Receptionist => "موظف استقبال",
                _ => "موظف",
            };
        }
    }
}
