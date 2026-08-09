using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VehiclePermitSystemWeb.Controllers;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Audit;
using VehiclePermitSystemWeb.Services.Display;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static Task ScenarioDisplayDeviceApprovalFlow(
        IDisplayDeviceService displayDeviceService,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        MutableSystemClock clock
    )
    {
        const string setupKey = "Setup-Key-For-Display-Devices-Random-Alpha!";
        displayDeviceService.UpdateSetupKey(setupKey);

        var newDeviceContext = BuildDisplayHttpContext();
        Require(
            displayDeviceService.GetApprovedDevice(newDeviceContext) == null,
            "new display device without cookie should not be approved"
        );

        var rejectedDefault = displayDeviceService.RegisterRequest(
            new DisplayDeviceRegistrationViewModel
            {
                ScreenName = "شاشة مرفوضة",
                ScreenLocation = "البوابة",
                SetupKey = "display-2026-secure",
            },
            BuildDisplayHttpContext()
        );
        Require(rejectedDefault == null, "display-2026-secure must not create display requests");

        var pending = displayDeviceService.RegisterRequest(
            new DisplayDeviceRegistrationViewModel
            {
                ScreenName = "شاشة البوابة",
                ScreenLocation = "البوابة الرئيسية",
                Description = "اختبار اعتماد شاشة",
                SetupKey = setupKey,
            },
            BuildDisplayHttpContext()
        );
        Require(pending != null, "valid setup key should create a pending display request");
        Require(
            displayDeviceService.GetApprovedDevice(BuildDisplayHttpContext()) == null,
            "pending display device should not see display data"
        );

        var approvedResult = displayDeviceService.ApproveDevice(pending!.Id, "tester");
        Require(approvedResult.Success, "admin should approve pending display device");
        Require(
            displayDeviceService.UpdateDeviceMode(
                pending.Id,
                DisplayDeviceModes.WaitingBoard,
                "tester"
            ),
            "admin should be able to switch an approved display to waiting-board mode"
        );
        var approvedContext = BuildDisplayHttpContext(approvedResult.DeviceToken);
        Require(
            displayDeviceService.GetApprovedDevice(approvedContext) != null,
            "approved display device with cookie should see display data"
        );

        clock.Advance(TimeSpan.FromSeconds(30));
        Require(
            displayDeviceService.RecordHeartbeat(approvedContext),
            "approved display device heartbeat should succeed"
        );
        using (var db = dbFactory.CreateDbContext())
        {
            var stored = db.DisplayDevices.Single(item => item.Id == pending.Id);
            Require(stored.LastSeenUtc.HasValue, "heartbeat should update LastSeenUtc");
            Require(
                stored.Mode == DisplayDeviceModes.WaitingBoard,
                "selected display mode should be persisted"
            );
            Require(
                !db.UserActivities.Any(activity =>
                    activity.ActionType == "DisplayDeviceHeartbeat"
                ),
                "display heartbeats should not flood the audit log"
            );
        }

        var returningDeviceContext = BuildDisplayHttpContext(requestCode: pending.RequestCode);
        Require(
            displayDeviceService.TryActivateApprovedRequest(returningDeviceContext),
            "approved display device should re-link from its request cookie after program restart"
        );
        Require(
            returningDeviceContext
                .Response.Headers.SetCookie.ToString()
                .Contains(displayDeviceService.DeviceCookieName, StringComparison.Ordinal),
            "re-linked display device should receive a fresh device token cookie"
        );
        Require(
            !displayDeviceService.TryActivateApprovedRequest(
                BuildDisplayHttpContext(requestCode: pending.RequestCode)
            ),
            "a consumed display request cookie must not mint another device token"
        );
        using (var db = dbFactory.CreateDbContext())
        {
            Require(
                db.DisplayDevices.Count(item => item.ScreenName == pending.ScreenName) == 1,
                "re-linking an approved display device should not create duplicate requests"
            );
        }

        Require(
            displayDeviceService.DisableDevice(pending.Id, "tester"),
            "admin should disable approved display device"
        );
        Require(
            displayDeviceService.GetApprovedDevice(approvedContext) == null,
            "disabled display device should not see display data"
        );

        var rejected = displayDeviceService.RegisterRequest(
            new DisplayDeviceRegistrationViewModel
            {
                ScreenName = "شاشة الاستقبال",
                ScreenLocation = "الاستقبال",
                SetupKey = setupKey,
            },
            BuildDisplayHttpContext()
        );
        Require(rejected != null, "second valid setup key should create pending request");
        Require(
            displayDeviceService.RejectDevice(rejected!.Id, "tester"),
            "admin should reject device"
        );
        var rejectedApproval = displayDeviceService.ApproveDevice(rejected.Id, "tester");
        var rejectedContext = BuildDisplayHttpContext(rejectedApproval.DeviceToken);
        displayDeviceService.RejectDevice(rejected.Id, "tester");
        Require(
            displayDeviceService.GetApprovedDevice(rejectedContext) == null,
            "rejected display device should not see display data"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUnifiedDisplaySettingsUsesAdministrationKeyAndRotatesLinks(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService,
        MutableSystemClock clock
    )
    {
        const string oldKey = "Old-Display-Setup-Key-For-Tests-Alpha!";
        const string newKey = "New-Display-Setup-Key-For-Tests-Beta!";

        using (var db = dbFactory.CreateDbContext())
        {
            var settings = db.AdministrationSettings.FirstOrDefault(item => item.Id == 1);
            if (settings == null)
            {
                settings = new AdministrationSettings { Id = 1 };
                db.AdministrationSettings.Add(settings);
            }

            settings.DisplayBaseUrl = "http://127.0.0.1:5001";
            settings.DisplayAccessKey = oldKey;
            settings.AllowedClientIpRanges = "10.3.25.x";
            db.SaveChanges();
        }

        var controller = CreateAdministrationController(
            userAdminService,
            BuildPrincipal("tester", AppRoles.GeneralManager, AppPermissions.ManageAdministration),
            clock
        );
        var view = controller.DisplaySettings() as ViewResult;
        var model = view?.Model as DisplaySettingsViewModel;
        Require(
            model != null,
            "unified display settings page should return a display settings model"
        );
        Require(
            string.IsNullOrWhiteSpace(model!.DisplayAccessKey),
            "display settings page should not expose the stored setup key"
        );
        Require(
            !model.RegistrationUrl.Contains(Uri.EscapeDataString(oldKey), StringComparison.Ordinal)
                && model.GateDisplayUrl.StartsWith(
                    "http://127.0.0.1:5001",
                    StringComparison.Ordinal
                ),
            "display settings page should build links without exposing the stored setup key"
        );

        var oldLink = model.RegistrationUrl;
        var oldRequest = BuildDisplayHttpContext();
        var displayService = new DisplayDeviceService(
            dbFactory,
            clock,
            new PermitAuditService(),
            userAdminService
        );
        var pendingBeforeRotation = displayService.RegisterRequest(
            new DisplayDeviceRegistrationViewModel
            {
                ScreenName = "شاشة قبل التدوير",
                ScreenLocation = "البوابة",
                SetupKey = oldKey,
            },
            oldRequest
        );
        Require(pendingBeforeRotation != null, "old setup key should work before rotation");
        var approvedBeforeRotation = displayService.ApproveDevice(
            pendingBeforeRotation!.Id,
            "tester"
        );
        Require(
            approvedBeforeRotation.Success
                && displayService.GetApprovedDevice(
                    BuildDisplayHttpContext(approvedBeforeRotation.DeviceToken)
                ) != null,
            "approved display device token should work before setup key rotation"
        );

        displayService.UpdateSetupKey(newKey);
        displayService.InvalidateDeviceTrust("tester");
        Require(
            DisplayAccessKeyHasher.Verify(
                userAdminService.GetAdministrationSettings().DisplayAccessKey,
                newKey
            ),
            "setup key updates should persist a verifiable hash"
        );
        Require(
            displayService.RegisterRequest(
                new DisplayDeviceRegistrationViewModel
                {
                    ScreenName = "شاشة قديمة",
                    ScreenLocation = "البوابة",
                    SetupKey = oldKey,
                },
                BuildDisplayHttpContext()
            ) == null,
            "old setup key should stop working after rotation"
        );
        Require(
            displayService.RegisterRequest(
                new DisplayDeviceRegistrationViewModel
                {
                    ScreenName = "شاشة جديدة",
                    ScreenLocation = "البوابة",
                    SetupKey = newKey,
                },
                BuildDisplayHttpContext()
            ) != null,
            "new setup key should work after rotation"
        );
        Require(
            displayService.GetApprovedDevice(
                BuildDisplayHttpContext(approvedBeforeRotation.DeviceToken)
            ) == null,
            "existing display device token should stop working after setup key rotation"
        );

        var newView = controller.DisplaySettings() as ViewResult;
        var newModel = newView?.Model as DisplaySettingsViewModel;
        Require(
            newModel != null
                && string.Equals(newModel.RegistrationUrl, oldLink, StringComparison.Ordinal)
                && !newModel.RegistrationUrl.Contains(
                    Uri.EscapeDataString(newKey),
                    StringComparison.Ordinal
                ),
            "display registration link should never expose a persisted setup key"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDisplayPublicLinksOpenRegistrationForNewBrowsers()
    {
        var displayControllerType = typeof(DisplayController);
        foreach (
            var actionName in new[]
            {
                nameof(DisplayController.Gate),
                nameof(DisplayController.Visits),
                nameof(DisplayController.Access),
                nameof(DisplayController.Register),
            }
        )
        {
            var methods = displayControllerType
                .GetMethods()
                .Where(method => method.Name == actionName)
                .ToList();
            Require(methods.Count > 0, $"display action {actionName} should exist");
            Require(
                methods.All(method =>
                    method
                        .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false)
                        .Any()
                ),
                $"display action {actionName} should allow anonymous new display browsers to reach registration instead of login"
            );
        }

        var view = File.ReadAllText(
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "Views",
                    "Administration",
                    "DisplaySettings.cshtml"
                )
            )
        );
        Require(
            view.Contains("value=\"@Model.RegistrationUrl\"", StringComparison.Ordinal)
                && view.Contains(
                    "data-copy-source=\"display-registration-url\"",
                    StringComparison.Ordinal
                ),
            "unified display page should copy the registration/linking URL, not the protected administration page"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDisplaySidebarHasSingleEntryAndRotationRequiresAdministration()
    {
        var layout = File.ReadAllText(
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "Views",
                    "Shared",
                    "_Layout.cshtml"
                )
            )
        );
        Require(
            CountOccurrences(layout, ">شاشات العرض<") == 1,
            "sidebar should expose one display screens entry"
        );
        Require(
            !layout.Contains("إدارة شاشات العرض", StringComparison.Ordinal),
            "sidebar should not expose a separate display management entry"
        );

        var method = typeof(AdministrationController).GetMethod("RotateDisplayAccessKey");
        var authorizeAttribute = method
            ?.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .OfType<AuthorizeAttribute>()
            .FirstOrDefault();
        Require(
            string.Equals(
                authorizeAttribute?.Policy,
                AppPolicies.ManageAdministration,
                StringComparison.Ordinal
            ),
            "generating a new display key should require administration permission"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDisplayOperatorEndpointsAreRateLimitedAndReturnUrlsStayLocal(
        IUserAdminService userAdminService,
        IPermitService permitService,
        IVisitService visitService,
        IDisplayDeviceService displayDeviceService
    )
    {
        const string setupKey = "Setup-Key-For-Display-Devices-Random-Alpha!";
        displayDeviceService.UpdateSetupKey(setupKey);

        var pending = displayDeviceService.RegisterRequest(
            new DisplayDeviceRegistrationViewModel
            {
                ScreenName = "شاشة اختبار الأمان",
                ScreenLocation = "البوابة",
                SetupKey = setupKey,
            },
            BuildDisplayHttpContext()
        );
        Require(pending != null, "display request should be created for redirect hardening checks");

        var approved = displayDeviceService.ApproveDevice(pending!.Id, "tester");
        Require(
            approved.Success,
            "display request should be approved for redirect hardening checks"
        );

        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var approvedController = CreateDisplayController(
            userAdminService,
            permitService,
            visitService,
            displayDeviceService,
            anonymousUser,
            BuildDisplayHttpContext(deviceToken: approved.DeviceToken)
        );
        var redirectedResult =
            approvedController.Register("https://evil.example/phish") as RedirectResult;
        Require(
            redirectedResult != null
                && string.Equals(redirectedResult.Url, "/Display/Gate", StringComparison.Ordinal),
            "approved display registration should ignore external returnUrl values and redirect to the safe local gate path"
        );

        var pendingController = CreateDisplayController(
            userAdminService,
            permitService,
            visitService,
            displayDeviceService,
            anonymousUser,
            BuildDisplayHttpContext()
        );
        var registerView = pendingController.Register("https://evil.example/phish") as ViewResult;
        Require(
            registerView != null
                && string.Equals(
                    registerView.ViewData["ReturnUrl"] as string,
                    "/Display/Gate",
                    StringComparison.Ordinal
                ),
            "display registration view should replace external returnUrl values with the safe local gate path"
        );

        var keyEntryRedirect =
            pendingController.KeyEntry("https://evil.example/phish") as RedirectToActionResult;
        Require(
            keyEntryRedirect != null
                && string.Equals(
                    keyEntryRedirect.ActionName,
                    nameof(DisplayController.Register),
                    StringComparison.Ordinal
                )
                && string.Equals(
                    keyEntryRedirect.RouteValues?["returnUrl"]?.ToString(),
                    "/Display/Gate",
                    StringComparison.Ordinal
                ),
            "display key entry should not forward external returnUrl values into the registration flow"
        );

        foreach (
            var actionName in new[]
            {
                nameof(DisplayController.SwitchOperator),
                nameof(DisplayController.ChangeOperatorPin),
            }
        )
        {
            var method = typeof(DisplayController).GetMethod(actionName);
            var limiterAttribute = method
                ?.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
                .OfType<EnableRateLimitingAttribute>()
                .FirstOrDefault();
            Require(
                string.Equals(
                    limiterAttribute?.PolicyName,
                    "display-operator",
                    StringComparison.Ordinal
                ),
                $"display action {actionName} should enforce the display-operator rate-limiting policy"
            );
        }

        return Task.CompletedTask;
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static DefaultHttpContext BuildDisplayHttpContext(
        string? deviceToken = null,
        string? requestCode = null
    )
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        context.Request.Headers.UserAgent = "PermitBehaviorChecks";
        var cookies = new List<string>();
        if (!string.IsNullOrWhiteSpace(deviceToken))
        {
            cookies.Add($"DisplayDeviceToken={deviceToken}");
        }
        if (!string.IsNullOrWhiteSpace(requestCode))
        {
            cookies.Add($"DisplayDeviceRequestCode={requestCode}");
        }
        if (cookies.Count > 0)
        {
            context.Request.Headers.Cookie = string.Join("; ", cookies);
        }

        return context;
    }
}
