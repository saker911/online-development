using Microsoft.AspNetCore.Http;
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
    public static class GeneralManagerAssignmentMapper
    {
        public static void ApplyFormValues(
            HttpRequest request,
            GeneralManagerAssignmentRequest assignment
        )
        {
            if (!request.HasFormContentType)
            {
                return;
            }

            var form = request.Form;
            assignment.WizardStep = FormValueReader.ReadInt(
                form,
                "GeneralManagerAssignment.WizardStep",
                assignment.WizardStep
            );
            assignment.SelectionMode = FormValueReader.ReadString(
                form,
                "GeneralManagerAssignment.SelectionMode",
                assignment.SelectionMode
            );
            assignment.ExistingUserUsername = FormValueReader.ReadString(
                form,
                "GeneralManagerAssignment.ExistingUserUsername",
                assignment.ExistingUserUsername
            );
            assignment.NewUserUsername = FormValueReader.ReadString(
                form,
                "GeneralManagerAssignment.NewUserUsername",
                assignment.NewUserUsername
            );
            assignment.NewUserFullName = FormValueReader.ReadString(
                form,
                "GeneralManagerAssignment.NewUserFullName",
                assignment.NewUserFullName
            );
            assignment.NewUserPhoneNumber = FormValueReader.ReadString(
                form,
                "GeneralManagerAssignment.NewUserPhoneNumber",
                assignment.NewUserPhoneNumber
            );
            assignment.NewUserPassword = FormValueReader.ReadString(
                form,
                "GeneralManagerAssignment.NewUserPassword",
                assignment.NewUserPassword
            );
            assignment.NewUserConfirmPassword = FormValueReader.ReadString(
                form,
                "GeneralManagerAssignment.NewUserConfirmPassword",
                assignment.NewUserConfirmPassword
            );
            assignment.NewUserJobTitle = FormValueReader.ReadString(
                form,
                "GeneralManagerAssignment.NewUserJobTitle",
                assignment.NewUserJobTitle
            );
            assignment.NewUserDepartmentId = FormValueReader.ReadInt(
                form,
                "GeneralManagerAssignment.NewUserDepartmentId",
                assignment.NewUserDepartmentId
            );
            assignment.NewUserIsActive = FormValueReader.ReadBool(
                form,
                "GeneralManagerAssignment.NewUserIsActive",
                assignment.NewUserIsActive
            );
            assignment.PreviousGeneralManagerAction = FormValueReader.ReadString(
                form,
                "GeneralManagerAssignment.PreviousGeneralManagerAction",
                assignment.PreviousGeneralManagerAction
            );
            assignment.PreviousGeneralManagerTargetDepartmentId = FormValueReader.ReadInt(
                form,
                "GeneralManagerAssignment.PreviousGeneralManagerTargetDepartmentId",
                assignment.PreviousGeneralManagerTargetDepartmentId
            );
            assignment.AssignmentType = FormValueReader.ReadString(
                form,
                "GeneralManagerAssignment.AssignmentType",
                assignment.AssignmentType
            );
        }

        public static void Normalize(GeneralManagerAssignmentRequest assignment)
        {
            assignment.SelectionMode = (assignment.SelectionMode ?? string.Empty).Trim();
            assignment.ExistingUserUsername = (
                assignment.ExistingUserUsername ?? string.Empty
            ).Trim();
            assignment.NewUserUsername = (assignment.NewUserUsername ?? string.Empty).Trim();
            assignment.NewUserFullName = (assignment.NewUserFullName ?? string.Empty).Trim();
            assignment.NewUserPhoneNumber = new string(
                (assignment.NewUserPhoneNumber ?? string.Empty).Where(char.IsDigit).ToArray()
            );
            assignment.NewUserPassword = assignment.NewUserPassword ?? string.Empty;
            assignment.NewUserConfirmPassword = assignment.NewUserConfirmPassword ?? string.Empty;
            assignment.NewUserJobTitle = (assignment.NewUserJobTitle ?? string.Empty).Trim();
            assignment.PreviousGeneralManagerAction = (
                assignment.PreviousGeneralManagerAction ?? string.Empty
            ).Trim();
            assignment.AssignmentType = (assignment.AssignmentType ?? string.Empty).Trim();
        }

        public static string NormalizePreviousActionValue(string? value)
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
    }
}
