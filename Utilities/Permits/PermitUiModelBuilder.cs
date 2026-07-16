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
    public static class PermitUiModelBuilder
    {
        public static PermitExitRequestViewModel BuildExitRequestViewModel(Permit permit)
        {
            return new PermitExitRequestViewModel
            {
                PermitNumber = permit.PermitNumber,
                DriverName = permit.DriverName,
                NationalId = permit.NationalId,
                PermitTypeDisplay = permit.PermitTypeDisplay,
                CurrentStateDisplay = string.Equals(
                    permit.CurrentState,
                    "Inside",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "داخل"
                    : "خارج",
                ApprovalStatusDisplay = permit.ApprovalStatusDisplay,
                VehicleType = permit.VehicleType,
                PlateNumberDisplay = permit.PlateNumberDisplay,
                PermitDate = permit.PermitDate,
                RequestMode = permit.HasDailyLeaveSchedule
                    ? PermitExitRequestViewModel.RequestModeDailySchedule
                    : PermitExitRequestViewModel.RequestModeOneTime,
                ExitMode = permit.HasDailyLeaveSchedule
                    ? (
                        permit.DailyLeaveScheduleRequiresReturn
                            ? PermitExitRequestViewModel.ExitModeWithReturn
                            : PermitExitRequestViewModel.ExitModeWithoutReturn
                    )
                    : PermitExitRequestViewModel.ExitModeWithReturn,
                LeaveStartAt = null,
                LeaveEndAt = null,
                ScheduleStartDate = permit.DailyLeaveScheduleStartDate,
                ScheduleEndDate = permit.DailyLeaveScheduleEndDate,
                DailyExitTime = permit.DailyLeaveScheduleExitMinutes.HasValue
                    ? TimeSpan.FromMinutes(permit.DailyLeaveScheduleExitMinutes.Value)
                    : null,
                DailyReturnTime = permit.DailyLeaveScheduleReturnMinutes.HasValue
                    ? TimeSpan.FromMinutes(permit.DailyLeaveScheduleReturnMinutes.Value)
                    : null,
                LeaveReason = permit.HasDailyLeaveSchedule
                    ? permit.DailyLeaveScheduleReason
                    : string.Empty,
            };
        }

        public static PermitExitRequestViewModel BuildLeaveRequestViewModel(
            Permit permit,
            DateTime now
        )
        {
            var hasOpenOneTimeRequest = permit.PendingExitRequest && !permit.HasDailyLeaveSchedule;
            var oneTimeRequiresReturn = hasOpenOneTimeRequest
                ? permit.ExpectedReturnTime.HasValue
                : true;
            var leaveStartAt =
                permit.LeaveWindowStartAt.HasValue && permit.LeaveWindowStartAt.Value > now
                    ? permit.LeaveWindowStartAt.Value
                    : now;
            var leaveEndAt =
                permit.LeaveWindowEndAt.HasValue && permit.LeaveWindowEndAt.Value > leaveStartAt
                    ? permit.LeaveWindowEndAt.Value
                : oneTimeRequiresReturn ? (DateTime?)leaveStartAt.AddHours(1)
                : null;

            return new PermitExitRequestViewModel
            {
                PermitNumber = permit.PermitNumber,
                DriverName = permit.DriverName,
                NationalId = permit.NationalId,
                DepartmentName = permit.DepartmentName,
                PermitTypeDisplay = permit.PermitTypeDisplay,
                CurrentStateDisplay = string.Equals(
                    permit.CurrentState,
                    "Inside",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "داخل"
                    : "خارج",
                ApprovalStatusDisplay = permit.ApprovalStatusDisplay,
                VehicleType = permit.VehicleType,
                PlateNumberDisplay = permit.PlateNumberDisplay,
                PermitDate = permit.PermitDate,
                RequestMode = permit.HasDailyLeaveSchedule
                    ? PermitExitRequestViewModel.RequestModeDailySchedule
                    : PermitExitRequestViewModel.RequestModeOneTime,
                ExitMode =
                    permit.HasDailyLeaveSchedule
                        ? (
                            permit.DailyLeaveScheduleRequiresReturn
                                ? PermitExitRequestViewModel.ExitModeWithReturn
                                : PermitExitRequestViewModel.ExitModeWithoutReturn
                        )
                    : oneTimeRequiresReturn ? PermitExitRequestViewModel.ExitModeWithReturn
                    : PermitExitRequestViewModel.ExitModeWithoutReturn,
                LeaveStartAt = leaveStartAt,
                LeaveEndAt = leaveEndAt,
                ScheduleStartDate = permit.DailyLeaveScheduleStartDate,
                ScheduleEndDate = permit.DailyLeaveScheduleEndDate,
                DailyExitTime = permit.DailyLeaveScheduleExitMinutes.HasValue
                    ? TimeSpan.FromMinutes(permit.DailyLeaveScheduleExitMinutes.Value)
                    : null,
                DailyReturnTime = permit.DailyLeaveScheduleReturnMinutes.HasValue
                    ? TimeSpan.FromMinutes(permit.DailyLeaveScheduleReturnMinutes.Value)
                    : null,
                LeaveReason = permit.HasDailyLeaveSchedule
                    ? permit.DailyLeaveScheduleReason
                    : permit.LeaveReason,
            };
        }

        public static string BuildPermitUsageNotice(AdministrationSettings administration)
        {
            var departmentName = string.IsNullOrWhiteSpace(administration.DepartmentName)
                ? administration.OrganizationName
                : administration.DepartmentName;

            return $"هذا التصريح مخصص لدخول المبنى التابع لـ {departmentName}، ولا يُستخدم لأي جهة أخرى. ويتحمل من يُبرز هذا التصريح أو يستخدمه لغير الجهة الرسمية المخوَّل لها كامل المسؤولية.";
        }

        public static PermitVerificationViewModel BuildPermitVerificationModel(
            Permit? permit,
            bool isAuthorized,
            string status,
            string message,
            AdministrationSettings? administration = null
        )
        {
            var model = new PermitVerificationViewModel
            {
                IsValid = permit != null,
                IsAuthorized = isAuthorized,
                IsExpired = string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase),
                Message = message,
                OrganizationName = administration?.OrganizationName?.Trim() ?? string.Empty,
                PermitNumber = permit?.PublicPermitCode ?? string.Empty,
                PermitTypeDisplay = permit?.PermitTypeDisplay ?? string.Empty,
                PlateNumberDisplay = permit?.PlateNumberDisplay ?? string.Empty,
                ExpiresAt = permit?.ExpiresAt,
            };

            if (isAuthorized)
            {
                model.Title = "تصريح مصرح";
                model.StatusText = "مصرح";
                model.BadgeClass = "bg-success";
            }
            else if (string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase))
            {
                model.Title = "تصريح منتهي";
                model.StatusText = "منتهي";
                model.BadgeClass = "bg-warning text-dark";
            }
            else
            {
                model.Title = "تصريح غير مصرح";
                model.StatusText = "غير مصرح";
                model.BadgeClass = "bg-danger";
            }

            return model;
        }

        public static PermitDigitalPassViewModel BuildPermitDigitalPassModel(
            Permit? permit,
            bool isAuthorized,
            string status,
            string message,
            AdministrationSettings? administration,
            string qrImageUrl
        )
        {
            var model = new PermitDigitalPassViewModel
            {
                IsValid = permit != null,
                IsAuthorized = isAuthorized,
                IsExpired = string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase),
                Message = message,
                OrganizationName = administration?.OrganizationName?.Trim() ?? string.Empty,
                DepartmentName = administration?.DepartmentName?.Trim() ?? string.Empty,
                HolderName = permit?.DriverName ?? string.Empty,
                PermitNumber = permit?.PublicPermitCode ?? string.Empty,
                PermitTypeDisplay = permit?.PermitTypeDisplay ?? string.Empty,
                LocationDisplay = permit?.LocationDisplay ?? string.Empty,
                PlateNumberDisplay = permit?.PlateNumberDisplay ?? string.Empty,
                QrImageUrl = isAuthorized ? qrImageUrl : string.Empty,
                ExpiresAt = permit?.ExpiresAt,
            };

            if (isAuthorized)
            {
                model.Title = "تصريح دخول فعال";
                model.StatusText = "فعال";
            }
            else if (model.IsExpired)
            {
                model.Title = "انتهت صلاحية التصريح";
                model.StatusText = "منتهي";
            }
            else
            {
                model.Title = permit == null ? "الرابط غير صالح" : "التصريح غير فعال";
                model.StatusText = "غير مصرح";
            }

            return model;
        }
    }
}
