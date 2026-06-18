using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using VehiclePermitSystemWeb.Controllers;
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
using VehiclePermitSystemWeb.Services.Display;
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
    private static ClaimsPrincipal BuildPrincipal(
        string username,
        string role,
        params string[] permissions
    )
    {
        return BuildPrincipal(username, role, false, permissions);
    }

    private static ClaimsPrincipal BuildPrincipal(
        string username,
        string role,
        bool isSuperAdmin,
        params string[] permissions
    )
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.Role, role),
        };

        claims.AddRange(
            permissions.Select(permission => new Claim(AppPermissions.ClaimType, permission))
        );

        if (isSuperAdmin)
        {
            claims.Add(new Claim(AppClaimTypes.SuperAdmin, "true"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static UsersController CreateUsersController(
        IUserAdminService userAdminService,
        ClaimsPrincipal user
    )
    {
        var httpContext = new DefaultHttpContext { User = user };
        var serviceProvider = BuildControllerServiceProvider(httpContext);
        httpContext.RequestServices = serviceProvider;
        var actionContext = CreateActionContext(httpContext);
        actionContext.RouteData.Values["controller"] = "Users";

        return new UsersController(userAdminService, new NullPermitService())
        {
            ControllerContext = new ControllerContext(actionContext),
            TempData = serviceProvider
                .GetRequiredService<ITempDataDictionaryFactory>()
                .GetTempData(httpContext),
            ObjectValidator = new NullObjectModelValidator(),
            Url = new StubUrlHelper(actionContext),
        };
    }

    private static AdministrationController CreateAdministrationController(
        IUserAdminService userAdminService,
        ClaimsPrincipal user,
        ISystemClock systemClock
    )
    {
        var controllerContext = CreateControllerTestContext(user, "Administration");
        var backupService = new BackupService(
            new ConfigurationBuilder().Build(),
            LoggerFactory.Create(builder => { }).CreateLogger<BackupService>(),
            new TestWebHostEnvironment(),
            systemClock
        );

        return new AdministrationController(
            userAdminService,
            backupService,
            new TestWebHostEnvironment(),
            systemClock
        )
        {
            ControllerContext = new ControllerContext(controllerContext.ActionContext),
            TempData = controllerContext
                .ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>()
                .GetTempData(controllerContext.HttpContext),
            ObjectValidator = new NullObjectModelValidator(),
            Url = new StubUrlHelper(controllerContext.ActionContext),
        };
    }

    private static PermitsController CreatePermitsController(
        IPermitService permitService,
        IUserAdminService userAdminService,
        IAccessControlService accessControlService,
        ClaimsPrincipal user,
        ISystemClock systemClock
    )
    {
        var controllerContext = CreateControllerTestContext(user, "Permits");

        return new PermitsController(
            permitService,
            userAdminService,
            accessControlService,
            new TestWebHostEnvironment(),
            systemClock,
            new ConfigurationBuilder().Build()
        )
        {
            ControllerContext = new ControllerContext(controllerContext.ActionContext),
            TempData = controllerContext
                .ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>()
                .GetTempData(controllerContext.HttpContext),
            ObjectValidator = new NullObjectModelValidator(),
            Url = new StubUrlHelper(controllerContext.ActionContext),
        };
    }

    private static ReportsController CreateReportsController(
        IPermitService permitService,
        IReportsDashboardService reportsDashboardService,
        IAccessControlService accessControlService,
        IUserAdminService userAdminService,
        ClaimsPrincipal user
    )
    {
        var controllerContext = CreateControllerTestContext(user, "Reports");

        return new ReportsController(
            permitService,
            reportsDashboardService,
            new NullReportsDocumentService(),
            accessControlService,
            userAdminService
        )
        {
            ControllerContext = new ControllerContext(controllerContext.ActionContext),
            TempData = controllerContext
                .ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>()
                .GetTempData(controllerContext.HttpContext),
            ObjectValidator = new NullObjectModelValidator(),
            Url = new StubUrlHelper(controllerContext.ActionContext),
        };
    }

    private static DisplayController CreateDisplayController(
        IUserAdminService userAdminService,
        IPermitService permitService,
        IVisitService visitService,
        IDisplayDeviceService displayDeviceService,
        ClaimsPrincipal user,
        DefaultHttpContext? httpContext = null
    )
    {
        var effectiveHttpContext = httpContext ?? new DefaultHttpContext();
        effectiveHttpContext.User = user;
        var serviceProvider = BuildControllerServiceProvider(effectiveHttpContext);
        effectiveHttpContext.RequestServices = serviceProvider;
        var actionContext = CreateActionContext(effectiveHttpContext);
        actionContext.RouteData.Values["controller"] = "Display";

        return new DisplayController(
            userAdminService,
            permitService,
            visitService,
            displayDeviceService
        )
        {
            ControllerContext = new ControllerContext(actionContext),
            TempData = serviceProvider
                .GetRequiredService<ITempDataDictionaryFactory>()
                .GetTempData(effectiveHttpContext),
            ObjectValidator = new NullObjectModelValidator(),
            Url = new StubUrlHelper(actionContext),
        };
    }

    private static ControllerTestContext CreateControllerTestContext(
        ClaimsPrincipal user,
        string controllerName
    )
    {
        var httpContext = new DefaultHttpContext { User = user };
        var serviceProvider = BuildControllerServiceProvider(httpContext);
        httpContext.RequestServices = serviceProvider;
        var actionContext = CreateActionContext(httpContext);
        actionContext.RouteData.Values["controller"] = controllerName;

        return new ControllerTestContext(httpContext, serviceProvider, actionContext);
    }

    private static ActionContext CreateActionContext(HttpContext httpContext)
    {
        return new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());
    }

    private static IServiceProvider BuildControllerServiceProvider(HttpContext httpContext)
    {
        var services = new ServiceCollection();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ITempDataProvider, NullTempDataProvider>();
        services.AddSingleton<ITempDataDictionaryFactory, TempDataDictionaryFactory>();
        services.AddSingleton<IUrlHelperFactory, UrlHelperFactory>();
        services.AddTransient<IToastNotificationService, ToastNotificationService>();

        return services.BuildServiceProvider();
    }

    private sealed record ControllerTestContext(
        DefaultHttpContext HttpContext,
        IServiceProvider ServiceProvider,
        ActionContext ActionContext
    );

    private sealed class NullObjectModelValidator : IObjectModelValidator
    {
        public void Validate(
            ActionContext actionContext,
            ValidationStateDictionary? validationState,
            string prefix,
            object? model
        ) { }
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class NullReportsDocumentService : IReportsDocumentService
    {
        public ReportDocumentResult BuildVisitsReport(VisitFilterResult filter)
        {
            return new ReportDocumentResult();
        }

        public ReportDocumentResult BuildPermitActivityReport(
            string? activityQuery,
            IReadOnlyCollection<PermitActivity> activities,
            IReadOnlyDictionary<string, Permit> permitsLookup
        )
        {
            return new ReportDocumentResult();
        }

        public ReportDocumentResult BuildPendingPermitsReport(IReadOnlyCollection<Permit> permits)
        {
            return new ReportDocumentResult();
        }

        public ReportDocumentResult BuildPermitDetailedReport(
            Permit permit,
            IReadOnlyCollection<PermitActivity> activities
        )
        {
            return new ReportDocumentResult();
        }

        public ReportDocumentResult BuildPermitsReport(PermitReportResult permitReport)
        {
            return new ReportDocumentResult();
        }
    }

    private sealed class NullPermitService : IPermitService
    {
        public IEnumerable<Permit> GetPendingPermits(string? username = null) =>
            Array.Empty<Permit>();

        public IEnumerable<Permit> GetAllPermits(string? username = null) => Array.Empty<Permit>();

        public IEnumerable<Permit> GetVisiblePermits(string? username = null) =>
            Array.Empty<Permit>();

        public IEnumerable<Permit> GetApprovedPermitsForDisplay() => Array.Empty<Permit>();

        public IEnumerable<Permit> GetEmployeesOutForDisplay() => Array.Empty<Permit>();

        public IEnumerable<PermitActivity> GetRecentPermitActivities(
            int take = 20,
            string? username = null
        ) => Array.Empty<PermitActivity>();

        public PermitActivity? GetLatestPermitActivity() => null;

        public IEnumerable<PermitActivity> GetPermitActivities(
            string permitNumber,
            string? username = null
        ) => Array.Empty<PermitActivity>();

        public IEnumerable<PermitActivity> GetSequencedPermitActivities(string? username = null) =>
            Array.Empty<PermitActivity>();

        public List<PermitConflictViewModel> FindPermitConflicts(
            Permit permit,
            string? excludePermitNumber = null
        ) => [];

        public Permit? GetPermitByNumber(string permitNumber, string? username = null) => null;

        public void AddPermit(Permit permit, string? performedBy = null) { }

        public void UpdatePermit(Permit permit, string? performedBy = null) { }

        public void DeletePermit(string permitNumber, string? performedBy = null) { }

        public void CancelExpiredPermits(string? performedBy = null) { }

        public bool TryValidatePermitQrToken(
            string token,
            out Permit? permit,
            out string status,
            out string message
        )
        {
            permit = null;
            status = string.Empty;
            message = string.Empty;
            return false;
        }

        public string EnsurePermitQrToken(string permitNumber, string? performedBy = null) =>
            string.Empty;

        public (bool allowed, string reason) RecordPermitScan(
            string permitNumber,
            string? recordedBy = null,
            bool overrideEntry = false,
            PermitScanAuditContext? auditContext = null
        ) => (false, string.Empty);

        public bool MarkPermitDeparted(
            string permitNumber,
            string? recordedBy = null,
            PermitScanAuditContext? auditContext = null
        ) => false;

        public bool MarkPermitReturned(
            string permitNumber,
            string? recordedBy = null,
            PermitScanAuditContext? auditContext = null
        ) => false;

        public bool StopPermit(string permitNumber, string? performedBy = null) => false;

        public bool ReactivatePermit(string permitNumber, string? performedBy = null) => false;

        public bool ClearDailyLeaveSchedule(string permitNumber, string? performedBy = null) =>
            false;

        public bool ResolveUnauthorizedExitReview(
            string sequenceId,
            bool confirmViolation,
            string? performedBy = null
        ) => false;

        public bool AddOperatorNote(
            string permitNumber,
            string noteText,
            string? performedBy = null,
            PermitScanAuditContext? auditContext = null
        ) => false;

        public int ClosePermitsAtWorkEnd(string? performedBy = null) => 0;

        public bool SubmitLeaveRequest(
            string permitNumber,
            bool requiresReturn,
            string leaveReason,
            DateTime? leaveStartAt,
            DateTime? leaveEndAt,
            string? performedBy = null,
            bool isDailySchedule = false,
            DateTime? scheduleStartDate = null,
            DateTime? scheduleEndDate = null,
            int? dailyExitMinutes = null,
            int? dailyReturnMinutes = null
        ) => false;

        public void UpdatePermitApprovalStatus(
            string permitNumber,
            string approvalStatus,
            string? performedBy = null,
            bool? requiresReturn = null,
            DateTime? expectedReturnTime = null,
            bool? pendingExitRequest = null,
            string? leaveReason = null
        ) { }

        public bool ForwardPermitToGeneralManager(
            string permitNumber,
            string? performedBy = null
        ) => false;
    }

    private sealed class StubUrlHelper(ActionContext actionContext) : IUrlHelper
    {
        public ActionContext ActionContext { get; } = actionContext;

        public string? Action(UrlActionContext actionContext)
        {
            var controllerName =
                actionContext.Controller
                ?? ActionContext.RouteData.Values["controller"]?.ToString()
                ?? string.Empty;
            var actionName =
                actionContext.Action
                ?? ActionContext.RouteData.Values["action"]?.ToString()
                ?? string.Empty;
            var path = $"/{controllerName}/{actionName}".TrimEnd('/');

            if (actionContext.Values is null)
            {
                return path;
            }

            var routeValues = new RouteValueDictionary(actionContext.Values);
            var query = string.Join(
                "&",
                routeValues
                    .Where(pair => pair.Value is not null)
                    .Select(pair =>
                        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!.ToString() ?? string.Empty)}"
                    )
            );
            return string.IsNullOrWhiteSpace(query) ? path : $"{path}?{query}";
        }

        public string? Content(string? contentPath)
        {
            return contentPath;
        }

        public bool IsLocalUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            return (url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\')))
                || (url[0] == '~' && url.Length > 1 && url[1] == '/');
        }

        public string? Link(string? routeName, object? values)
        {
            return string.Empty;
        }

        public string? RouteUrl(UrlRouteContext routeContext)
        {
            return string.Empty;
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "PermitBehaviorChecks";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public string EnvironmentName { get; set; } = "Development";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
