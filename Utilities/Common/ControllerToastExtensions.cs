using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Audit;
using VehiclePermitSystemWeb.Services.Backup;
using VehiclePermitSystemWeb.Services.Bootstrap;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Delegations;
using VehiclePermitSystemWeb.Services.Gate;
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Utilities.Common
{
    public static class ControllerToastExtensions
    {
        public static void ToastSuccess(this Controller controller, string? message)
        {
            ResolveService(controller).Success(message);
        }

        public static void ToastError(this Controller controller, string? message)
        {
            ResolveService(controller).Error(message);
        }

        public static void ToastWarning(this Controller controller, string? message)
        {
            ResolveService(controller).Warning(message);
        }

        public static void ToastInfo(this Controller controller, string? message)
        {
            ResolveService(controller).Info(message);
        }

        public static void ToastModelStateErrors(this Controller controller)
        {
            ResolveService(controller).ImportModelState(controller.ModelState);
        }

        private static IToastNotificationService ResolveService(Controller controller)
        {
            return controller.HttpContext.RequestServices.GetRequiredService<IToastNotificationService>();
        }
    }
}
