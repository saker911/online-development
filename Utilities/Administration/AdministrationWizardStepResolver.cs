using Microsoft.AspNetCore.Mvc.ModelBinding;
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
    public static class AdministrationWizardStepResolver
    {
        public static int ResolveManagerHandoverWizardStep(
            ModelStateDictionary modelState,
            int requestedStep
        )
        {
            var keysWithErrors = modelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .Select(entry => entry.Key ?? string.Empty)
                .ToList();

            if (
                keysWithErrors.Any(key =>
                    key.EndsWith(
                        nameof(DepartmentManagerHandoverRequest.PreviousManagerTargetDepartmentId),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(DepartmentManagerHandoverRequest.PreviousManagerNewRole),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(DepartmentManagerHandoverRequest.ExitAction),
                        StringComparison.Ordinal
                    )
                )
            )
            {
                return 3;
            }

            if (
                keysWithErrors.Any(key =>
                    key.EndsWith(
                        nameof(DepartmentManagerHandoverRequest.ExistingManagerUsername),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(DepartmentManagerHandoverRequest.NewManagerUsername),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(DepartmentManagerHandoverRequest.NewManagerFullName),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(DepartmentManagerHandoverRequest.NewManagerPhoneNumber),
                        StringComparison.Ordinal
                    )
                )
            )
            {
                return 2;
            }

            if (requestedStep >= 1 && requestedStep <= 3)
            {
                return requestedStep;
            }

            return 1;
        }

        public static int ResolveGeneralManagerWizardStep(
            ModelStateDictionary modelState,
            int requestedStep
        )
        {
            var keysWithErrors = modelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .Select(entry => entry.Key ?? string.Empty)
                .ToList();

            if (
                keysWithErrors.Any(key =>
                    key.EndsWith(
                        nameof(GeneralManagerAssignmentRequest.AssignmentType),
                        StringComparison.Ordinal
                    )
                )
            )
            {
                return 4;
            }

            if (
                keysWithErrors.Any(key =>
                    key.EndsWith(
                        nameof(GeneralManagerAssignmentRequest.PreviousGeneralManagerAction),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(
                            GeneralManagerAssignmentRequest.PreviousGeneralManagerTargetDepartmentId
                        ),
                        StringComparison.Ordinal
                    )
                )
            )
            {
                return 3;
            }

            if (
                keysWithErrors.Any(key =>
                    key.EndsWith(
                        nameof(GeneralManagerAssignmentRequest.ExistingUserUsername),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(GeneralManagerAssignmentRequest.NewUserUsername),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(GeneralManagerAssignmentRequest.NewUserFullName),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(GeneralManagerAssignmentRequest.NewUserPhoneNumber),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(GeneralManagerAssignmentRequest.NewUserPassword),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(GeneralManagerAssignmentRequest.NewUserConfirmPassword),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(GeneralManagerAssignmentRequest.NewUserJobTitle),
                        StringComparison.Ordinal
                    )
                    || key.EndsWith(
                        nameof(GeneralManagerAssignmentRequest.NewUserDepartmentId),
                        StringComparison.Ordinal
                    )
                )
            )
            {
                return 2;
            }

            if (requestedStep >= 1 && requestedStep <= 5)
            {
                return requestedStep;
            }

            return 1;
        }
    }
}
