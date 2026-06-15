using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    private static string RemovedBootstrapSuperAdminUsername => string.Concat("11111", "11111");
    private static string LegacyBootstrapSuperAdminUsername => "admin";
    private static string LegacyNumericBootstrapSuperAdminUsername =>
        string.Concat("111111", "11111");

    private static Task ScenarioInitialSystemOwnerIsCreatedAsSuperAdminAndSurvivesUpgrade()
    {
        using var harness = new SuperAdminScenarioHarness();

        const string initialUsername = "1234567890";
        const string initialPassword = "Owner2026!";
        var weakPasswordRejected = harness.UserAdminService.CompleteInitialSetup(
            new InitialSetupViewModel
            {
                Username = initialUsername,
                FullName = "مالك النظام",
                PhoneNumber = "0500000000",
                Password = "Owner2026",
                ConfirmPassword = "Owner2026",
                JobTitle = "مالك النظام",
                OrganizationName = "الإدارة العامة للأمن",
                AdministrationPhone = "0111111111",
                AdministrationEmail = "owner@example.com",
                AdministrationAddress = "الرياض",
            }
        );
        Require(!weakPasswordRejected, "initial setup should reject weak super-admin passwords");

        var created = harness.UserAdminService.CompleteInitialSetup(
            new InitialSetupViewModel
            {
                Username = initialUsername,
                FullName = "مالك النظام",
                PhoneNumber = "0500000000",
                Password = initialPassword,
                ConfirmPassword = initialPassword,
                JobTitle = "مالك النظام",
                OrganizationName = "الإدارة العامة للأمن",
                AdministrationPhone = "0111111111",
                AdministrationEmail = "owner@example.com",
                AdministrationAddress = "الرياض",
            }
        );
        Require(created, "fresh install should create the initial system owner account");

        var initialOwner =
            harness.UserAdminService.GetUserAccount(initialUsername)
            ?? throw new InvalidOperationException("initial system owner was not created");
        Require(initialOwner.IsSuperAdmin, "initial system owner should be marked as super admin");
        Require(
            !initialOwner.MustChangePassword,
            "wizard-created owner should not be forced into an immediate password reset"
        );
        Require(
            harness.UserAdminService.ValidateCredentials(
                initialUsername,
                initialPassword,
                out var displayName,
                out _
            ),
            "initial system owner should be able to sign in with the submitted strong password"
        );
        Require(
            !harness.UserAdminService.ValidateCredentials(
                initialUsername,
                "NotTheOwner2026!",
                out _,
                out _
            ),
            "initial system owner should not accept any fallback or unrelated password"
        );
        Require(
            string.Equals(displayName, "مالك النظام", StringComparison.Ordinal),
            "initial system owner should preserve the submitted display name"
        );
        Require(
            AppPermissions.GetGrantedPermissions(initialOwner).Contains(AppPermissions.ManageUsers),
            "initial system owner should receive full management permissions"
        );

        var administration = harness.UserAdminService.GetAdministrationSettings();
        Require(
            administration.IsInitialSetupCompleted,
            "initial setup wizard should mark the system as initialized"
        );
        Require(
            string.Equals(
                administration.OrganizationName,
                "الإدارة العامة للأمن",
                StringComparison.Ordinal
            ),
            "initial setup wizard should persist the organization name"
        );
        Require(
            string.Equals(administration.Phone, "0111111111", StringComparison.Ordinal),
            "initial setup wizard should persist the organization phone"
        );

        harness.DatabaseBootstrapService.EnsureInitialized();
        new DatabaseBootstrapService(harness.DbFactory, harness.Configuration).EnsureInitialized();

        using var db = harness.DbFactory.CreateDbContext();
        Require(
            db.UserAccounts.Count(user => user.IsSuperAdmin) == 1,
            "future upgrades should preserve exactly one super admin account"
        );
        Require(
            db.UserAccounts.Count() == 1,
            "future upgrades should not create duplicate bootstrap accounts"
        );
        Require(
            db.UserAccounts.AsNoTracking().Single().IsSuperAdmin,
            "future upgrades should preserve the super-admin flag on the initial owner"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioLegacyAdminUpgradePreservesMarkedSuperAdminWithoutRenaming()
    {
        using var harness = new SuperAdminScenarioHarness();

        const string preservedPassword = "Legacy14011401!";
        var legacyAdmin = new UserAccount
        {
            Username = LegacyBootstrapSuperAdminUsername,
            DisplayName = "مدير النظام القديم",
            FullName = "مدير النظام القديم",
            Department = "الإدارة الرئيسية",
            JobTitle = "مشرف النظام",
            PhoneNumber = "0501111111",
            Role = AppRoles.SystemAdmin,
            IsActive = true,
        };
        AppPermissions.ApplyRoleDefaults(legacyAdmin);

        var secondaryPrivilegedUser = new UserAccount
        {
            Username = "2000000001",
            DisplayName = "مدير عام احتياطي",
            FullName = "مدير عام احتياطي",
            Department = string.Empty,
            JobTitle = "مدير عام",
            PhoneNumber = "0502222222",
            Role = AppRoles.GeneralManager,
            IsActive = true,
        };
        AppPermissions.ApplyRoleDefaults(secondaryPrivilegedUser);

        Require(
            harness.UserAdminService.CreateUser(legacyAdmin, preservedPassword),
            "legacy admin account should be seeded before upgrade"
        );
        Require(
            harness.UserAdminService.CreateUser(secondaryPrivilegedUser, "Bb15011501!"),
            "secondary privileged user should be seeded before upgrade"
        );

        using (var setupDb = harness.DbFactory.CreateDbContext())
        {
            var account = setupDb.UserAccounts.Single(user =>
                user.Username == LegacyBootstrapSuperAdminUsername
            );
            account.IsSuperAdmin = true;
            setupDb.SaveChanges();
        }

        harness.DatabaseBootstrapService.EnsureInitialized();

        using var db = harness.DbFactory.CreateDbContext();
        var upgradedLegacyAdmin = db
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == LegacyBootstrapSuperAdminUsername);
        var upgradedSecondaryUser = db
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == "2000000001");

        Require(
            upgradedLegacyAdmin.IsSuperAdmin,
            "upgrade should preserve the legacy admin account when it is already marked as super admin"
        );
        Require(
            !upgradedSecondaryUser.IsSuperAdmin,
            "upgrade should not automatically promote every privileged account to super admin"
        );
        Require(
            db.UserAccounts.Count(user => user.IsSuperAdmin) == 1,
            "upgrade should choose exactly one super admin"
        );
        Require(
            db.UserAccounts.Count(user => user.Username == RemovedBootstrapSuperAdminUsername) == 0,
            "upgrade should not create or rename to a bootstrap super-admin account"
        );
        Require(
            harness.UserAdminService.ValidateCredentials(
                LegacyBootstrapSuperAdminUsername,
                preservedPassword,
                out _,
                out _
            ),
            "upgrade should preserve the existing admin password and username"
        );
        Require(
            !harness.UserAdminService.ValidateCredentials(
                RemovedBootstrapSuperAdminUsername,
                preservedPassword,
                out _,
                out _
            ),
            "bootstrap username should not authenticate when it was not the submitted account"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUpgradeDoesNotPromotePrivilegedUserWhenNoSuperAdminFlagExists()
    {
        using var harness = new SuperAdminScenarioHarness();

        var privilegedUser = new UserAccount
        {
            Username = "2000000002",
            DisplayName = "مدير نظام عادي",
            FullName = "مدير نظام عادي",
            Department = string.Empty,
            JobTitle = "مدير نظام",
            PhoneNumber = "0502222223",
            Role = AppRoles.SystemAdmin,
            IsActive = true,
        };
        AppPermissions.ApplyRoleDefaults(privilegedUser);

        Require(
            harness.UserAdminService.CreateUser(privilegedUser, "Cc16011601!"),
            "privileged user should be seeded before upgrade"
        );

        harness.DatabaseBootstrapService.EnsureInitialized();

        using var db = harness.DbFactory.CreateDbContext();
        Require(
            !db.UserAccounts.Any(user => user.IsSuperAdmin),
            "upgrade should not invent a super-admin account when no persisted account is marked as super admin"
        );
        Require(
            db.UserAccounts.Count(user => user.Username == RemovedBootstrapSuperAdminUsername) == 0,
            "upgrade should not create the bootstrap super-admin identity for existing data"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUpgradeBootstrapSuppressesFirstRunWizardForExistingData()
    {
        using var harness = new SuperAdminScenarioHarness();

        Require(
            harness.UserAdminService.CreateUser(
                new UserAccount
                {
                    Username = "1023456789",
                    DisplayName = "مشغل النظام الحالي",
                    FullName = "مشغل النظام الحالي",
                    Department = string.Empty,
                    JobTitle = "مشغل النظام",
                    PhoneNumber = "0501234567",
                    Role = AppRoles.SystemAdmin,
                    IsActive = true,
                },
                "Aa1111111111!"
            ),
            "existing system user should be seeded before upgrade bootstrap"
        );

        using (var db = harness.DbFactory.CreateDbContext())
        {
            var settings = db.AdministrationSettings.SingleOrDefault(x => x.Id == 1);
            if (settings == null)
            {
                settings = new AdministrationSettings { Id = 1, OrganizationName = "جهة موجودة" };
                db.AdministrationSettings.Add(settings);
            }

            settings.IsInitialSetupCompleted = false;
            db.SaveChanges();
        }

        Require(
            harness.UserAdminService.IsInitialSetupRequired(),
            "a legacy database with users but an unset completion flag should look uninitialized before bootstrap"
        );

        harness.DatabaseBootstrapService.EnsureInitialized();

        var upgradedSettings = harness.UserAdminService.GetAdministrationSettings();
        Require(
            upgradedSettings.IsInitialSetupCompleted,
            "upgrade bootstrap should mark the existing system as initialized when users already exist"
        );
        Require(
            !harness.UserAdminService.IsInitialSetupRequired(),
            "existing user data should suppress the first-run wizard after upgrade bootstrap"
        );

        var accountController = new AccountController(harness.UserAdminService);
        var initialSetupResult = accountController.InitialSetup();
        Require(
            initialSetupResult
                is RedirectToActionResult { ActionName: nameof(AccountController.Login) },
            "initial setup page should redirect to login on upgraded systems with persisted users"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioLegacyNumericBootstrapUpgradePreservesExistingUsername()
    {
        using var harness = new SuperAdminScenarioHarness();

        const string preservedPassword = "Legacy14011401!";
        var legacyNumericOwner = new UserAccount
        {
            Username = LegacyNumericBootstrapSuperAdminUsername,
            DisplayName = "مالك قديم",
            FullName = "مالك قديم",
            Department = string.Empty,
            JobTitle = "مشرف النظام",
            PhoneNumber = "0501111111",
            Role = AppRoles.SystemAdmin,
            IsActive = true,
            IsSuperAdmin = true,
        };
        AppPermissions.ApplyRoleDefaults(legacyNumericOwner);

        Require(
            harness.UserAdminService.CreateUser(legacyNumericOwner, preservedPassword),
            "legacy numeric bootstrap account should be seeded before upgrade"
        );

        using (var db = harness.DbFactory.CreateDbContext())
        {
            var account = db.UserAccounts.Single(user =>
                user.Username == LegacyNumericBootstrapSuperAdminUsername
            );
            account.IsSuperAdmin = true;
            db.SaveChanges();
        }

        harness.DatabaseBootstrapService.EnsureInitialized();

        using var upgradedDb = harness.DbFactory.CreateDbContext();
        Require(
            upgradedDb.UserAccounts.Any(user =>
                user.Username == LegacyNumericBootstrapSuperAdminUsername && user.IsSuperAdmin
            ),
            "upgrade should preserve the previous numeric bootstrap username when it is already the super admin"
        );
        Require(
            !upgradedDb.UserAccounts.Any(user =>
                user.Username == RemovedBootstrapSuperAdminUsername
            ),
            "upgrade should not create a replacement bootstrap username"
        );
        Require(
            harness.UserAdminService.ValidateCredentials(
                LegacyNumericBootstrapSuperAdminUsername,
                preservedPassword,
                out _,
                out _
            ),
            "upgrade should preserve the password and existing numeric username"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioSuperAdminRenameKeepsPrivilegesAndProtectedActions()
    {
        using var harness = new SuperAdminScenarioHarness();

        const string originalPassword = "Owner14011401!";
        const string updatedPassword = "Bb15011501!";
        const string originalUsername = "1099999999";
        const string renamedUsername = "1234567890";

        Require(
            harness.UserAdminService.CompleteInitialSetup(
                new InitialSetupViewModel
                {
                    Username = originalUsername,
                    FullName = "مالك النظام",
                    PhoneNumber = "0503333333",
                    Password = originalPassword,
                    ConfirmPassword = originalPassword,
                    JobTitle = "مالك النظام",
                    OrganizationName = "جهة الاختبار",
                    AdministrationPhone = "0111111111",
                    AdministrationEmail = "owner-rename@example.com",
                    AdministrationAddress = "الرياض",
                }
            ),
            "super-admin rename scenario should create the submitted owner account first"
        );

        var systemOwner =
            harness.UserAdminService.GetUserAccount(originalUsername)
            ?? throw new InvalidOperationException("initial owner account was not created");
        systemOwner.Username = renamedUsername;
        systemOwner.DisplayName = "مالك النظام المحدث";
        systemOwner.FullName = "مالك النظام المحدث";
        systemOwner.JobTitle = "المالك السيادي";
        systemOwner.Role = AppRoles.Employee;

        Require(
            harness.UserAdminService.UpdateUser(systemOwner, updatedPassword, originalUsername),
            "super admin should be able to rename the protected account to a real 10-digit national id and set a new password"
        );

        var renamedOwner =
            harness.UserAdminService.GetUserAccount(renamedUsername)
            ?? throw new InvalidOperationException("renamed super-admin account was not found");
        Require(
            harness.UserAdminService.GetUserAccount(originalUsername) == null,
            "old super-admin username should no longer resolve after rename"
        );
        Require(renamedOwner.IsSuperAdmin, "renamed account should keep the super-admin flag");
        Require(
            harness.UserAdminService.ValidateCredentials(
                renamedUsername,
                updatedPassword,
                out _,
                out _
            ),
            "renamed super-admin account should accept the new password after rename"
        );
        Require(
            !harness.UserAdminService.ValidateCredentials(
                renamedUsername,
                originalPassword,
                out _,
                out _
            ),
            "old password should stop working after the super-admin password is replaced during rename"
        );
        Require(
            !harness.UserAdminService.ValidateCredentials(
                originalUsername,
                updatedPassword,
                out _,
                out _
            ),
            "old username should no longer authenticate after rename"
        );
        Require(
            AppPermissions
                .GetGrantedPermissions(renamedOwner)
                .Contains(AppPermissions.ManageAdministration)
                && AppPermissions
                    .GetGrantedPermissions(renamedOwner)
                    .Contains(AppPermissions.ManageUsers),
            "renamed super-admin account should retain the protected management permissions"
        );

        using (var db = harness.DbFactory.CreateDbContext())
        {
            db.Permits.Add(
                new Permit
                {
                    PermitNumber = $"PERMIT-{Guid.NewGuid():N}".ToUpperInvariant(),
                    PermitType = Permit.PermitTypePermanent,
                    DriverName = "موظف محمي",
                    NationalId = "1234567890",
                    VehicleType = "Sedan",
                    PlateNumber = "ABC1234",
                    DepartmentName = "قسم لا يتبع المالك",
                    EmployeeDepartment = "قسم لا يتبع المالك",
                    ManagerName = "مدير القسم",
                    EmployeePhone = "0554444444",
                    PermitDate = AppClock.LocalNow,
                    ExpiresAt = AppClock.LocalNow.AddDays(1),
                    ApprovalStatus = "Pending",
                    CurrentState = "Outside",
                }
            );
            db.SaveChanges();
        }

        using (var db = harness.DbFactory.CreateDbContext())
        {
            var permit = db.Permits.AsNoTracking().Single();
            Require(
                harness.AccessControlService.CanAccessPermit(permit, renamedOwner),
                "super admin should bypass permit access scope restrictions"
            );
            Require(
                harness.AccessControlService.CanApprovePermit(permit, renamedOwner),
                "super admin should bypass permit approval scope restrictions"
            );
        }

        var superAdminPrincipal = BuildPrincipal(renamedUsername, AppRoles.Employee, true);
        Require(
            superAdminPrincipal.HasPermission(AppPermissions.ManageUsers),
            "super-admin claim should bypass permission checks even if the role text changes"
        );

        var auditController = CreateUsersController(harness.UserAdminService, superAdminPrincipal);
        var auditResult = auditController.ActivityLog(username: renamedUsername);
        Require(
            auditResult is ViewResult { Model: UserActivityReportViewModel },
            "super admin should be able to open protected user-audit screens"
        );

        var managerPrincipal = BuildPrincipal(
            "tester",
            AppRoles.GeneralManager,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments
        );
        var resetPasswordController = CreateUsersController(
            harness.UserAdminService,
            managerPrincipal
        );
        var resetPasswordResult = resetPasswordController.ResetTemporaryPassword(renamedUsername);
        Require(
            resetPasswordResult
                is RedirectToActionResult { ActionName: nameof(UsersController.Index) },
            "resetting a super-admin password from the users screen should be blocked"
        );
        Require(
            HasQueuedToast(resetPasswordController.TempData, "مالك النظام", "danger"),
            "blocked super-admin password reset should publish a protected-account error"
        );

        new DatabaseBootstrapService(harness.DbFactory, harness.Configuration).EnsureInitialized();

        using var upgradedDb = harness.DbFactory.CreateDbContext();
        Require(
            upgradedDb.UserAccounts.Count(user => user.IsSuperAdmin) == 1,
            "future upgrades should preserve exactly one renamed super-admin account"
        );
        Require(
            upgradedDb.UserAccounts.Any(user =>
                user.Username == renamedUsername && user.IsSuperAdmin
            ),
            "future upgrades should preserve the renamed super-admin identity"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioSuperAdminDoesNotUseHardcodedPasswordBackdoor()
    {
        var sourceRoot = LocateSourceRoot();
        var scannedFiles = new[]
        {
            Path.Combine(sourceRoot, "Security", "AppSecurity.cs"),
            Path.Combine(sourceRoot, "Services", "Users", "UserAccountService.cs"),
            Path.Combine(sourceRoot, "Services", "Users", "UserAdminService.cs"),
            Path.Combine(sourceRoot, "Services", "Bootstrap", "DatabaseBootstrapService.cs"),
        };
        var forbiddenTerms = new[]
        {
            string.Concat("Aa14", "011401!"),
            string.Concat("Super", "Admin", "Password"),
            string.Concat("Super", "Admin", "Fixed", "Password"),
            string.Concat("admin", "123"),
        };

        foreach (var filePath in scannedFiles)
        {
            var content = File.ReadAllText(filePath);
            foreach (var forbiddenTerm in forbiddenTerms)
            {
                Require(
                    !content.Contains(forbiddenTerm, StringComparison.Ordinal),
                    $"security source file should not contain hardcoded credential term in {Path.GetFileName(filePath)}"
                );
            }
        }

        return Task.CompletedTask;
    }

    private static Task ScenarioSuperAdminCanExecutePermitAndVisitApproval(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IVisitService visitService,
        IAccessControlService accessControlService
    )
    {
        using var db = dbFactory.CreateDbContext();
        const string testSuperAdminUsername = "1099999998";

        var superAdmin = new UserAccount
        {
            Username = testSuperAdminUsername,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            DisplayName = "السوبر أدمن",
            FullName = "السوبر أدمن",
            Department = string.Empty,
            JobTitle = "مالك النظام",
            PhoneNumber = "0501111111",
            Email = string.Empty,
            IsActive = true,
            Role = AppRoles.Employee,
            IsSuperAdmin = true,
        };
        AppPermissions.ApplyRoleDefaults(superAdmin);

        db.UserAccounts.Add(superAdmin);

        var permitNumber = CreatePendingPermitRecord(db, "قسم لا يتبع المالك", "مدير القسم");
        var visitId = CreatePendingVisitRecord(db, "قسم لا يتبع المالك");
        db.SaveChanges();

        var pendingPermit = db.Permits.AsNoTracking().First(x => x.PermitNumber == permitNumber);
        var pendingVisit = db.Visits.AsNoTracking().First(x => x.VisitId == visitId);
        var storedSuperAdmin = db
            .UserAccounts.AsNoTracking()
            .First(x => x.Username == testSuperAdminUsername);

        Require(
            accessControlService.CanApprovePermit(pendingPermit, storedSuperAdmin),
            "super admin should be allowed to approve permits directly"
        );
        Require(
            accessControlService.CanApproveVisit(pendingVisit, storedSuperAdmin),
            "super admin should be allowed to approve visits directly"
        );

        permitService.UpdatePermitApprovalStatus(permitNumber, "Approved", testSuperAdminUsername);
        visitService.UpdateVisitApprovalStatus(visitId, "Approved", testSuperAdminUsername);

        using var verificationDb = dbFactory.CreateDbContext();

        Require(
            string.Equals(
                verificationDb
                    .Permits.AsNoTracking()
                    .First(x => x.PermitNumber == permitNumber)
                    .ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "super admin approval should change permit status to approved"
        );
        Require(
            string.Equals(
                verificationDb
                    .Visits.AsNoTracking()
                    .First(x => x.VisitId == visitId)
                    .ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "super admin approval should change visit status to approved"
        );

        return Task.CompletedTask;
    }

    private static async Task ScenarioAdministrationEditPreservesAutomaticGeneralManagerSync()
    {
        using var harness = new SuperAdminScenarioHarness();

        var activeGeneralManager = new UserAccount
        {
            Username = "2000000001",
            DisplayName = "مدير عام تلقائي",
            FullName = "مدير عام تلقائي",
            Department = string.Empty,
            JobTitle = "مدير عام معتمد",
            PhoneNumber = "0508000001",
            Role = AppRoles.GeneralManager,
            IsActive = true,
        };
        AppPermissions.ApplyRoleDefaults(activeGeneralManager);

        var initialSetup = harness.UserAdminService.CompleteInitialSetup(
            new InitialSetupViewModel
            {
                Username = "1999999999",
                FullName = "مالك النظام للاختبار",
                PhoneNumber = "0509999999",
                Password = "Owner2026!",
                ConfirmPassword = "Owner2026!",
                JobTitle = "مالك النظام",
                OrganizationName = "جهة الاختبار",
                AdministrationPhone = "0111111111",
                AdministrationEmail = "admin-test@example.com",
                AdministrationAddress = "الرياض",
            }
        );
        Require(
            initialSetup,
            "initial setup should complete before validating administration save behavior"
        );
        Require(
            !harness.UserAdminService.IsInitialSetupRequired(),
            "initial setup should no longer be required after the owner account is created"
        );

        Require(
            harness.UserAdminService.CreateUser(activeGeneralManager, "GeneralManager2026!"),
            "active general manager account should be created"
        );

        var currentSettings = harness.UserAdminService.GetAdministrationSettings();
        Require(
            string.Equals(
                currentSettings.GeneralManagerUsername,
                activeGeneralManager.Username,
                StringComparison.OrdinalIgnoreCase
            ),
            "administration settings should auto-link to the active general manager account"
        );

        var updatedSettings = harness.UserAdminService.GetAdministrationSettings();
        updatedSettings.GeneralManagerUsername = string.Empty;
        updatedSettings.ManagerName = "قيمة يدوية يجب تجاهلها";
        updatedSettings.ManagerTitle = "قيمة يدوية يجب تجاهلها";
        updatedSettings.ManagerPhoneNumber = "0500000000";
        updatedSettings.OrganizationName = "إدارة بعد الحفظ";
        updatedSettings.Phone = "0112345678";
        updatedSettings.WorkStartTime = new TimeOnly(7, 30);
        updatedSettings.WorkEndTime = new TimeOnly(15, 45);
        updatedSettings.LateReturnGraceMinutes = 12;
        updatedSettings.OfficialWorkDaysCsv = "Sunday,Monday,Tuesday";

        var administrationController = CreateAdministrationController(
            harness.UserAdminService,
            BuildPrincipal(
                "system-admin",
                AppRoles.SystemAdmin,
                AppPermissions.ManageAdministration
            ),
            harness.Clock
        );

        var result = await administrationController.Edit(updatedSettings, null, null);
        Require(
            result is RedirectToActionResult { ActionName: nameof(AdministrationController.Edit) },
            "administration edit should redirect back to the administration screen after save"
        );

        var persistedSettings = harness.UserAdminService.GetAdministrationSettings();

        Require(
            string.Equals(
                persistedSettings.GeneralManagerUsername,
                activeGeneralManager.Username,
                StringComparison.OrdinalIgnoreCase
            ),
            "administration edit should preserve the automatic general-manager link even when the form does not post a manual link"
        );
        Require(
            string.Equals(
                persistedSettings.ManagerName,
                activeGeneralManager.DisplayName,
                StringComparison.Ordinal
            ),
            "administration manager name should still sync automatically from the active general manager account after save"
        );
        Require(
            string.Equals(
                persistedSettings.ManagerTitle,
                activeGeneralManager.JobTitle,
                StringComparison.Ordinal
            ),
            "administration manager title should still sync automatically from the active general manager account after save"
        );
        Require(
            string.Equals(
                persistedSettings.ManagerPhoneNumber,
                activeGeneralManager.PhoneNumber,
                StringComparison.Ordinal
            ),
            "administration manager phone should remain sourced from the active general manager after save"
        );
        Require(
            string.Equals(persistedSettings.Phone, updatedSettings.Phone, StringComparison.Ordinal),
            "administration phone should update independently from the general-manager mobile number"
        );
        Require(
            persistedSettings.WorkStartTime == updatedSettings.WorkStartTime
                && persistedSettings.WorkEndTime == updatedSettings.WorkEndTime
                && persistedSettings.LateReturnGraceMinutes
                    == updatedSettings.LateReturnGraceMinutes
                && string.Equals(
                    persistedSettings.OfficialWorkDaysCsv,
                    updatedSettings.OfficialWorkDaysCsv,
                    StringComparison.Ordinal
                ),
            "administration work schedule updates should persist without mutating the general-manager display fields"
        );
        Require(
            persistedSettings.IsInitialSetupCompleted,
            "saving administration settings should not reset the initial setup completion flag"
        );
        Require(
            !harness.UserAdminService.IsInitialSetupRequired(),
            "administration save should not make the system require the first-run setup again"
        );
    }

    private static string LocateSourceRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VehiclePermitSystemWeb.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the project source root.");
    }

    private sealed class SuperAdminScenarioHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;

        public SuperAdminScenarioHarness()
        {
            Clock = new MutableSystemClock(new DateTime(2026, 4, 13, 9, 0, 0));
            AppClock.Configure(Clock);

            Configuration = new ConfigurationBuilder().Build();
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(Configuration);
            services.AddHttpContextAccessor();
            services.AddMemoryCache();
            services.AddSingleton<ISystemClock>(Clock);
            services.AddDbContextFactory<ApplicationDbContext>(options =>
                options.UseSqlite(_connection)
            );
            services.AddSingleton<IPermitAuditService, PermitAuditService>();
            services.AddSingleton<IDelegationService, DelegationService>();
            services.AddSingleton<IAccessControlService, AccessControlService>();
            services.AddSingleton<UserSessionService>();
            services.AddSingleton<IUserAdminService, UserAdminService>();
            services.AddSingleton<IDatabaseBootstrapService, DatabaseBootstrapService>();

            _serviceProvider = services.BuildServiceProvider();
            DbFactory = _serviceProvider.GetRequiredService<
                IDbContextFactory<ApplicationDbContext>
            >();
            UserAdminService = _serviceProvider.GetRequiredService<IUserAdminService>();
            AccessControlService = _serviceProvider.GetRequiredService<IAccessControlService>();
            DatabaseBootstrapService =
                _serviceProvider.GetRequiredService<IDatabaseBootstrapService>();

            using var db = DbFactory.CreateDbContext();
            db.Database.EnsureCreated();
        }

        public IConfiguration Configuration { get; }

        public MutableSystemClock Clock { get; }

        public IDbContextFactory<ApplicationDbContext> DbFactory { get; }

        public IUserAdminService UserAdminService { get; }

        public IAccessControlService AccessControlService { get; }

        public IDatabaseBootstrapService DatabaseBootstrapService { get; }

        public void Dispose()
        {
            _serviceProvider.Dispose();
            _connection.Dispose();
        }
    }
}
