using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Controllers;
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

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static readonly JsonSerializerOptions ToastJsonOptions = new(
        JsonSerializerDefaults.Web
    );

    private static Task ScenarioCreateDeleteAndActivateActionsBridgeToSuccessToasts(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        MutableSystemClock clock,
        IPermitService permitService,
        IUserAdminService userAdminService,
        IAccessControlService accessControlService
    )
    {
        const string actorUsername = "tester";
        var departmentName = $"قسم إشعارات {Guid.NewGuid():N}";
        var targetUsername = $"notify.{Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            db.Departments.Add(
                new Department
                {
                    Name = departmentName,
                    ManagerUsername = string.Empty,
                    ManagerDisplayName = string.Empty,
                    IsActive = true,
                }
            );
            db.SaveChanges();
        }

        var managerPrincipal = BuildPrincipal(
            actorUsername,
            AppRoles.GeneralManager,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments,
            AppPermissions.EditPermit,
            AppPermissions.ViewPermits,
            AppPermissions.StopPermit
        );

        var createController = CreateUsersController(userAdminService, managerPrincipal);
        var createResult = createController.Create(
            new UserEditViewModel
            {
                Username = targetUsername,
                FullName = "مستخدم إشعارات",
                Department = departmentName,
                JobTitle = "موظف استقبال",
                PhoneNumber = "0501112233",
                IsActive = true,
                Role = AppRoles.Receptionist,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
                AutoBindManager = false,
            }
        );
        Require(
            createResult is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "user create setup should redirect to the users index"
        );

        Require(
            HasQueuedToast(createController.TempData, "تمت إضافة المستخدم بنجاح", "success"),
            "user save should surface a success toast through the unified bridge"
        );

        var activateController = CreateUsersController(userAdminService, managerPrincipal);
        var deactivateResult = activateController.Deactivate(targetUsername);
        Require(
            deactivateResult
                is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "user deactivate setup should redirect to the users index"
        );

        Require(
            HasQueuedToast(activateController.TempData, "تم إيقاف المستخدم بنجاح", "success"),
            "delete-style deactivate action should surface a success toast"
        );

        var reactivateController = CreateUsersController(userAdminService, managerPrincipal);
        var reactivateResult = reactivateController.Activate(targetUsername);
        Require(
            reactivateResult
                is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "user activate should redirect to the users index"
        );

        Require(
            HasQueuedToast(reactivateController.TempData, "تم تفعيل المستخدم بنجاح", "success"),
            "reactivate action should surface a success toast through the unified bridge"
        );

        var permitNumber = CreateApprovedEmployeePermit(permitService);
        var permitsController = CreatePermitsController(
            permitService,
            userAdminService,
            accessControlService,
            BuildPrincipal(
                actorUsername,
                AppRoles.GeneralManager,
                AppPermissions.EditPermit,
                AppPermissions.ViewPermits,
                AppPermissions.StopPermit
            ),
            clock
        );

        var deletePermitResult = permitsController.DeleteConfirmed(permitNumber);
        Require(
            deletePermitResult
                is RedirectToActionResult { ActionName: nameof(PermitsController.Index) },
            "permit delete should redirect back to the permits index"
        );

        Require(
            HasQueuedToast(permitsController.TempData, "تم حذف التصريح بنجاح", "success"),
            "permit delete should surface a success toast"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDuplicateUsernameValidationBridgesToErrorToast(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        const string actorUsername = "tester";
        var departmentName = $"قسم تكرار {Guid.NewGuid():N}";
        var duplicateUsername = $"duplicate.{Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            db.Departments.Add(
                new Department
                {
                    Name = departmentName,
                    ManagerUsername = string.Empty,
                    ManagerDisplayName = string.Empty,
                    IsActive = true,
                }
            );
            db.SaveChanges();
        }

        Require(
            userAdminService.CreateUser(
                new UserAccount
                {
                    Username = duplicateUsername,
                    PasswordHash = "hash",
                    PasswordSalt = "salt",
                    DisplayName = "مستخدم موجود",
                    FullName = "مستخدم موجود",
                    Department = departmentName,
                    JobTitle = "موظف",
                    PhoneNumber = "0500000000",
                    Email = string.Empty,
                    IsActive = true,
                    Role = AppRoles.Receptionist,
                },
                "123456"
            ),
            "duplicate username setup should create the first user"
        );

        var controller = CreateUsersController(
            userAdminService,
            BuildPrincipal(
                actorUsername,
                AppRoles.GeneralManager,
                AppPermissions.ManageUsers,
                AppPermissions.ManageDepartments
            )
        );

        var result = controller.Create(
            new UserEditViewModel
            {
                Username = duplicateUsername,
                FullName = "مستخدم مكرر",
                Department = departmentName,
                JobTitle = "موظف",
                PhoneNumber = "0509999999",
                IsActive = true,
                Role = AppRoles.Receptionist,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
                AutoBindManager = false,
            }
        );
        Require(
            result is ViewResult { ViewName: "Create" },
            "duplicate username should return the create view"
        );

        var notifications = CollectLayoutNotifications(
            controller.TempData,
            controller.ViewData,
            controller.ModelState
        );
        Require(
            notifications.Any(notification =>
                string.Equals(notification.Type, "danger", StringComparison.Ordinal)
                && notification.Message.Contains(
                    "اسم المستخدم موجود مسبقًا",
                    StringComparison.Ordinal
                )
            ),
            "duplicate username validation should surface an error toast"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioToastServiceQueuesMultipleNotificationsInOrder()
    {
        var notifications = CollectToastQueue(service =>
        {
            service.Success("تم الحفظ بنجاح");
            service.Error("تعذر تنفيذ الحذف");
            service.Warning("تحقق من البيانات قبل المتابعة");
        });

        Require(notifications.Count == 3, "toast service should queue all submitted notifications");
        Require(
            string.Equals(notifications[0].Type, "success", StringComparison.Ordinal)
                && string.Equals(
                    notifications[0].Message,
                    "تم الحفظ بنجاح",
                    StringComparison.Ordinal
                ),
            "toast queue should keep the first success notification"
        );
        Require(
            string.Equals(notifications[1].Type, "danger", StringComparison.Ordinal)
                && string.Equals(
                    notifications[1].Message,
                    "تعذر تنفيذ الحذف",
                    StringComparison.Ordinal
                ),
            "toast queue should keep the second error notification"
        );
        Require(
            string.Equals(notifications[2].Type, "warning", StringComparison.Ordinal)
                && string.Equals(
                    notifications[2].Message,
                    "تحقق من البيانات قبل المتابعة",
                    StringComparison.Ordinal
                ),
            "toast queue should keep the third warning notification"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioValidationToastsCollapseMultipleMissingFieldsIntoOnePrompt()
    {
        var emptyNotifications = CollectToastQueue(service =>
            service.ImportModelState(new ModelStateDictionary())
        );

        Require(
            emptyNotifications.Count == 0,
            "empty model state should not create a validation toast"
        );

        var multiFieldModelState = new ModelStateDictionary();
        multiFieldModelState.AddModelError("Username", "اسم المستخدم مطلوب.");
        multiFieldModelState.AddModelError("NationalId", "رقم الهوية مطلوب.");
        multiFieldModelState.AddModelError("PhoneNumber", "رقم الجوال مطلوب.");

        var multiNotifications = CollectToastQueue(service =>
            service.ImportModelState(multiFieldModelState)
        );

        Require(
            multiNotifications.Count == 1,
            "multiple required fields should collapse into one validation toast"
        );
        Require(
            string.Equals(
                multiNotifications[0].Message,
                "أكمل الحقول الإلزامية المطلوبة.",
                StringComparison.Ordinal
            ),
            "multiple missing fields should surface one generic completion prompt"
        );

        var singleFieldModelState = new ModelStateDictionary();
        singleFieldModelState.AddModelError("VisitDate", "تاريخ الزيارة مطلوب.");

        var singleNotifications = CollectToastQueue(service =>
            service.ImportModelState(singleFieldModelState)
        );

        Require(
            singleNotifications.Count == 1,
            "single missing field should still emit one validation toast"
        );
        Require(
            string.Equals(
                singleNotifications[0].Message,
                "تاريخ الزيارة مطلوب.",
                StringComparison.Ordinal
            ),
            "single missing field should keep the specific field message"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioViewsContainNoInlineBootstrapAlerts()
    {
        var viewsRoot = Path.Combine(FindProjectRoot(), "Views");
        var inlineAlertPattern = new Regex(
            "class\\s*=\\s*\"(?:[^\"]*\\s)?alert(?:\\s[^\"]*)?\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        var offendingFiles = Directory
            .EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories)
            .Where(path => inlineAlertPattern.IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .ToList();

        Require(
            offendingFiles.Count == 0,
            $"views should not contain inline bootstrap alerts, but found: {string.Join(", ", offendingFiles)}"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioSharedLayoutKeepsRtlNotificationShell()
    {
        var layoutPath = Path.Combine(FindProjectRoot(), "Views", "Shared", "_Layout.cshtml");
        var layoutContent = File.ReadAllText(layoutPath);

        Require(
            layoutContent.Contains("<html lang=\"ar\" dir=\"rtl\"", StringComparison.Ordinal),
            "shared layout should keep the RTL root shell"
        );
        Require(
            layoutContent.Contains("class=\"app-notify", StringComparison.Ordinal),
            "shared layout should render notification toasts from the unified container"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioAccessDeniedPageBridgesToErrorToast(
        IPermitService permitService,
        IVisitService visitService,
        IReportsDashboardService reportsDashboardService,
        IUserAdminService userAdminService
    )
    {
        var httpContext = new DefaultHttpContext();
        var serviceProvider = BuildControllerServiceProvider(httpContext);
        httpContext.RequestServices = serviceProvider;
        var controller = new HomeController(
            permitService,
            visitService,
            reportsDashboardService,
            userAdminService
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = serviceProvider
                .GetRequiredService<ITempDataDictionaryFactory>()
                .GetTempData(httpContext),
        };

        var result = controller.AccessDenied();
        Require(result is ViewResult, "access denied should render its view");

        var notifications = CollectLayoutNotifications(controller.TempData, controller.ViewData);
        Require(
            notifications.Any(notification =>
                string.Equals(notification.Type, "danger", StringComparison.Ordinal)
                && notification.Message.Contains("ليس لديك صلاحية", StringComparison.Ordinal)
            ),
            "access denied page should surface an error toast through the shared layout bridge"
        );

        return Task.CompletedTask;
    }

    private static IReadOnlyList<ToastNotificationItem> CollectLayoutNotifications(
        ITempDataDictionary? tempData = null,
        ViewDataDictionary? viewData = null,
        ModelStateDictionary? modelState = null
    )
    {
        return CollectToastQueue(service =>
        {
            var directPayload = tempData?.Peek("__ToastNotifications") as string;

            if (!string.IsNullOrWhiteSpace(directPayload))
            {
                var directNotifications = JsonSerializer.Deserialize<List<ToastNotificationItem>>(
                    directPayload,
                    ToastJsonOptions
                );
                if (directNotifications is { Count: > 0 })
                {
                    service.Import(directNotifications);
                }
            }

            service.Import(GetTempDataString(tempData, "SuccessMessage"), "success");
            service.Import(GetTempDataString(tempData, "ErrorMessage"), "danger");
            service.Import(GetTempDataString(tempData, "AdministrationSaved"), "success");
            service.Import(GetTempDataString(tempData, "DisplaySettingsSaved"), "success");
            service.Import(GetTempDataString(tempData, "BackupMessage"), "info");
            service.Import(GetTempDataString(tempData, "UserSaved"), "success");
            service.Import(GetTempDataString(tempData, "UserSaveError"), "danger");
            service.Import(GetTempDataString(tempData, "DepartmentSaved"), "success");
            service.Import(GetTempDataString(tempData, "PasswordChanged"), "success");
            service.Import(GetTempDataString(tempData, "ProfileSaved"), "success");
            service.Import(GetTempDataString(tempData, "ProfileError"), "danger");
            service.Import(GetTempDataString(tempData, "KeyError"), "danger");

            var pageToastMessage = viewData?["PageToastMessage"] as string;
            var pageToastType = viewData?["PageToastType"] as string;
            service.Import(
                pageToastMessage,
                string.IsNullOrWhiteSpace(pageToastType) ? "danger" : pageToastType
            );

            if (modelState != null)
            {
                service.ImportModelState(modelState);
            }
        });
    }

    private static bool HasQueuedToast(
        ITempDataDictionary? tempData,
        string expectedMessage,
        string? expectedType = null
    )
    {
        var payload = tempData?.Peek("__ToastNotifications") as string;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            var notifications = JsonSerializer.Deserialize<List<ToastNotificationItem>>(
                payload,
                ToastJsonOptions
            );
            return notifications?.Any(notification =>
                    (
                        string.IsNullOrWhiteSpace(expectedType)
                        || string.Equals(
                            notification.Type,
                            expectedType,
                            StringComparison.OrdinalIgnoreCase
                        )
                    ) && notification.Message.Contains(expectedMessage, StringComparison.Ordinal)
                ) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<ToastNotificationItem> CollectToastQueue(
        Action<IToastNotificationService> enqueue
    )
    {
        var httpContext = new DefaultHttpContext();
        var tempDataProvider = new NullTempDataProvider();
        var tempDataFactory = new TempDataDictionaryFactory(tempDataProvider);
        var toastService = new ToastNotificationService(
            new HttpContextAccessor { HttpContext = httpContext },
            tempDataFactory
        );

        enqueue(toastService);
        return toastService.ConsumeNotifications();
    }

    private static string? GetTempDataString(ITempDataDictionary? tempData, string key)
    {
        return tempData?.Peek(key) as string;
    }

    private static string FindProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "VehiclePermitSystemWeb.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "VehiclePermitSystemWeb.csproj was not found from the test base directory."
        );
    }
}
