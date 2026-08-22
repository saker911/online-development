using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static readonly JsonSerializerOptions UserToastJsonOptions = new(
        JsonSerializerDefaults.Web
    );

    private static Task ScenarioSuperAdminCreatesGeneralManagerInsideSelectedTenant(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        const string tenantId = "selected-gm-tenant";
        const string username = "2699999998";
        var departmentName = $"إدارة جهة مختارة {Guid.NewGuid():N}";
        string defaultManagerUsername;

        using (var db = dbFactory.CreateDbContext())
        {
            var defaultSettings = db.AdministrationSettings.SingleOrDefault();
            if (defaultSettings == null)
            {
                defaultSettings = new AdministrationSettings
                {
                    Id = 1,
                    TenantId = TenantDefaults.DefaultTenantId,
                    OrganizationName = "الجهة الافتراضية",
                    DepartmentName = "الإدارة العامة",
                };
                db.AdministrationSettings.Add(defaultSettings);
            }
            defaultManagerUsername = defaultSettings.GeneralManagerUsername;
            var nextSettingsId = defaultSettings.Id + 1;
            db.Tenants.Add(
                new Tenant
                {
                    TenantId = tenantId,
                    Name = "جهة المدير المختارة",
                    Slug = tenantId,
                    IsActive = true,
                    SubscriptionStatus = TenantSubscriptionStatuses.Active,
                }
            );
            db.AdministrationSettings.Add(
                new AdministrationSettings
                {
                    Id = nextSettingsId,
                    TenantId = tenantId,
                    OrganizationName = "جهة المدير المختارة",
                    DepartmentName = departmentName,
                }
            );
            db.Departments.Add(
                new Department
                {
                    TenantId = tenantId,
                    Name = departmentName,
                    IsActive = true,
                }
            );
            db.SaveChanges();
        }

        var message = userAdminService.AssignGeneralManager(
            new GeneralManagerAssignmentRequest
            {
                TenantId = tenantId,
                SelectionMode = GeneralManagerSelectionModes.CreateNew,
                NewUserUsername = username,
                NewUserFullName = "مدير الجهة المختارة",
                NewUserPhoneNumber = "0557777777",
                NewUserEmail = "selected-manager@example.com",
                NewUserEmployeeNumber = "GM-SELECTED",
                NewUserPassword = "Aa123456!",
                NewUserConfirmPassword = "Aa123456!",
                NewUserJobTitle = "مدير عام",
                NewUserIsActive = true,
                NewUserMustChangePassword = true,
                AssignmentType = GeneralManagerAssignmentTypes.Permanent,
                PreviousGeneralManagerAction = GeneralManagerPreviousActions.EndAssignment,
            },
            "super-admin"
        );

        Require(!string.IsNullOrWhiteSpace(message), "selected-tenant general-manager assignment should succeed");
        using var assertDb = dbFactory.CreateDbContext();
        var manager = assertDb.UserAccounts.IgnoreQueryFilters().Single(user => user.Username == username);
        var targetSettings = assertDb.AdministrationSettings.IgnoreQueryFilters().Single(settings => settings.TenantId == tenantId);
        var defaultSettingsAfter = assertDb.AdministrationSettings.Single();
        Require(manager.TenantId == tenantId, "created general manager must belong to the selected tenant");
        Require(manager.MustChangePassword, "temporary general-manager credentials must require a password change");
        Require(manager.CanManageAdministration && manager.CanManageUsers, "created general manager must receive role defaults");
        Require(targetSettings.GeneralManagerUsername == username, "selected tenant settings must link the new general manager");
        Require(defaultSettingsAfter.GeneralManagerUsername == defaultManagerUsername, "default tenant settings must not be changed");
        Require(
            assertDb.UserActivities.IgnoreQueryFilters().Any(activity =>
                activity.TenantId == tenantId
                && activity.Username == username
                && activity.ActionType == "GeneralManagerAssigned"
            ),
            "general-manager audit activity must be recorded in the selected tenant"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUserManagerAssignmentReplacesPreviousManager(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var departmentName = $"قسم مستخدمين {Guid.NewGuid():N}";
        var previousManagerUsername = $"users-manager-old-{Guid.NewGuid():N}";
        var newManagerUsername = $"users-manager-new-{Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            db.Departments.Add(
                new Department
                {
                    Name = departmentName,
                    ManagerUsername = previousManagerUsername,
                    ManagerDisplayName = "مدير سابق",
                    IsActive = true,
                }
            );
            db.UserAccounts.Add(
                CreateDepartmentManagerAccount(previousManagerUsername, departmentName, "مدير سابق")
            );
            db.SaveChanges();
        }

        var created = userAdminService.CreateUser(
            CreateDepartmentManagerAccount(newManagerUsername, departmentName, "مدير جديد"),
            "123456"
        );
        Require(created, "new department manager should be created successfully");

        using (var db = dbFactory.CreateDbContext())
        {
            var department = db.Departments.AsNoTracking().Single(x => x.Name == departmentName);
            var previousManager = db
                .UserAccounts.AsNoTracking()
                .Single(x => x.Username == previousManagerUsername);
            var newManager = db
                .UserAccounts.AsNoTracking()
                .Single(x => x.Username == newManagerUsername);

            Require(
                string.Equals(
                    department.ManagerUsername,
                    newManagerUsername,
                    StringComparison.OrdinalIgnoreCase
                ),
                "creating a new department manager from users should replace the previous manager on the department"
            );
            Require(
                string.Equals(
                    newManager.Department,
                    departmentName,
                    StringComparison.OrdinalIgnoreCase
                ),
                "new manager should be assigned to the selected department"
            );
            Require(
                newManager.CanApprovePermit,
                "new manager should receive manager approval permissions"
            );
            Require(
                string.Equals(
                    previousManager.Role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                ),
                "previous manager should remain a manager account after being replaced"
            );
            Require(
                !previousManager.CanApprovePermit && !previousManager.CanApproveVisits,
                "previous manager without a managed department should lose approval permissions"
            );
        }

        return Task.CompletedTask;
    }

    private static Task ScenarioDepartmentManagerAssignmentSyncsDepartmentAndPermissions(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var targetDepartmentName = $"قسم شاشة الأقسام {Guid.NewGuid():N}";
        var previousManagerUsername = $"department-manager-old-{Guid.NewGuid():N}";
        var unassignedManagerUsername = $"department-manager-free-{Guid.NewGuid():N}";
        var targetDepartmentId = 0;

        using (var db = dbFactory.CreateDbContext())
        {
            var department = new Department
            {
                Name = targetDepartmentName,
                ManagerUsername = previousManagerUsername,
                ManagerDisplayName = "مدير القسم السابق",
                IsActive = true,
            };
            db.Departments.Add(department);
            db.UserAccounts.Add(
                CreateDepartmentManagerAccount(
                    previousManagerUsername,
                    targetDepartmentName,
                    "مدير القسم السابق"
                )
            );

            var unassignedManager = CreateDepartmentManagerAccount(
                unassignedManagerUsername,
                string.Empty,
                "مدير بدون قسم"
            );
            unassignedManager.CanViewDashboard = false;
            unassignedManager.CanViewPermits = false;
            unassignedManager.CanViewVisitorPermits = false;
            unassignedManager.CanCreatePermit = false;
            unassignedManager.CanCreateVisitorPermit = false;
            unassignedManager.CanEditPermit = false;
            unassignedManager.CanEditVisitorPermit = false;
            unassignedManager.CanApprovePermit = false;
            unassignedManager.CanApproveLeaveRequest = false;
            unassignedManager.CanStopPermit = false;
            unassignedManager.CanViewVisits = false;
            unassignedManager.CanApproveDetainedVisit = false;
            unassignedManager.CanApproveVisits = false;
            db.UserAccounts.Add(unassignedManager);
            db.SaveChanges();
            targetDepartmentId = department.Id;
        }

        var assigned = userAdminService.SetDepartmentManager(
            targetDepartmentId,
            unassignedManagerUsername
        );
        Require(assigned, "department screen should assign the selected manager successfully");

        using (var db = dbFactory.CreateDbContext())
        {
            var department = db.Departments.AsNoTracking().Single(x => x.Id == targetDepartmentId);
            var previousManager = db
                .UserAccounts.AsNoTracking()
                .Single(x => x.Username == previousManagerUsername);
            var currentManager = db
                .UserAccounts.AsNoTracking()
                .Single(x => x.Username == unassignedManagerUsername);

            Require(
                string.Equals(
                    department.ManagerUsername,
                    unassignedManagerUsername,
                    StringComparison.OrdinalIgnoreCase
                ),
                "department assignment should replace the old department manager"
            );
            Require(
                string.Equals(
                    currentManager.Department,
                    targetDepartmentName,
                    StringComparison.OrdinalIgnoreCase
                ),
                "department assignment should move the selected manager into the assigned department"
            );
            Require(
                currentManager.CanApprovePermit && currentManager.CanApproveVisits,
                "manager selected from departments should regain permissions after assignment"
            );
            Require(
                !previousManager.CanApprovePermit && !previousManager.CanApproveVisits,
                "replaced manager should lose approval permissions after replacement from departments"
            );
        }

        return Task.CompletedTask;
    }

    private static Task ScenarioUsersControllerCreateDepartmentManagerRedirectsToHandoverWhenDepartmentAlreadyHasManager(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var departmentName = $"قسم إعادة توجيه المدراء {Guid.NewGuid():N}";
        var previousManagerUsername = $"users-manager-current-{Guid.NewGuid():N}";
        var newManagerUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        var departmentId = 0;

        using (var db = dbFactory.CreateDbContext())
        {
            var department = new Department
            {
                Name = departmentName,
                ManagerUsername = previousManagerUsername,
                ManagerDisplayName = "مدير قائم",
                IsActive = true,
            };

            db.Departments.Add(department);
            db.UserAccounts.Add(
                CreateDepartmentManagerAccount(previousManagerUsername, departmentName, "مدير قائم")
            );
            db.SaveChanges();
            departmentId = department.Id;
        }

        var managerPrincipal = BuildPrincipal(
            "tester",
            AppRoles.GeneralManager,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments
        );

        var controller = CreateUsersController(userAdminService, managerPrincipal);
        var result = controller.Create(
            new UserEditViewModel
            {
                Username = newManagerUsername,
                FullName = "مدير جديد يحتاج تناقل",
                Department = departmentName,
                JobTitle = "مدير قسم",
                PhoneNumber = "0501111122",
                IsActive = true,
                Role = AppRoles.DepartmentManager,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            result
                is RedirectToActionResult
                {
                    ActionName: nameof(AdministrationController.Leadership),
                    ControllerName: "Administration"
                }
                && Convert.ToInt32(
                    ((RedirectToActionResult)result).RouteValues!["handoverDepartmentId"]
                ) == departmentId,
            "creating a department manager from users should redirect to department handover when the target department already has a manager"
        );
        var redirect = (RedirectToActionResult)result;
        Require(
            Convert.ToInt32(redirect.RouteValues!["handoverWizardStep"]) == 2,
            "department-manager redirect should resume the handover wizard on the manager details step"
        );
        Require(
            string.Equals(
                redirect.RouteValues["handoverCreateNewManager"]?.ToString(),
                bool.TrueString,
                StringComparison.OrdinalIgnoreCase
            ),
            "department-manager redirect should preselect the create-new-manager branch"
        );
        Require(
            string.Equals(
                redirect.RouteValues["handoverNewManagerUsername"]?.ToString(),
                newManagerUsername,
                StringComparison.OrdinalIgnoreCase
            )
                && string.Equals(
                    redirect.RouteValues["handoverNewManagerFullName"]?.ToString(),
                    "مدير جديد يحتاج تناقل",
                    StringComparison.Ordinal
                )
                && string.Equals(
                    redirect.RouteValues["handoverNewManagerPhoneNumber"]?.ToString(),
                    "0501111122",
                    StringComparison.Ordinal
                ),
            "department-manager redirect should carry the drafted manager details into the handover wizard"
        );
        Require(
            HasQueuedToast(controller.TempData, "تناقل المدراء", "warning"),
            "creating a department manager with an occupied department should show a handover warning"
        );
        Require(
            userAdminService.GetUserAccount(newManagerUsername) == null,
            "creating a department manager with an occupied department should not persist the new account before the handover flow"
        );
        Require(
            string.Equals(
                userAdminService.GetDepartment(departmentId)?.ManagerUsername,
                previousManagerUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "redirecting to handover should leave the current department manager unchanged"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUsersControllerCreateDepartmentManagerCreatesDirectlyWhenDepartmentHasNoManager(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var departmentName = $"قسم بلا مدير {Guid.NewGuid():N}";
        var newManagerUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";

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
            "tester",
            AppRoles.GeneralManager,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments
        );

        var controller = CreateUsersController(userAdminService, managerPrincipal);
        var result = controller.Create(
            new UserEditViewModel
            {
                Username = newManagerUsername,
                FullName = "مدير جديد مباشر",
                Department = departmentName,
                JobTitle = "مدير قسم",
                PhoneNumber = "0501111133",
                IsActive = true,
                Role = AppRoles.DepartmentManager,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            result is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "creating a department manager from users should still create directly when the target department has no current manager"
        );
        Require(
            HasQueuedToast(controller.TempData, "تمت إضافة المستخدم بنجاح", "success"),
            "creating a department manager for an empty department should keep the normal success flow"
        );

        var createdUser =
            userAdminService.GetUserAccount(newManagerUsername)
            ?? throw new InvalidOperationException(
                "direct department manager creation did not persist the account"
            );
        Require(
            string.Equals(
                createdUser.Department,
                departmentName,
                StringComparison.OrdinalIgnoreCase
            ),
            "direct department manager creation should assign the manager to the selected department"
        );
        Require(
            string.Equals(
                userAdminService
                    .GetDepartments()
                    .Single(department =>
                        string.Equals(
                            department.Name,
                            departmentName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ManagerUsername,
                newManagerUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "direct department manager creation should link the empty department to the new manager"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUsersControllerCreateDepartmentManagerPrefillsDepartmentFromDepartmentsShortcut(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var departmentName = $"قسم جاهز للتعيين {Guid.NewGuid():N}";

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
            "tester",
            AppRoles.GeneralManager,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments
        );

        var controller = CreateUsersController(userAdminService, managerPrincipal);
        var result = controller.Create(
            presetRole: AppRoles.DepartmentManager,
            creationFlow: "department-manager",
            department: departmentName
        );

        Require(
            result is ViewResult { Model: UserEditViewModel },
            "department-manager shortcut should render the create view"
        );

        var viewResult = (ViewResult)result;
        var model = (UserEditViewModel)viewResult.Model!;
        Require(
            string.Equals(
                model.Role,
                AppRoles.DepartmentManager,
                StringComparison.OrdinalIgnoreCase
            ),
            "department-manager shortcut should preset the department manager role"
        );
        Require(
            string.Equals(model.Department, departmentName, StringComparison.OrdinalIgnoreCase),
            "department-manager shortcut should preselect the requested department"
        );
        Require(
            model.DepartmentManagerSummaryByName.TryGetValue(
                departmentName,
                out var departmentSummary
            ) && string.IsNullOrWhiteSpace(departmentSummary),
            "department-manager shortcut should keep the current manager summary empty for a vacant department"
        );
        Require(
            model.AutoBindManager,
            "department-manager shortcut should enable automatic manager binding"
        );
        Require(
            string.Equals(
                viewResult.ViewData["CreateFlowTitle"]?.ToString(),
                "تعيين مدير قسم",
                StringComparison.Ordinal
            ),
            "department-manager shortcut should use the department-manager flow title"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUsersControllerCreateDepartmentManagerShowsOccupiedDepartmentManagerSummary(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var departmentName = $"قسم مشغول {Guid.NewGuid():N}";
        var currentManagerUsername = $"users-manager-current-{Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            db.Departments.Add(
                new Department
                {
                    Name = departmentName,
                    ManagerUsername = currentManagerUsername,
                    ManagerDisplayName = "مدير قائم",
                    IsActive = true,
                }
            );
            db.UserAccounts.Add(
                CreateDepartmentManagerAccount(currentManagerUsername, departmentName, "مدير قائم")
            );
            db.SaveChanges();
        }

        var managerPrincipal = BuildPrincipal(
            "tester",
            AppRoles.GeneralManager,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments
        );

        var controller = CreateUsersController(userAdminService, managerPrincipal);
        var result = controller.Create(
            presetRole: AppRoles.DepartmentManager,
            creationFlow: "department-manager",
            department: departmentName
        );

        Require(
            result is ViewResult { Model: UserEditViewModel },
            "occupied department should keep the create screen open for the handover wizard"
        );

        var viewResult = (ViewResult)result;
        var model = (UserEditViewModel)viewResult.Model!;
        Require(
            string.Equals(model.Department, departmentName, StringComparison.OrdinalIgnoreCase),
            "occupied department should remain selected in the department-manager wizard"
        );
        Require(
            model.DepartmentManagerSummaryByName.TryGetValue(departmentName, out var summary)
                && summary.Contains("مدير قائم", StringComparison.OrdinalIgnoreCase)
                && summary.Contains(currentManagerUsername, StringComparison.OrdinalIgnoreCase),
            "occupied department should surface the current manager summary in the create view model"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUsersControllerCreateGeneralManagerRequiresConfiguredAdministration(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        using (var db = dbFactory.CreateDbContext())
        {
            var settings = db.AdministrationSettings.FirstOrDefault(x => x.Id == 1);
            if (settings == null)
            {
                settings = new AdministrationSettings { Id = 1 };
                db.AdministrationSettings.Add(settings);
            }

            settings.OrganizationName = string.Empty;
            settings.DepartmentName = string.Empty;
            settings.GeneralManagerUsername = string.Empty;
            settings.ManagerName = string.Empty;
            settings.ManagerTitle = string.Empty;
            settings.ManagerPhoneNumber = string.Empty;
            db.SaveChanges();
        }

        var newGeneralManagerUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        var controller = CreateUsersController(
            userAdminService,
            BuildPrincipal("system-owner", AppRoles.SystemAdmin, true, AppPermissions.ManageUsers)
        );
        var result = controller.Create(
            new UserEditViewModel
            {
                Username = newGeneralManagerUsername,
                FullName = "مدير عام بلا إدارة",
                Department = "قسم يجب تجاهله",
                JobTitle = "مدير عام",
                PhoneNumber = "0502222211",
                IsActive = true,
                Role = AppRoles.GeneralManager,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            result is ViewResult { Model: UserEditViewModel },
            "general-manager creation should stay on create view when administration data is missing"
        );
        Require(
            controller
                .ModelState[string.Empty]
                ?.Errors.Any(error =>
                    error.ErrorMessage.Contains("تهيئة بيانات الإدارة", StringComparison.Ordinal)
                ) == true,
            "general-manager creation should explain that administration data must be configured first"
        );
        Require(
            userAdminService.GetUserAccount(newGeneralManagerUsername) == null,
            "general-manager creation without administration data should not create an account"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUsersControllerCreateGeneralManagerUsesAssignmentWorkflow(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var newGeneralManagerUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        using (var db = dbFactory.CreateDbContext())
        {
            foreach (
                var existingGeneralManager in db.UserAccounts.Where(user =>
                    user.Role == AppRoles.GeneralManager
                )
            )
            {
                existingGeneralManager.Role = AppRoles.Employee;
                existingGeneralManager.Department = string.Empty;
                existingGeneralManager.ManagerUsername = string.Empty;
                AppPermissions.ApplyRoleDefaults(existingGeneralManager);
            }

            SeedAdministrationSettings(db, string.Empty);
        }

        var controller = CreateUsersController(
            userAdminService,
            BuildPrincipal(
                "system-owner",
                AppRoles.SystemAdmin,
                true,
                AppPermissions.ManageUsers,
                AppPermissions.ManageAdministration
            )
        );
        var result = controller.Create(
            new UserEditViewModel
            {
                Username = newGeneralManagerUsername,
                FullName = "مدير عام من المستخدمين",
                Department = "قسم غير مستخدم",
                JobTitle = "مدير عام",
                PhoneNumber = "0502222212",
                IsActive = true,
                Role = AppRoles.GeneralManager,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            result is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "general-manager creation without a current manager should complete through the assignment workflow"
        );
        var temporaryPassword = ExtractTemporaryPassword(controller.TempData);
        Require(
            !string.IsNullOrWhiteSpace(temporaryPassword),
            "general-manager creation should publish the generated temporary password"
        );

        using var assertDb = dbFactory.CreateDbContext();
        var settings = assertDb.AdministrationSettings.AsNoTracking().Single(x => x.Id == 1);
        var createdGeneralManager = assertDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == newGeneralManagerUsername);
        Require(
            string.Equals(
                settings.GeneralManagerUsername,
                newGeneralManagerUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "general-manager creation from users should link administration settings to the new manager"
        );
        Require(
            string.Equals(
                createdGeneralManager.Role,
                AppRoles.GeneralManager,
                StringComparison.Ordinal
            )
                && string.IsNullOrWhiteSpace(createdGeneralManager.Department)
                && string.IsNullOrWhiteSpace(createdGeneralManager.ManagerUsername),
            "general-manager created from users should be linked to administration only, not to a department manager slot"
        );
        Require(
            createdGeneralManager.MustChangePassword,
            "general-manager temporary password from users should require change at first login"
        );
        Require(
            userAdminService.ValidateCredentials(
                newGeneralManagerUsername,
                temporaryPassword!,
                out _,
                out var createdRole
            ) && string.Equals(createdRole, AppRoles.GeneralManager, StringComparison.Ordinal),
            "general-manager created from users should authenticate with the generated workflow password"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUsersControllerCreateGeneralManagerRedirectsToTransitionWhenCurrentExists(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var currentGeneralManagerUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        var newGeneralManagerUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        using (var db = dbFactory.CreateDbContext())
        {
            var currentGeneralManager = new UserAccount
            {
                Username = currentGeneralManagerUsername,
                DisplayName = "مدير عام حالي",
                FullName = "مدير عام حالي",
                Department = string.Empty,
                JobTitle = "مدير عام",
                PhoneNumber = "0502222213",
                Role = AppRoles.GeneralManager,
                IsActive = true,
            };
            AppPermissions.ApplyRoleDefaults(currentGeneralManager);
            db.UserAccounts.Add(currentGeneralManager);
            SeedAdministrationSettings(db, currentGeneralManagerUsername);
        }

        var controller = CreateUsersController(
            userAdminService,
            BuildPrincipal(
                "system-owner",
                AppRoles.SystemAdmin,
                true,
                AppPermissions.ManageUsers,
                AppPermissions.ManageDepartments
            )
        );
        var result = controller.Create(
            new UserEditViewModel
            {
                Username = newGeneralManagerUsername,
                FullName = "مدير عام بديل",
                Department = "قسم غير مستخدم",
                JobTitle = "مدير عام",
                PhoneNumber = "0502222214",
                IsActive = true,
                Role = AppRoles.GeneralManager,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            result
                is RedirectToActionResult
                {
                    ActionName: nameof(AdministrationController.Leadership),
                    ControllerName: "Administration"
                },
            "general-manager creation with a current manager should redirect to the existing transition wizard"
        );
        var redirect = (RedirectToActionResult)result;
        Require(
            string.Equals(
                redirect.RouteValues?["openGeneralManagerWizard"]?.ToString(),
                bool.TrueString,
                StringComparison.OrdinalIgnoreCase
            )
                && string.Equals(
                    redirect.RouteValues?["generalManagerSelectionMode"]?.ToString(),
                    GeneralManagerSelectionModes.CreateNew,
                    StringComparison.Ordinal
                ),
            "general-manager transition redirect should open the create-new branch of the current wizard"
        );
        Require(
            string.Equals(
                redirect.RouteValues?["generalManagerNewUserUsername"]?.ToString(),
                newGeneralManagerUsername,
                StringComparison.OrdinalIgnoreCase
            )
                && string.Equals(
                    redirect.RouteValues?["generalManagerNewUserFullName"]?.ToString(),
                    "مدير عام بديل",
                    StringComparison.Ordinal
                ),
            "general-manager transition redirect should carry the drafted user details"
        );
        Require(
            HasQueuedToast(controller.TempData, "معالج تعيين / تغيير المدير العام", "warning"),
            "general-manager transition redirect should warn the administrator about the replacement workflow"
        );
        Require(
            userAdminService.GetUserAccount(newGeneralManagerUsername) == null,
            "general-manager transition redirect should not create the replacement before approval"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUsersControllerManagerCreationRequiresGeneralManagerActor(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var departmentName = $"قسم صلاحية إنشاء مدير {Guid.NewGuid():N}";
        var departmentManagerUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        var generalManagerUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";

        using (var db = dbFactory.CreateDbContext())
        {
            db.Departments.Add(new Department { Name = departmentName, IsActive = true });
            SeedAdministrationSettings(db, string.Empty);
            db.SaveChanges();
        }

        var systemAdminPrincipal = BuildPrincipal(
            "system-admin-actor",
            AppRoles.SystemAdmin,
            AppPermissions.ManageUsers
        );

        var departmentManagerController = CreateUsersController(
            userAdminService,
            systemAdminPrincipal
        );
        var departmentManagerResult = departmentManagerController.Create(
            new UserEditViewModel
            {
                Username = departmentManagerUsername,
                FullName = "مدير قسم غير مصرح",
                Department = departmentName,
                JobTitle = "مدير قسم",
                PhoneNumber = "0503333311",
                IsActive = true,
                Role = AppRoles.DepartmentManager,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            departmentManagerResult is ViewResult { Model: UserEditViewModel },
            "system admin with user-management permission should stay on create view when attempting to create a department manager"
        );
        Require(
            departmentManagerController
                .ModelState[nameof(UserEditViewModel.Role)]
                ?.Errors.Any(error =>
                    error.ErrorMessage.Contains(
                        "مالك النظام أو المدير العام",
                        StringComparison.Ordinal
                    )
                ) == true,
            "department-manager creation denial should explain who is allowed to assign managers"
        );
        Require(
            userAdminService.GetUserAccount(departmentManagerUsername) == null,
            "unauthorized department-manager creation should not persist an account"
        );

        var generalManagerController = CreateUsersController(
            userAdminService,
            systemAdminPrincipal
        );
        var generalManagerResult = generalManagerController.Create(
            new UserEditViewModel
            {
                Username = generalManagerUsername,
                FullName = "مدير عام غير مصرح",
                Department = string.Empty,
                JobTitle = "مدير عام",
                PhoneNumber = "0503333312",
                IsActive = true,
                Role = AppRoles.GeneralManager,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            generalManagerResult is ViewResult { Model: UserEditViewModel },
            "system admin with user-management permission should stay on create view when attempting to create a general manager"
        );
        Require(
            generalManagerController
                .ModelState[nameof(UserEditViewModel.Role)]
                ?.Errors.Any(error =>
                    error.ErrorMessage.Contains("الأدوار الإدارية العليا", StringComparison.Ordinal)
                ) == true,
            "general-manager creation denial should explain that higher administrative roles belong to the system owner"
        );
        Require(
            userAdminService.GetUserAccount(generalManagerUsername) == null,
            "unauthorized general-manager creation should not persist an account"
        );

        return Task.CompletedTask;
    }

    private static async Task ScenarioUsersControllerEmailRemainsOptionalAndDoesNotFallbackToNumericFields(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var departmentName = $"قسم البريد {Guid.NewGuid():N}";
        var username = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
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
            "tester",
            AppRoles.GeneralManager,
            AppPermissions.ManageUsers
        );
        var createController = CreateUsersController(userAdminService, managerPrincipal);
        var createResult = createController.Create(
            new UserEditViewModel
            {
                Username = username,
                FullName = "مستخدم بلا بريد",
                Department = departmentName,
                JobTitle = "موظف",
                PhoneNumber = "0502222215",
                Email = string.Empty,
                IsActive = true,
                Role = AppRoles.Receptionist,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            createResult is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "user create without email should still succeed"
        );
        Require(
            string.IsNullOrWhiteSpace(userAdminService.GetUserAccount(username)?.Email),
            "user create without email should persist an empty email instead of a numeric fallback"
        );

        using (var db = dbFactory.CreateDbContext())
        {
            var user = db.UserAccounts.Single(item => item.Username == username);
            user.Email = user.PhoneNumber;
            db.SaveChanges();
        }

        var editGetController = CreateUsersController(userAdminService, managerPrincipal);
        var editGetResult = editGetController.Edit(username);
        var editModel = ((ViewResult)editGetResult).Model as UserEditViewModel;
        Require(
            editModel != null && string.IsNullOrWhiteSpace(editModel.Email),
            "edit view should not display phone, identity, or badge values as email"
        );

        var invalidEditController = CreateUsersController(userAdminService, managerPrincipal);
        var invalidEditResult = await invalidEditController.Edit(
            new UserEditViewModel
            {
                OriginalUsername = username,
                Username = username,
                FullName = "مستخدم بلا بريد",
                Department = departmentName,
                JobTitle = "موظف",
                PhoneNumber = "0502222215",
                Email = "0502222215",
                IsActive = true,
                Role = AppRoles.Receptionist,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            invalidEditResult is ViewResult
                && invalidEditController.ModelState[nameof(UserEditViewModel.Email)]?.Errors.Count
                    > 0,
            "edit should reject an entered numeric email value instead of silently saving it"
        );
    }

    private static Task ScenarioApplyRoleDefaultsDuringCreateStaysOnCreateFlow(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var departmentName = $"قسم صلاحيات الدور {Guid.NewGuid():N}";
        var username = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
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
            "tester",
            AppRoles.GeneralManager,
            AppPermissions.ManageUsers
        );
        var applyDefaultsController = CreateUsersController(userAdminService, managerPrincipal);
        var applyDefaultsResult = applyDefaultsController.ApplyRoleDefaults(
            new UserEditViewModel
            {
                Username = username,
                FullName = "حارس بوابة جديد",
                Department = departmentName,
                JobTitle = "أمن البوابة",
                PhoneNumber = "0503333311",
                IsActive = true,
                Role = AppRoles.GateSecurity,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            applyDefaultsResult
                is ViewResult
                {
                    ViewName: "Create",
                    Model: UserEditViewModel
                    {
                        Role: AppRoles.GateSecurity,
                        CanScanOperations: true,
                        OriginalUsername: null or ""
                    }
                },
            "applying role defaults while creating a user should keep the create form instead of switching to edit"
        );
        Require(
            userAdminService.GetUserAccount(username) == null,
            "applying role defaults during create should not persist the user before the final save"
        );

        var returnedModel = (UserEditViewModel)((ViewResult)applyDefaultsResult).Model!;
        var saveController = CreateUsersController(userAdminService, managerPrincipal);
        var saveResult = saveController.Create(returnedModel);
        Require(
            saveResult is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "saving after applying role defaults on create should use the normal create flow"
        );
        Require(
            userAdminService.GetUserAccount(username)?.CanScanOperations == true,
            "created gate security user should keep the role default scan permission"
        );

        return Task.CompletedTask;
    }

    private static async Task ScenarioUsersControllerLifecycleSupportsAuditSearchAndPrint(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        const string actorUsername = "tester";
        var departmentName = $"قسم مستخدمين {Guid.NewGuid():N}";
        var scenarioToken = Guid.NewGuid().ToString("N")[..6];
        var targetUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        var originalFullName = $"موظف اختبار {scenarioToken}";
        var updatedFullName = $"{originalFullName} محدث";
        const string originalPhoneNumber = "0501234567";
        const string updatedPhoneNumber = "0507654321";
        const string updatedJobTitle = "موظف استقبال أول";

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
            AppPermissions.ManageDepartments
        );

        var createController = CreateUsersController(userAdminService, managerPrincipal);
        var createResult = createController.Create(
            new UserEditViewModel
            {
                Username = targetUsername,
                FullName = originalFullName,
                Department = departmentName,
                JobTitle = "موظف استقبال",
                PhoneNumber = originalPhoneNumber,
                IsActive = true,
                Role = AppRoles.Receptionist,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
                AutoBindManager = false,
            }
        );

        Require(
            createResult is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "user create should redirect back to the users index"
        );
        Require(
            HasQueuedToast(createController.TempData, "تمت إضافة المستخدم بنجاح", "success"),
            "user create should publish a success message with the temporary credentials"
        );

        var createdUser =
            userAdminService.GetUserAccount(targetUsername)
            ?? throw new InvalidOperationException("created user account was not persisted");
        Require(createdUser.IsActive, "created user should start as active");
        Require(createdUser.MustChangePassword, "created user should require password change");
        Require(
            string.Equals(createdUser.FullName, originalFullName, StringComparison.Ordinal),
            "created user should persist the submitted full name"
        );
        var createdTemporaryPassword = ExtractTemporaryPassword(createController.TempData);
        Require(
            !string.IsNullOrWhiteSpace(createdTemporaryPassword),
            "user create should publish the generated temporary password"
        );
        Require(
            string.Equals(
                ExtractCredentialNoticeValue(
                    createController.TempData,
                    "__CredentialNoticeTemporaryPassword"
                ),
                createdTemporaryPassword,
                StringComparison.Ordinal
            ),
            "user create should store the one-time credential notice password"
        );
        Require(
            string.Equals(
                ExtractCredentialNoticeValue(
                    createController.TempData,
                    "__CredentialNoticeUsername"
                ),
                targetUsername,
                StringComparison.Ordinal
            ),
            "user create should store the one-time credential notice username"
        );
        Require(
            userAdminService.ValidateCredentials(
                targetUsername,
                createdTemporaryPassword!,
                out var createdDisplayName,
                out var createdRole
            ),
            "created user should accept the generated temporary password"
        );
        Require(
            string.Equals(createdDisplayName, originalFullName, StringComparison.Ordinal),
            "credential validation should return the created display name"
        );
        Require(
            string.Equals(createdRole, AppRoles.Receptionist, StringComparison.Ordinal),
            "created user should keep the receptionist role"
        );

        var editController = CreateUsersController(userAdminService, managerPrincipal);
        var editResult = await editController.Edit(
            new UserEditViewModel
            {
                OriginalUsername = targetUsername,
                Username = targetUsername,
                FullName = updatedFullName,
                Department = departmentName,
                JobTitle = updatedJobTitle,
                PhoneNumber = updatedPhoneNumber,
                IsActive = true,
                Role = AppRoles.Receptionist,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
                AutoBindManager = false,
            }
        );

        Require(
            editResult is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "user edit should redirect back to the users index"
        );

        var updatedUser =
            userAdminService.GetUserAccount(targetUsername)
            ?? throw new InvalidOperationException("updated user account was not found");
        Require(
            string.Equals(updatedUser.FullName, updatedFullName, StringComparison.Ordinal),
            "user edit should persist the updated full name"
        );
        Require(
            string.Equals(updatedUser.PhoneNumber, updatedPhoneNumber, StringComparison.Ordinal),
            "user edit should persist the updated phone number"
        );
        Require(
            string.Equals(updatedUser.JobTitle, updatedJobTitle, StringComparison.Ordinal),
            "user edit should persist the updated job title"
        );

        var deactivateController = CreateUsersController(userAdminService, managerPrincipal);
        var deactivateResult = deactivateController.Deactivate(targetUsername);

        Require(
            deactivateResult
                is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "user deactivate should redirect back to the users index"
        );

        var deactivatedUser =
            userAdminService.GetUserAccount(targetUsername)
            ?? throw new InvalidOperationException("deactivated user account was not found");
        Require(!deactivatedUser.IsActive, "user deactivate should mark the account inactive");
        Require(
            !userAdminService.ValidateCredentials(targetUsername, "123456", out _, out _),
            "inactive user should fail credential validation"
        );
        Require(
            HasQueuedToast(deactivateController.TempData, "تم إيقاف المستخدم بنجاح.", "success"),
            "user deactivate should publish the stop confirmation message"
        );

        var searchController = CreateUsersController(userAdminService, managerPrincipal);
        var searchResult = searchController.ActivityLog(
            query: targetUsername,
            username: targetUsername,
            page: 1
        );

        var searchView = searchResult as ViewResult;
        Require(
            searchView?.Model is UserActivityReportViewModel,
            "authorized managers should be able to open the user activity log"
        );
        var searchModel = (UserActivityReportViewModel)searchView!.Model!;
        Require(
            searchModel.TotalCount >= 3,
            "activity log search should find create, update, and deactivate entries for the target user"
        );
        Require(
            searchModel.Activities.All(activity =>
                string.Equals(activity.Username, targetUsername, StringComparison.OrdinalIgnoreCase)
            ),
            "activity log username filter should keep only the target user rows"
        );

        var visibleActivityTypes = searchModel
            .Activities.Select(activity => activity.ActionType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(
            visibleActivityTypes.Contains("Create")
                && visibleActivityTypes.Contains("Update")
                && visibleActivityTypes.Contains("Deactivate"),
            "activity log search should surface the create, update, and deactivate records"
        );

        var filteredResult = searchController.ActivityLog(
            query: targetUsername,
            actionType: "Deactivate",
            username: targetUsername,
            page: 1
        );

        var filteredView = filteredResult as ViewResult;
        Require(
            filteredView?.Model is UserActivityReportViewModel,
            "activity log filter should render the report view"
        );
        var filteredModel = (UserActivityReportViewModel)filteredView!.Model!;
        Require(
            filteredModel.TotalCount == 1,
            "activity log action filter should isolate the deactivation entry"
        );
        Require(
            filteredModel.Activities.Count == 1
                && string.Equals(
                    filteredModel.Activities[0].ActionType,
                    "Deactivate",
                    StringComparison.OrdinalIgnoreCase
                ),
            "deactivation filter should return only the deactivation record"
        );

        var printController = CreateUsersController(userAdminService, managerPrincipal);
        var printResult = printController.PrintActivityLog(
            query: targetUsername,
            username: targetUsername
        );

        var printFile = printResult as FileContentResult;
        Require(
            printFile != null,
            "activity log print should return a PDF file for authorized managers"
        );
        Require(
            string.Equals(
                printFile!.ContentType,
                "application/pdf",
                StringComparison.OrdinalIgnoreCase
            ),
            "activity log print should use the PDF content type"
        );
        Require(printFile.FileContents.Length > 0, "activity log print should generate PDF bytes");
        Require(
            string.Equals(
                printFile.FileDownloadName,
                "UserActivityLog.pdf",
                StringComparison.Ordinal
            ),
            "activity log print should use the expected download name"
        );

        var activateController = CreateUsersController(userAdminService, managerPrincipal);
        var activateResult = activateController.Activate(targetUsername);

        Require(
            activateResult is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "user activate should redirect back to the users index"
        );

        var reactivatedUser =
            userAdminService.GetUserAccount(targetUsername)
            ?? throw new InvalidOperationException("reactivated user account was not found");
        Require(reactivatedUser.IsActive, "user activate should mark the account active again");
        Require(
            reactivatedUser.MustChangePassword,
            "reactivated user should require password change after reset"
        );
        var reactivationTemporaryPassword = ExtractTemporaryPassword(activateController.TempData);
        Require(
            !string.IsNullOrWhiteSpace(reactivationTemporaryPassword),
            "user activate should publish the generated temporary password"
        );
        Require(
            userAdminService.ValidateCredentials(
                targetUsername,
                reactivationTemporaryPassword!,
                out var reactivatedDisplayName,
                out _
            ),
            "reactivated user should accept the generated reset temporary password"
        );
        Require(
            string.Equals(reactivatedDisplayName, updatedFullName, StringComparison.Ordinal),
            "reactivated user should keep the updated display name"
        );
        Require(
            HasQueuedToast(activateController.TempData, "تم تفعيل المستخدم بنجاح", "success"),
            "user activate should publish the reactivation message"
        );

        var sessionBeforePasswordReset = userAdminService.CreateSession(targetUsername);
        Require(
            userAdminService.ValidateSession(sessionBeforePasswordReset, out _),
            "user session should be active before the password reset"
        );

        var resetPasswordController = CreateUsersController(userAdminService, managerPrincipal);
        var resetPasswordResult = resetPasswordController.ResetTemporaryPassword(targetUsername);
        Require(
            resetPasswordResult
                is RedirectToActionResult
                {
                    ActionName: nameof(UsersController.Edit),
                    RouteValues: not null,
                },
            "temporary password reset should redirect back to the user edit screen"
        );
        var resetTemporaryPassword = ExtractTemporaryPassword(resetPasswordController.TempData);
        Require(
            !string.IsNullOrWhiteSpace(resetTemporaryPassword),
            "temporary password reset should publish the generated temporary password"
        );
        Require(
            userAdminService.ValidateCredentials(
                targetUsername,
                resetTemporaryPassword!,
                out var resetDisplayName,
                out _
            ),
            "temporary password reset should accept the generated temporary password"
        );
        Require(
            string.Equals(resetDisplayName, updatedFullName, StringComparison.Ordinal),
            "temporary password reset should keep the user display name intact"
        );
        Require(
            HasQueuedToast(
                resetPasswordController.TempData,
                "تمت إعادة تعيين كلمة المرور وإغلاق جلسات المستخدم القديمة",
                "success"
            ),
            "temporary password reset should publish a safe success message"
        );
        Require(
            !HasQueuedToast(resetPasswordController.TempData, resetTemporaryPassword!, "success"),
            "temporary password reset should not leak the temporary password in a toast"
        );
        Require(
            !userAdminService.ValidateSession(sessionBeforePasswordReset, out _),
            "temporary password reset should invalidate the user's active sessions"
        );

        if (OperatingSystem.IsWindows())
        {
            var userBadgeController = CreateUsersController(userAdminService, managerPrincipal);
            var userBadgeResult = userBadgeController.UserBadge(targetUsername);
            var userBadgeFile = userBadgeResult as FileContentResult;
            Require(userBadgeFile != null, "user badge endpoint should return an image file");
            Require(
                string.Equals(
                    userBadgeFile!.ContentType,
                    "image/png",
                    StringComparison.OrdinalIgnoreCase
                ),
                "user badge endpoint should return PNG content"
            );
            Require(
                userBadgeFile.FileContents.Length > 0,
                "user badge endpoint should contain image bytes"
            );
        }

        var printUserBadgeController = CreateUsersController(userAdminService, managerPrincipal);
        var printUserBadgeResult = printUserBadgeController.PrintUserBadge(targetUsername);
        Require(
            printUserBadgeResult is ViewResult { Model: UserAccount },
            "user badge print should render the employee badge view"
        );

        var gateOperatorUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        var gateOperatorName = $"مشغل بوابة {scenarioToken}";
        var gateCreateController = CreateUsersController(userAdminService, managerPrincipal);
        var gateCreateResult = gateCreateController.Create(
            new UserEditViewModel
            {
                Username = gateOperatorUsername,
                FullName = gateOperatorName,
                Department = departmentName,
                JobTitle = "مشغل بوابة",
                PhoneNumber = "0501112233",
                IsActive = true,
                Role = AppRoles.GateSecurity,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
                AutoBindManager = false,
            }
        );
        Require(
            gateCreateResult
                is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "gate operator create should redirect back to the users index"
        );

        var gateOperator =
            userAdminService.GetUserAccount(gateOperatorUsername)
            ?? throw new InvalidOperationException("gate operator account was not persisted");
        Require(gateOperator.CanScanOperations, "gate operator should receive scan permissions");
        Require(
            !string.IsNullOrWhiteSpace(gateOperator.OperatorBadgeCode),
            "gate operator should receive an operator badge code"
        );

        var gateOperatorWithResetPin =
            userAdminService.GetUserAccount(gateOperatorUsername)
            ?? throw new InvalidOperationException(
                "gate operator account was not found after PIN reset"
            );
        var operatorPinHashBeforeBlankEdit = gateOperatorWithResetPin.OperatorPinHash;
        var operatorPinSaltBeforeBlankEdit = gateOperatorWithResetPin.OperatorPinSalt;
        var editedGateOperatorName = $"{gateOperatorName} محدث";
        var editGateOperatorController = CreateUsersController(userAdminService, managerPrincipal);
        var editGateOperatorResult = await editGateOperatorController.Edit(
            new UserEditViewModel
            {
                OriginalUsername = gateOperatorUsername,
                Username = gateOperatorUsername,
                FullName = editedGateOperatorName,
                Department = departmentName,
                JobTitle = "مشغل بوابة أول",
                OperatorBadgeCode = gateOperatorWithResetPin.OperatorBadgeCode,
                PhoneNumber = "0501112244",
                IsActive = true,
                Role = AppRoles.GateSecurity,
                ApplyRoleDefaults = true,
                TemporaryOperatorPin = string.Empty,
                MustChangeOperatorPin = true,
                AutoBindManager = false,
            }
        );
        Require(
            editGateOperatorResult
                is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "gate operator edit with a blank PIN should redirect back to the users index"
        );

        var gateOperatorAfterBlankPinEdit =
            userAdminService.GetUserAccount(gateOperatorUsername)
            ?? throw new InvalidOperationException(
                "gate operator account was not found after blank PIN edit"
            );
        Require(
            string.Equals(
                gateOperatorAfterBlankPinEdit.OperatorPinHash,
                operatorPinHashBeforeBlankEdit,
                StringComparison.Ordinal
            )
                && string.Equals(
                    gateOperatorAfterBlankPinEdit.OperatorPinSalt,
                    operatorPinSaltBeforeBlankEdit,
                    StringComparison.Ordinal
                ),
            "gate operator edit should keep operator PIN credentials removed"
        );
        Require(
            string.Equals(
                gateOperatorAfterBlankPinEdit.FullName,
                editedGateOperatorName,
                StringComparison.Ordinal
            ),
            "gate operator edit with a blank PIN should still persist other user changes"
        );
        Require(
            string.IsNullOrWhiteSpace(gateOperatorAfterBlankPinEdit.OperatorPinHash)
                && string.IsNullOrWhiteSpace(gateOperatorAfterBlankPinEdit.OperatorPinSalt)
                && !gateOperatorAfterBlankPinEdit.MustChangeOperatorPin,
            "gate operator should use the account session without separate PIN credentials"
        );

        if (OperatingSystem.IsWindows())
        {
            var operatorBadgeController = CreateUsersController(userAdminService, managerPrincipal);
            var operatorBadgeResult = operatorBadgeController.OperatorBadge(gateOperatorUsername);
            var operatorBadgeFile = operatorBadgeResult as FileContentResult;
            Require(
                operatorBadgeFile != null,
                "operator badge endpoint should return an image file"
            );
            Require(
                string.Equals(
                    operatorBadgeFile!.ContentType,
                    "image/png",
                    StringComparison.OrdinalIgnoreCase
                ),
                "operator badge endpoint should return PNG content"
            );
            Require(
                operatorBadgeFile.FileContents.Length > 0,
                "operator badge endpoint should contain image bytes"
            );
        }

        var printOperatorBadgeController = CreateUsersController(
            userAdminService,
            managerPrincipal
        );
        var printOperatorBadgeResult = printOperatorBadgeController.PrintOperatorBadge(
            gateOperatorUsername
        );
        Require(
            printOperatorBadgeResult is ViewResult { Model: UserAccount },
            "operator badge print should render the operator badge view"
        );

        var activatedFilterController = CreateUsersController(userAdminService, managerPrincipal);
        var activatedFilterResult = activatedFilterController.ActivityLog(
            query: targetUsername,
            actionType: "Activate",
            username: targetUsername,
            page: 1
        );

        var activatedView = activatedFilterResult as ViewResult;
        Require(
            activatedView?.Model is UserActivityReportViewModel,
            "activation filter should render the report view"
        );
        var activatedModel = (UserActivityReportViewModel)activatedView!.Model!;
        Require(
            activatedModel.TotalCount == 1,
            "activity log action filter should isolate the reactivation entry"
        );
        Require(
            activatedModel.Activities.Count == 1
                && string.Equals(
                    activatedModel.Activities[0].ActionType,
                    "Activate",
                    StringComparison.OrdinalIgnoreCase
                ),
            "activation filter should return only the activation record"
        );

        var unauthorizedController = CreateUsersController(
            userAdminService,
            BuildPrincipal(targetUsername, AppRoles.Receptionist)
        );
        var deniedLogResult = unauthorizedController.ActivityLog(username: targetUsername);
        Require(
            deniedLogResult
                is RedirectToActionResult { ActionName: "AccessDenied", ControllerName: "Home" },
            "receptionist should be blocked from opening the user activity log"
        );

        var deniedPrintResult = unauthorizedController.PrintActivityLog(username: targetUsername);
        Require(
            deniedPrintResult
                is RedirectToActionResult { ActionName: "AccessDenied", ControllerName: "Home" },
            "receptionist should be blocked from printing the user activity log"
        );
    }

    private static Task ScenarioUsersControllerWizardIdentityLookupAndReactivateFlow(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        const string actorUsername = "tester";
        var departmentName = $"قسم معالج المستخدمين {Guid.NewGuid():N}";
        var activeUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        var inactiveUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        var missingUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";

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

        var activeUser = new UserAccount
        {
            Username = activeUsername,
            DisplayName = "مستخدم نشط",
            FullName = "مستخدم نشط",
            Department = departmentName,
            JobTitle = "موظف",
            PhoneNumber = "0507000001",
            Role = AppRoles.Employee,
            IsActive = true,
        };
        AppPermissions.ApplyRoleDefaults(activeUser);
        Require(
            userAdminService.CreateUser(activeUser, "OnlineTest2026!"),
            "wizard lookup scenario should create the active user"
        );

        var inactiveUser = new UserAccount
        {
            Username = inactiveUsername,
            DisplayName = "مستخدم موقوف",
            FullName = "مستخدم موقوف",
            Department = departmentName,
            JobTitle = "موظف",
            PhoneNumber = "0507000002",
            Role = AppRoles.Employee,
            IsActive = true,
        };
        AppPermissions.ApplyRoleDefaults(inactiveUser);
        Require(
            userAdminService.CreateUser(inactiveUser, "OnlineTest2026!"),
            "wizard lookup scenario should create the inactive user"
        );
        Require(
            userAdminService.SetUserActiveStatus(inactiveUsername, false) != null,
            "wizard lookup scenario should be able to stop the inactive user before testing reactivate"
        );

        var managerPrincipal = BuildPrincipal(
            actorUsername,
            AppRoles.GeneralManager,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments
        );

        static System.Text.Json.JsonElement ReadPayload(JsonResult result)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(result.Value)
            );
            return doc.RootElement.Clone();
        }

        var lookupController = CreateUsersController(userAdminService, managerPrincipal);

        var invalidLookup = lookupController.WizardIdentityLookup("123") as JsonResult;
        Require(invalidLookup != null, "wizard lookup should return JSON for invalid usernames");
        var invalidPayload = ReadPayload(invalidLookup!);
        Require(
            string.Equals(
                invalidPayload.GetProperty("status").GetString(),
                "invalid",
                StringComparison.OrdinalIgnoreCase
            ),
            "wizard lookup should reject short usernames"
        );

        var activeLookup = lookupController.WizardIdentityLookup(activeUsername) as JsonResult;
        Require(activeLookup != null, "wizard lookup should return JSON for active users");
        var activePayload = ReadPayload(activeLookup!);
        Require(
            string.Equals(
                activePayload.GetProperty("status").GetString(),
                "active",
                StringComparison.OrdinalIgnoreCase
            ),
            "wizard lookup should report active accounts"
        );
        Require(
            activePayload.GetProperty("editUrl").GetString() is string activeEditUrl
                && activeEditUrl.Contains("/Users/Edit", StringComparison.OrdinalIgnoreCase)
                && activeEditUrl.Contains(activeUsername, StringComparison.OrdinalIgnoreCase),
            "wizard lookup should point active users to the edit screen"
        );
        Require(
            activePayload.GetProperty("pendingPermitsCount").GetInt32() == 0,
            "wizard lookup should report zero pending permits when no permit service data exists"
        );

        var inactiveLookup = lookupController.WizardIdentityLookup(inactiveUsername) as JsonResult;
        Require(inactiveLookup != null, "wizard lookup should return JSON for inactive users");
        var inactivePayload = ReadPayload(inactiveLookup!);
        Require(
            string.Equals(
                inactivePayload.GetProperty("status").GetString(),
                "inactive",
                StringComparison.OrdinalIgnoreCase
            ),
            "wizard lookup should report inactive accounts"
        );
        Require(
            !string.IsNullOrWhiteSpace(inactivePayload.GetProperty("reactivateUrl").GetString()),
            "wizard lookup should expose a reactivation endpoint for inactive users"
        );

        var missingLookup = lookupController.WizardIdentityLookup(missingUsername) as JsonResult;
        Require(missingLookup != null, "wizard lookup should return JSON for missing users");
        var missingPayload = ReadPayload(missingLookup!);
        Require(
            string.Equals(
                missingPayload.GetProperty("status").GetString(),
                "not_found",
                StringComparison.OrdinalIgnoreCase
            ),
            "wizard lookup should report missing accounts"
        );

        var reactivateController = CreateUsersController(userAdminService, managerPrincipal);
        var reactivateResult =
            reactivateController.WizardReactivate(inactiveUsername) as JsonResult;
        Require(reactivateResult != null, "wizard reactivate should return JSON");
        var reactivatePayload = ReadPayload(reactivateResult!);
        Require(
            string.Equals(
                reactivatePayload.GetProperty("status").GetString(),
                "reactivated",
                StringComparison.OrdinalIgnoreCase
            ),
            "wizard reactivate should report a successful reactivation"
        );
        var temporaryPassword = reactivatePayload.GetProperty("temporaryPassword").GetString();
        Require(
            !string.IsNullOrWhiteSpace(temporaryPassword),
            "wizard reactivate should return the temporary password"
        );
        Require(
            userAdminService.GetUserAccount(inactiveUsername)?.IsActive == true,
            "wizard reactivate should reactivate the stored user"
        );
        Require(
            userAdminService.ValidateCredentials(
                inactiveUsername,
                temporaryPassword!,
                out var reactivatedDisplayName,
                out _
            ),
            "wizard reactivate should issue a working temporary password"
        );
        Require(
            string.Equals(reactivatedDisplayName, "مستخدم موقوف", StringComparison.Ordinal),
            "wizard reactivate should preserve the user display name"
        );

        return Task.CompletedTask;
    }

    private static async Task ScenarioManualUserEditPersistsPermissionsAndPassword(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        const string actorUsername = "tester";
        const string replacementPassword = "OnlineTest2026!";
        var departmentName = $"قسم تحرير يدوي {Guid.NewGuid():N}";
        var targetUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";

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
            AppRoles.SystemAdmin,
            true,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments,
            AppPermissions.ManageAdministration,
            AppPermissions.ManageDelegations
        );

        var createController = CreateUsersController(userAdminService, managerPrincipal);
        var createResult = createController.Create(
            new UserEditViewModel
            {
                Username = targetUsername,
                FullName = "مستخدم تحرير يدوي",
                Department = departmentName,
                JobTitle = "موظف",
                PhoneNumber = "0501111111",
                IsActive = true,
                Role = AppRoles.Employee,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            createResult is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "manual edit scenario should create the initial user"
        );

        var editController = CreateUsersController(userAdminService, managerPrincipal);
        var editResult = await editController.Edit(
            new UserEditViewModel
            {
                OriginalUsername = targetUsername,
                Username = targetUsername,
                FullName = "مستخدم تحرير يدوي محدث",
                Department = departmentName,
                JobTitle = "منسق تشغيل",
                PhoneNumber = "0502222222",
                IsActive = true,
                Role = AppRoles.Employee,
                ApplyRoleDefaults = false,
                NewPassword = replacementPassword,
                CanViewDashboard = true,
                CanViewPermits = true,
                CanCreatePermit = true,
                CanViewVisits = true,
                CanCreateVisit = false,
                CanScanOperations = false,
                CanManageUsers = true,
                CanManageDepartments = true,
                CanManageAdministration = true,
                CanManageDelegations = true,
                MustChangeOperatorPin = false,
            }
        );

        Require(
            editResult is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "manual edit should redirect back to the users index"
        );

        var updatedUser =
            userAdminService.GetUserAccount(targetUsername)
            ?? throw new InvalidOperationException("manual edit user was not found after update");
        Require(updatedUser.CanViewDashboard, "manual edit should persist dashboard access");
        Require(updatedUser.CanViewPermits, "manual edit should persist permit access");
        Require(updatedUser.CanCreatePermit, "manual edit should persist permit creation access");
        Require(
            !updatedUser.CanCreateVisit,
            "manual edit should persist removed visit creation access"
        );
        Require(!updatedUser.CanScanOperations, "manual edit should persist removed scan access");
        Require(updatedUser.CanManageUsers, "manual edit should persist user management access");
        Require(
            updatedUser.CanManageDepartments,
            "manual edit should persist department management access"
        );
        Require(
            updatedUser.CanManageAdministration,
            "manual edit should persist administration management access"
        );
        Require(
            updatedUser.CanManageDelegations,
            "manual edit should persist delegation management access"
        );
        Require(
            !userAdminService.ValidateCredentials(targetUsername, "123456", out _, out _),
            "manual edit should invalidate the old temporary password"
        );
        Require(
            userAdminService.ValidateCredentials(targetUsername, replacementPassword, out _, out _),
            "manual edit should accept the replacement password"
        );
    }

    private static async Task ScenarioGeneralManagerAndSystemAdminPersistManageDelegationsPermission(
        IUserAdminService userAdminService
    )
    {
        var actorPrincipal = BuildPrincipal(
            "tester",
            AppRoles.SystemAdmin,
            true,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments,
            AppPermissions.ManageAdministration,
            AppPermissions.ManageDelegations
        );

        async Task VerifyRoleAsync(string username, string role, string phoneNumber)
        {
            var seededUser = new UserAccount
            {
                Username = username,
                DisplayName = username,
                FullName = username,
                Department = "قسم تجريبي",
                JobTitle = "موظف",
                PhoneNumber = phoneNumber,
                Role = AppRoles.Employee,
                IsActive = true,
            };
            AppPermissions.ApplyRoleDefaults(seededUser);

            var created = userAdminService.CreateUser(seededUser, "OnlineTest2026!");
            Require(created, $"{role} persistence scenario should create the seed user");

            var applyDefaultsController = CreateUsersController(userAdminService, actorPrincipal);
            var applyDefaultsResult = applyDefaultsController.ApplyRoleDefaults(
                new UserEditViewModel
                {
                    Username = username,
                    OriginalUsername = username,
                    FullName = username,
                    Department = role == AppRoles.GeneralManager ? string.Empty : "قسم تجريبي",
                    JobTitle = role == AppRoles.GeneralManager ? "مدير عام" : "مشرف النظام",
                    PhoneNumber = phoneNumber,
                    Role = role,
                    ApplyRoleDefaults = true,
                    IsActive = true,
                }
            );

            Require(
                applyDefaultsResult is ViewResult { Model: UserEditViewModel roleModel }
                    && roleModel.CanManageDelegations,
                $"apply role defaults should expose ManageDelegations for {role}"
            );

            var editController = CreateUsersController(userAdminService, actorPrincipal);
            var editResult = await editController.Edit(
                new UserEditViewModel
                {
                    Username = username,
                    OriginalUsername = username,
                    FullName = username + " محدث",
                    Department = role == AppRoles.GeneralManager ? string.Empty : "قسم تجريبي",
                    JobTitle = role == AppRoles.GeneralManager ? "مدير عام" : "مشرف النظام",
                    PhoneNumber = phoneNumber,
                    Role = role,
                    ApplyRoleDefaults = true,
                    IsActive = true,
                    MustChangeOperatorPin = false,
                }
            );

            Require(
                editResult is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
                $"editing {role} should redirect back to the users index"
            );

            var updatedUser =
                userAdminService.GetUserAccount(username)
                ?? throw new InvalidOperationException($"{role} user was not found after update");
            Require(
                updatedUser.CanManageDelegations,
                $"editing {role} should persist the ManageDelegations permission"
            );

            var getEditController = CreateUsersController(userAdminService, actorPrincipal);
            var getEditResult = getEditController.Edit(username);
            Require(
                getEditResult is ViewResult { Model: UserEditViewModel persistedModel }
                    && persistedModel.CanManageDelegations,
                $"editing screen should load persisted ManageDelegations for {role}"
            );
        }

        await VerifyRoleAsync(
            $"gm-delegation-{Guid.NewGuid():N}",
            AppRoles.GeneralManager,
            "0506111111"
        );
        await VerifyRoleAsync(
            $"admin-delegation-{Guid.NewGuid():N}",
            AppRoles.SystemAdmin,
            "0506222222"
        );
    }

    private static Task ScenarioUserSessionInvalidatesAfterAccountDeactivation(
        IUserAdminService userAdminService
    )
    {
        var username = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        var user = new UserAccount
        {
            Username = username,
            DisplayName = "مستخدم جلسة معطلة",
            FullName = "مستخدم جلسة معطلة",
            Department = "قسم الجلسات",
            JobTitle = "موظف",
            PhoneNumber = "0507444444",
            Email = string.Empty,
            Role = AppRoles.Employee,
            IsActive = true,
        };
        AppPermissions.ApplyRoleDefaults(user);

        Require(
            userAdminService.CreateUser(user, "Session2026!"),
            "session invalidation scenario should create an active user"
        );

        var sessionId = userAdminService.CreateSession(username);
        Require(
            userAdminService.ValidateSession(sessionId, out var activeSessionUsername)
                && string.Equals(
                    activeSessionUsername,
                    username,
                    StringComparison.OrdinalIgnoreCase
                ),
            "fresh session should validate while the account is active"
        );

        Require(
            userAdminService.SetUserActiveStatus(username, false) != null,
            "session invalidation scenario should deactivate the user"
        );
        Require(
            !userAdminService.ValidateSession(sessionId, out _),
            "existing session should fail immediately after the account is deactivated"
        );
        Require(
            !userAdminService.ValidateSession(sessionId, out _),
            "invalidated session record should be removed after the first failed validation"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUsersIndexUsesUnifiedActionMenuAndMobileCards()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
        );
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "Views", "Users", "Index.cshtml"));
        var styles = File.ReadAllText(
            Path.Combine(repositoryRoot, "wwwroot", "css", "users-pages.css")
        );

        Require(
            view.Contains("users-icon-action-tooltip", StringComparison.Ordinal)
                && view.Contains("users-row-dropdown", StringComparison.Ordinal)
                && view.Contains("users-action-dropdown", StringComparison.Ordinal)
                && view.Contains("data-bs-toggle=\"tooltip\"", StringComparison.Ordinal),
            "users index should expose compact icon actions with hover tooltips and keep extra actions in a menu"
        );
        Require(
            view.Contains("PrintUserBadge", StringComparison.Ordinal)
                && view.Contains("PrintOperatorBadge", StringComparison.Ordinal)
                && view.Contains("Deactivate", StringComparison.Ordinal)
                && view.Contains("Activate", StringComparison.Ordinal),
            "users index icon action row should preserve print, disable, and activate commands"
        );
        Require(
            styles.Contains(".users-index-table thead", StringComparison.Ordinal)
                && styles.Contains(".users-index-table tbody tr", StringComparison.Ordinal)
                && styles.Contains("content: attr(data-label)", StringComparison.Ordinal),
            "users index table should become labeled cards on mobile screens"
        );
        Require(
            styles.Contains(
                "[data-theme=\"dark\"] .users-action-dropdown",
                StringComparison.Ordinal
            )
                && styles.Contains(
                    "[data-theme=\"dark\"] .users-icon-action",
                    StringComparison.Ordinal
                ),
            "users index action menu should define dark-mode styling"
        );

        return Task.CompletedTask;
    }

    private static async Task ScenarioLegacyAdminUsernameCanBeEditedAndUsernameTamperingIsBlocked(
        IUserAdminService userAdminService
    )
    {
        const string actorUsername = "tester";
        const string legacyUsername = "admin";
        const string originalPassword = "LegacyAdmin2026!";
        const string replacementPassword = "Replacement2026!";

        var legacyAdmin = new UserAccount
        {
            Username = legacyUsername,
            DisplayName = "مدير النظام",
            FullName = "مدير النظام",
            Department = "الإدارة الرئيسية",
            JobTitle = "مشرف النظام",
            PhoneNumber = "0503333333",
            Role = AppRoles.SystemAdmin,
            IsActive = true,
        };
        AppPermissions.ApplyRoleDefaults(legacyAdmin);

        var created = userAdminService.CreateUser(legacyAdmin, originalPassword);
        Require(created, "legacy admin scenario should create the admin-style account");

        var managerPrincipal = BuildPrincipal(
            actorUsername,
            AppRoles.SystemAdmin,
            true,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments,
            AppPermissions.ManageAdministration,
            AppPermissions.ManageDelegations
        );

        var editController = CreateUsersController(userAdminService, managerPrincipal);
        var editResult = await editController.Edit(
            new UserEditViewModel
            {
                OriginalUsername = legacyUsername,
                Username = legacyUsername,
                FullName = "مدير النظام المحدث",
                Department = "الإدارة الرئيسية",
                JobTitle = "مشرف النظام الأول",
                PhoneNumber = "0504444444",
                IsActive = true,
                Role = AppRoles.SystemAdmin,
                ApplyRoleDefaults = false,
                NewPassword = replacementPassword,
                CanViewDashboard = true,
                CanViewPermits = true,
                CanViewVisitorPermits = true,
                CanCreatePermit = true,
                CanCreateVisitorPermit = true,
                CanEditPermit = true,
                CanEditVisitorPermit = true,
                CanApprovePermit = true,
                CanApproveLeaveRequest = true,
                CanStopPermit = true,
                CanReviewUnauthorizedExit = true,
                CanViewVisits = true,
                CanCreateVisit = true,
                CanEditVisit = true,
                CanApproveDetainedVisit = true,
                CanApproveVisits = true,
                CanScanOperations = false,
                CanViewDisplays = true,
                CanManageUsers = false,
                CanManageDepartments = true,
                CanManageAdministration = true,
                CanManageDelegations = true,
                MustChangeOperatorPin = false,
            }
        );

        Require(
            editResult is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "legacy admin edit should succeed without numeric username validation"
        );

        var updatedLegacyAdmin =
            userAdminService.GetUserAccount(legacyUsername)
            ?? throw new InvalidOperationException("legacy admin user was not found after edit");
        Require(
            string.Equals(
                updatedLegacyAdmin.FullName,
                "مدير النظام المحدث",
                StringComparison.Ordinal
            ),
            "legacy admin edit should persist the updated full name"
        );
        Require(
            !updatedLegacyAdmin.CanManageUsers,
            "legacy admin edit should persist removed manage-users permission"
        );
        Require(
            updatedLegacyAdmin.CanManageAdministration,
            "legacy admin edit should persist administration permission"
        );
        Require(
            updatedLegacyAdmin.CanManageDelegations,
            "legacy admin edit should persist delegation management permission"
        );
        Require(
            !userAdminService.ValidateCredentials(legacyUsername, originalPassword, out _, out _),
            "legacy admin edit should invalidate the previous password"
        );
        Require(
            userAdminService.ValidateCredentials(legacyUsername, replacementPassword, out _, out _),
            "legacy admin edit should accept the replacement password"
        );

        var tamperController = CreateUsersController(userAdminService, managerPrincipal);
        var tamperResult = await tamperController.Edit(
            new UserEditViewModel
            {
                OriginalUsername = legacyUsername,
                Username = "1234567890",
                FullName = "محاولة تغيير اسم المستخدم",
                Department = "الإدارة الرئيسية",
                JobTitle = "مشرف النظام الأول",
                PhoneNumber = "0504444444",
                IsActive = true,
                Role = AppRoles.SystemAdmin,
                ApplyRoleDefaults = false,
                MustChangeOperatorPin = false,
            }
        );

        Require(
            tamperResult is ViewResult { Model: UserEditViewModel },
            "username tampering should return the edit view"
        );
        Require(
            tamperController
                .ModelState[nameof(UserEditViewModel.Username)]
                ?.Errors.Any(error =>
                    error.ErrorMessage.Contains(
                        "لا يمكن تعديل اسم المستخدم",
                        StringComparison.Ordinal
                    )
                ) == true,
            "username tampering should be rejected with the immutability validation message"
        );
    }

    private static async Task ScenarioGeneralManagerCannotEscalateUsersToAdministrativeRolesOrPermissions(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var departmentName = $"قسم منع التصعيد {Guid.NewGuid():N}";
        var privilegedTargetUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";
        var editableUsername = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";

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

        var generalManagerPrincipal = BuildPrincipal(
            "general-manager-actor",
            AppRoles.GeneralManager,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments
        );

        var createController = CreateUsersController(userAdminService, generalManagerPrincipal);
        var createResult = createController.Create(
            new UserEditViewModel
            {
                Username = privilegedTargetUsername,
                FullName = "مشرف نظام غير مصرح",
                Department = departmentName,
                JobTitle = "مشرف نظام",
                PhoneNumber = "0505111101",
                IsActive = true,
                Role = AppRoles.SystemAdmin,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
            }
        );

        Require(
            createResult is ViewResult { Model: UserEditViewModel },
            "general manager should stay on create view when attempting to create a privileged administrative account"
        );
        Require(
            createController
                .ModelState[nameof(UserEditViewModel.Role)]
                ?.Errors.Any(error =>
                    error.ErrorMessage.Contains("الأدوار الإدارية العليا", StringComparison.Ordinal)
                ) == true,
            "privileged-role denial should explain that only the system owner may assign higher administrative roles"
        );
        Require(
            userAdminService.GetUserAccount(privilegedTargetUsername) == null,
            "blocked privileged-role creation should not persist an account"
        );

        var editableUser = new UserAccount
        {
            Username = editableUsername,
            DisplayName = "مستخدم قابل للتعديل",
            FullName = "مستخدم قابل للتعديل",
            Department = departmentName,
            JobTitle = "موظف",
            PhoneNumber = "0505111102",
            Role = AppRoles.Employee,
            IsActive = true,
        };
        AppPermissions.ApplyRoleDefaults(editableUser);

        Require(
            userAdminService.CreateUser(editableUser, "OnlineTest2026!"),
            "privilege-escalation scenario should create the editable seed user"
        );

        var editController = CreateUsersController(userAdminService, generalManagerPrincipal);
        var editResult = await editController.Edit(
            new UserEditViewModel
            {
                OriginalUsername = editableUsername,
                Username = editableUsername,
                FullName = "مستخدم حاول المدير العام ترقيته",
                Department = departmentName,
                JobTitle = "منسق تشغيل",
                PhoneNumber = "0505111103",
                IsActive = true,
                Role = AppRoles.Employee,
                ApplyRoleDefaults = false,
                CanViewDashboard = true,
                CanManageUsers = true,
                CanManageDepartments = true,
                CanManageAdministration = true,
                CanManageDelegations = true,
                MustChangeOperatorPin = false,
            }
        );

        Require(
            editResult is ViewResult { Model: UserEditViewModel },
            "general manager should stay on edit view when attempting to grant higher administrative permissions manually"
        );
        Require(
            editController
                .ModelState[string.Empty]
                ?.Errors.Any(error =>
                    error.ErrorMessage.Contains(
                        "إدارة المستخدمين أو الأقسام أو الإدارة أو التفويضات",
                        StringComparison.Ordinal
                    )
                ) == true,
            "manual privilege escalation denial should explain that higher administrative permissions belong to the system owner only"
        );

        var storedUser =
            userAdminService.GetUserAccount(editableUsername)
            ?? throw new InvalidOperationException(
                "editable seed user was not found after denied escalation"
            );
        Require(!storedUser.CanManageUsers, "denied escalation should not grant manage-users");
        Require(
            !storedUser.CanManageDepartments,
            "denied escalation should not grant manage-departments"
        );
        Require(
            !storedUser.CanManageAdministration,
            "denied escalation should not grant manage-administration"
        );
        Require(
            !storedUser.CanManageDelegations,
            "denied escalation should not grant manage-delegations"
        );
    }

    private static void SeedAdministrationSettings(
        ApplicationDbContext db,
        string generalManagerUsername
    )
    {
        var settings = db.AdministrationSettings.FirstOrDefault(x => x.Id == 1);
        if (settings == null)
        {
            settings = new AdministrationSettings { Id = 1 };
            db.AdministrationSettings.Add(settings);
        }

        settings.OrganizationName = "إدارة اختبار المستخدمين";
        settings.DepartmentName = "الإدارة العامة";
        settings.Phone = "0111111111";
        settings.Email = "admin@example.com";
        settings.Address = "مقر الاختبار";
        settings.GeneralManagerUsername = generalManagerUsername;
        settings.ManagerName = string.Empty;
        settings.ManagerTitle = string.Empty;
        settings.ManagerPhoneNumber = string.Empty;

        if (!db.Departments.Any(department => department.Name == settings.DepartmentName))
        {
            db.Departments.Add(
                new Department
                {
                    Name = settings.DepartmentName,
                    ManagerUsername = string.Empty,
                    ManagerDisplayName = string.Empty,
                    IsActive = true,
                }
            );
        }

        db.SaveChanges();
    }

    private static string? ExtractTemporaryPassword(ITempDataDictionary tempData)
    {
        var credentialNoticePassword = ExtractCredentialNoticeValue(
            tempData,
            "__CredentialNoticeTemporaryPassword"
        );
        if (!string.IsNullOrWhiteSpace(credentialNoticePassword))
        {
            return credentialNoticePassword;
        }

        var message =
            FindQueuedToastMessage(tempData, "كلمة مرور الدخول المؤقتة هي")
            ?? FindQueuedToastMessage(tempData, "كلمة المرور الحالية هي");
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        return message.Contains("كلمة مرور الدخول المؤقتة هي", StringComparison.Ordinal)
            ? ExtractValueAfterMarker(message, "كلمة مرور الدخول المؤقتة هي")
            : ExtractValueAfterMarker(message, "كلمة المرور الحالية هي");
    }

    private static string? ExtractTemporaryPin(ITempDataDictionary tempData)
    {
        var message = FindQueuedToastMessage(tempData, "PIN الحالي هو");
        return string.IsNullOrWhiteSpace(message)
            ? null
            : ExtractValueAfterMarker(message, "PIN الحالي هو");
    }

    private static string? ExtractCredentialNoticeValue(ITempDataDictionary tempData, string key)
    {
        return tempData.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static string? ExtractValueAfterMarker(string message, string marker)
    {
        var markerIndex = message.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        return message[(markerIndex + marker.Length)..]
            .Trim()
            .Split(new[] { ' ', '.', '،' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }

    private static string? FindQueuedToastMessage(
        ITempDataDictionary tempData,
        string expectedMessage
    )
    {
        var payload = tempData.Peek("__ToastNotifications") as string;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var notifications = JsonSerializer.Deserialize<List<ToastNotificationItem>>(
            payload,
            UserToastJsonOptions
        );
        return notifications
            ?.Select(notification => notification.Message)
            .FirstOrDefault(message => message.Contains(expectedMessage, StringComparison.Ordinal));
    }
}
