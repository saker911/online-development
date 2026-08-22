using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static Task ScenarioPendingPermitsCanBeForwardedByDepartmentManager(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        using (var db = dbFactory.CreateDbContext())
        {
            db.Departments.Add(
                new Department
                {
                    Name = "الإدارة التشغيلية",
                    ManagerUsername = "2000000001",
                    ManagerDisplayName = "مدير التشغيل",
                    IsActive = true,
                }
            );

            db.UserAccounts.AddRange(
                new UserAccount
                {
                    Username = "2000000001",
                    DisplayName = "مدير التشغيل",
                    FullName = "مدير التشغيل",
                    Department = "الإدارة التشغيلية",
                    JobTitle = "مدير قسم",
                    PhoneNumber = "0554444444",
                    Role = VehiclePermitSystemWeb.Security.AppRoles.PermitReviewer,
                    IsActive = true,
                    CanViewPermits = true,
                    CanEditPermit = true,
                    CanApprovePermit = false,
                },
                new UserAccount
                {
                    Username = "1000000001",
                    DisplayName = "المدير العام",
                    FullName = "المدير العام",
                    Department = "الإدارة العامة",
                    JobTitle = "مدير عام",
                    PhoneNumber = "0555555555",
                    Role = VehiclePermitSystemWeb.Security.AppRoles.GeneralManager,
                    IsActive = true,
                    CanViewPermits = true,
                    CanApprovePermit = true,
                }
            );

            db.SaveChanges();
        }

        permitService.AddPermit(
            new Permit
            {
                PermitType = Permit.PermitTypePermanent,
                DriverName = "موظف بيانات",
                NationalId = "1231231231",
                VehicleType = "Sedan",
                PlateNumber = "ABC1234",
                DepartmentName = "الإدارة التشغيلية",
                ManagerName = string.Empty,
                EmployeePhone = "0556666666",
                PermitDate = AppClock.LocalNow,
                ExpiresAt = AppClock.LocalNow.AddDays(2),
                RequiresReturn = true,
            },
            "tester"
        );

        var permitNumber = permitService
            .GetAllPermits("2000000001")
            .First(permit =>
                string.Equals(permit.DriverName, "موظف بيانات", StringComparison.OrdinalIgnoreCase)
            )
            .PermitNumber;

        var departmentManagerView = permitService.GetPendingPermits("2000000001").ToList();
        Require(
            departmentManagerView.Count == 0,
            "reviewer without final approval rights should not see security approval queue"
        );

        var forwarded = permitService.ForwardPermitToGeneralManager(permitNumber, "2000000001");
        Require(forwarded, "reviewer should be able to submit the reviewed request");

        var generalManagerView = permitService.GetPendingPermits("1000000001").ToList();
        Require(
            generalManagerView.Count == 1,
            "security manager should receive the permit after review"
        );
        Require(
            string.Equals(
                generalManagerView[0].DriverName,
                "موظف بيانات",
                StringComparison.OrdinalIgnoreCase
            ),
            "security manager should see the reviewed permit"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDepartmentManagerCanLoadPermitLists(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        using var db = dbFactory.CreateDbContext();

        var departmentName = $"قسم الاختبار {Guid.NewGuid():N}";
        var managerUsername = $"manager-{Guid.NewGuid():N}";

        db.Departments.Add(
            new Department
            {
                Name = departmentName,
                ManagerUsername = managerUsername,
                ManagerDisplayName = "مدير اختبار",
                IsActive = true,
            }
        );

        db.UserAccounts.Add(
            new UserAccount
            {
                Username = managerUsername,
                PasswordHash = "hash",
                PasswordSalt = "salt",
                DisplayName = "مدير اختبار",
                FullName = "مدير اختبار",
                Department = departmentName,
                JobTitle = "مدير قسم",
                PhoneNumber = "0555555555",
                Email = string.Empty,
                IsActive = true,
                Role = "DepartmentManager",
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
                CanViewVisits = true,
                CanApproveDetainedVisit = true,
            }
        );

        db.Permits.Add(
            new Permit
            {
                PermitNumber = $"PERMIT-{Guid.NewGuid():N}".ToUpperInvariant(),
                PermitType = Permit.PermitTypePermanent,
                DriverName = "مصرح اختبار",
                NationalId = "1234567890",
                VehicleType = "Sedan",
                PlateNumber = "ABC1234",
                DepartmentName = departmentName,
                EmployeeDepartment = departmentName,
                ManagerName = "مدير اختبار",
                EmployeePhone = "0555555555",
                PermitDate = AppClock.LocalNow.AddHours(-2),
                ExpiresAt = AppClock.LocalNow.AddHours(2),
                ApprovalStatus = "Approved",
            }
        );

        db.SaveChanges();

        var allPermits = permitService.GetAllPermits(managerUsername).ToList();
        var pendingPermits = permitService.GetPendingPermits(managerUsername).ToList();

        Require(
            allPermits.Any(permit =>
                string.Equals(
                    permit.DepartmentName,
                    departmentName,
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "department manager should be able to load scoped permit lists"
        );
        Require(
            pendingPermits.Count == 0,
            "approved scope should not surface pending permits for this scenario"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioLinkedGeneralManagerSyncsAdministrationSettingsAndForwarding(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService,
        IPermitService permitService
    )
    {
        var linkedGeneralManagerUsername = $"gm-linked-{Guid.NewGuid():N}";
        var promotedGeneralManagerUsername = $"gm-promoted-{Guid.NewGuid():N}";
        var departmentManagerUsername = $"dept-manager-{Guid.NewGuid():N}";
        var departmentName = $"قسم تحويل {Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            db.Departments.Add(
                new Department
                {
                    Name = departmentName,
                    ManagerUsername = departmentManagerUsername,
                    ManagerDisplayName = "مدير القسم المحيل",
                    IsActive = true,
                }
            );

            var linkedGeneralManager = new UserAccount
            {
                Username = linkedGeneralManagerUsername,
                DisplayName = "AAA مدير عام",
                FullName = "AAA مدير عام",
                Department = string.Empty,
                JobTitle = "مدير عام أول",
                PhoneNumber = "0555555555",
                IsActive = true,
                Role = AppRoles.GeneralManager,
            };
            AppPermissions.ApplyRoleDefaults(linkedGeneralManager);

            var promotedGeneralManager = new UserAccount
            {
                Username = promotedGeneralManagerUsername,
                DisplayName = "ZZZ مدير عام",
                FullName = "ZZZ مدير عام",
                Department = "إدارة تجريبية",
                JobTitle = "مدير مؤقت",
                PhoneNumber = "0555555556",
                IsActive = true,
                Role = AppRoles.Employee,
            };
            AppPermissions.ApplyRoleDefaults(promotedGeneralManager);

            var departmentManager = new UserAccount
            {
                Username = departmentManagerUsername,
                DisplayName = "مدير القسم المحيل",
                FullName = "مدير القسم المحيل",
                Department = departmentName,
                JobTitle = "مدير قسم",
                PhoneNumber = "0555555557",
                IsActive = true,
                Role = AppRoles.PermitReviewer,
            };
            AppPermissions.ApplyRoleDefaults(departmentManager);
            departmentManager.CanApprovePermit = false;

            db.UserAccounts.AddRange(
                linkedGeneralManager,
                promotedGeneralManager,
                departmentManager
            );
            db.AdministrationSettings.Add(
                new AdministrationSettings
                {
                    Id = 1,
                    OrganizationName = "اختبار",
                    DepartmentName = "الإدارة العامة",
                    GeneralManagerUsername = linkedGeneralManagerUsername,
                    ManagerName = linkedGeneralManager.DisplayName,
                    ManagerTitle = linkedGeneralManager.JobTitle,
                }
            );
            db.SaveChanges();
        }

        var promotedGeneralManagerAccount = userAdminService.GetUserAccount(
            promotedGeneralManagerUsername
        )!;
        promotedGeneralManagerAccount.Role = AppRoles.GeneralManager;
        promotedGeneralManagerAccount.JobTitle = "مدير عام محدث";
        AppPermissions.ApplyRoleDefaults(promotedGeneralManagerAccount);

        var updated = userAdminService.UpdateUser(promotedGeneralManagerAccount);
        Require(updated, "promoting the new general manager should succeed");

        var settings = userAdminService.GetAdministrationSettings();
        using (var uniquenessDb = dbFactory.CreateDbContext())
        {
            var previousLinkedManager = uniquenessDb
                .UserAccounts.AsNoTracking()
                .Single(user => user.Username == linkedGeneralManagerUsername);
            Require(
                !previousLinkedManager.IsActive
                    && string.Equals(
                        previousLinkedManager.Role,
                        AppRoles.Employee,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !previousLinkedManager.CanApprovePermit
                    && !previousLinkedManager.CanApproveVisits,
                "promoting a new general manager through user update should stop the previous linked general manager and revoke general-manager permissions"
            );
            Require(
                uniquenessDb
                    .UserAccounts.AsNoTracking()
                    .Count(user => user.IsActive && user.Role == AppRoles.GeneralManager) == 1,
                "promoting a new general manager through user update should leave only one active general manager"
            );
        }

        Require(
            string.Equals(
                settings.GeneralManagerUsername,
                promotedGeneralManagerUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "administration settings should link to the newly promoted general manager"
        );
        Require(
            string.Equals(
                settings.ManagerName,
                promotedGeneralManagerAccount.DisplayName,
                StringComparison.Ordinal
            ),
            "administration manager name should sync from the linked general manager account"
        );
        Require(
            string.Equals(
                settings.ManagerTitle,
                promotedGeneralManagerAccount.JobTitle,
                StringComparison.Ordinal
            ),
            "administration manager title should sync from the linked general manager account"
        );

        permitService.AddPermit(
            new Permit
            {
                PermitType = Permit.PermitTypePermanent,
                DriverName = "موظف ربط المدير العام",
                NationalId = "1231231231",
                VehicleType = "Sedan",
                PlateNumber = "GM12345",
                DepartmentName = departmentName,
                ManagerName = string.Empty,
                EmployeePhone = "0556666666",
                PermitDate = AppClock.LocalNow,
                ExpiresAt = AppClock.LocalNow.AddDays(2),
                RequiresReturn = true,
            },
            "tester"
        );

        string permitNumber;
        using (var db = dbFactory.CreateDbContext())
        {
            permitNumber = db
                .Permits.AsNoTracking()
                .First(permit => permit.DriverName == "موظف ربط المدير العام")
                .PermitNumber;
        }

        var forwarded = permitService.ForwardPermitToGeneralManager(
            permitNumber,
            departmentManagerUsername
        );
        Require(forwarded, "forwarding to the linked general manager should succeed");

        using var assertDb = dbFactory.CreateDbContext();
        var savedPermit = assertDb
            .Permits.AsNoTracking()
            .First(x => x.PermitNumber == permitNumber);
        Require(
            string.Equals(
                savedPermit.ManagerName,
                promotedGeneralManagerAccount.DisplayName,
                StringComparison.Ordinal
            ),
            "forwarded permit should be assigned to the linked general manager display name"
        );
        Require(
            assertDb
                .UserActivities.AsNoTracking()
                .Any(activity =>
                    activity.Username == promotedGeneralManagerUsername
                    && activity.ActionType == "SecurityApprovalRequested"
                ),
            "forwarding audit should target the linked general manager account"
        );
        Require(
            !assertDb
                .UserActivities.AsNoTracking()
                .Any(activity =>
                    activity.Username == linkedGeneralManagerUsername
                    && activity.ActionType == "SecurityApprovalRequested"
                ),
            "previous general manager should not receive forwarding activity after the link changes"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioPermitApprovalFollowsCurrentDepartmentManager(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        using var db = dbFactory.CreateDbContext();

        var departmentName = $"قسم اعتماد التصاريح {Guid.NewGuid():N}";
        var managerOneUsername = $"permit-manager-a-{Guid.NewGuid():N}";
        var managerTwoUsername = $"permit-manager-b-{Guid.NewGuid():N}";

        var department = new Department
        {
            Name = departmentName,
            ManagerUsername = managerOneUsername,
            ManagerDisplayName = "مدير أول",
            IsActive = true,
        };

        db.Departments.Add(department);
        db.UserAccounts.AddRange(
            CreateDepartmentManagerAccount(managerOneUsername, departmentName, "مدير أول"),
            CreateDepartmentManagerAccount(managerTwoUsername, departmentName, "مدير ثان")
        );
        db.SaveChanges();

        var firstPermitNumber = CreatePendingPermitRecord(db, departmentName, "مدير أول");
        db.SaveChanges();

        permitService.UpdatePermitApprovalStatus(firstPermitNumber, "Approved", managerOneUsername);
        Require(
            string.Equals(
                db.Permits.AsNoTracking()
                    .First(x => x.PermitNumber == firstPermitNumber)
                    .ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "current department manager should approve permits for the assigned department"
        );

        department.ManagerUsername = managerTwoUsername;
        department.ManagerDisplayName = "مدير ثان";

        var secondPermitNumber = CreatePendingPermitRecord(db, departmentName, "مدير ثان");
        db.SaveChanges();

        permitService.UpdatePermitApprovalStatus(
            secondPermitNumber,
            "Approved",
            managerOneUsername
        );
        Require(
            string.Equals(
                db.Permits.AsNoTracking()
                    .First(x => x.PermitNumber == secondPermitNumber)
                    .ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "previous manager should lose permit approval immediately after reassignment"
        );

        permitService.UpdatePermitApprovalStatus(
            secondPermitNumber,
            "Approved",
            managerTwoUsername
        );
        Require(
            string.Equals(
                db.Permits.AsNoTracking()
                    .First(x => x.PermitNumber == secondPermitNumber)
                    .ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "newly assigned manager should gain permit approval automatically"
        );

        var secondManager = db.UserAccounts.First(x => x.Username == managerTwoUsername);
        secondManager.Role = AppRoles.Employee;

        var thirdPermitNumber = CreatePendingPermitRecord(db, departmentName, "مدير ثان");
        db.SaveChanges();

        permitService.UpdatePermitApprovalStatus(thirdPermitNumber, "Approved", managerTwoUsername);
        Require(
            string.Equals(
                db.Permits.AsNoTracking()
                    .First(x => x.PermitNumber == thirdPermitNumber)
                    .ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "manager should lose permit approval immediately after role changes to employee"
        );

        secondManager.Role = AppRoles.DepartmentManager;
        secondManager.Department = string.Empty;

        var fourthPermitNumber = CreatePendingPermitRecord(db, departmentName, "مدير ثان");
        db.SaveChanges();

        permitService.UpdatePermitApprovalStatus(
            fourthPermitNumber,
            "Approved",
            managerTwoUsername
        );
        Require(
            string.Equals(
                db.Permits.AsNoTracking()
                    .First(x => x.PermitNumber == fourthPermitNumber)
                    .ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "manager without a department should not approve permits"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDepartmentManagerPermitCreationKeepsScopedDepartmentApprovalRouteAligned(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IUserAdminService userAdminService,
        IAccessControlService accessControl,
        ISystemClock systemClock
    )
    {
        using var db = dbFactory.CreateDbContext();

        var creatorDepartmentName = $"شؤون الأمن {Guid.NewGuid():N}";
        var otherDepartmentName = $"قسم البوابة {Guid.NewGuid():N}";
        var creatorUsername = $"scoped-create-manager-{Guid.NewGuid():N}";
        var creatorDisplayName = "مدير شؤون الأمن";
        var otherManagerUsername = $"scoped-create-other-manager-{Guid.NewGuid():N}";
        var otherManagerDisplayName = "مدير البوابة";
        var driverName = $"اختبار ضبط القسم {Guid.NewGuid():N}";
        var plateNumber = $"SC {Random.Shared.Next(1000, 9999)}";

        db.Departments.AddRange(
            new Department
            {
                Name = creatorDepartmentName,
                ManagerUsername = creatorUsername,
                ManagerDisplayName = creatorDisplayName,
                IsActive = true,
            },
            new Department
            {
                Name = otherDepartmentName,
                ManagerUsername = otherManagerUsername,
                ManagerDisplayName = otherManagerDisplayName,
                IsActive = true,
            }
        );
        db.UserAccounts.AddRange(
            CreateDepartmentManagerAccount(
                creatorUsername,
                creatorDepartmentName,
                creatorDisplayName
            ),
            CreateDepartmentManagerAccount(
                otherManagerUsername,
                otherDepartmentName,
                otherManagerDisplayName
            )
        );
        db.SaveChanges();

        var controller = CreatePermitsController(
            permitService,
            userAdminService,
            accessControl,
            BuildPrincipal(
                creatorUsername,
                AppRoles.DepartmentManager,
                AppPermissions.CreatePermit,
                AppPermissions.EditPermit,
                AppPermissions.ViewPermits,
                AppPermissions.ViewVisitorPermits
            ),
            systemClock
        );

        var nationalId = $"{Random.Shared.NextInt64(1000000000, 2999999999)}";

        var permit = new Permit
        {
            PermitType = Permit.PermitTypePermanent,
            DriverName = driverName,
            NationalId = nationalId,
            DepartmentName = otherDepartmentName,
            ManagerName = otherManagerDisplayName,
            EmployeePhone = "0555555555",
            ExpiresAt = AppClock.LocalNow.AddDays(7),
            VehicleType = "Sedan",
            PlateNumber = plateNumber,
            RequiresReturn = false,
        };

        var result = controller.Create(permit);
        if (result is not RedirectToActionResult)
        {
            var errors = string.Join(
                " | ",
                controller
                    .ModelState.Values.SelectMany(entry => entry.Errors)
                    .Select(error => error.ErrorMessage)
                    .Where(message => !string.IsNullOrWhiteSpace(message))
            );
            throw new InvalidOperationException(
                $"scoped manager create returned {result.GetType().Name}. ModelState: {errors}"
            );
        }

        var savedPermit = db.Permits.AsNoTracking().First(x => x.NationalId == nationalId);
        Require(
            string.Equals(
                savedPermit.DepartmentName,
                creatorDepartmentName,
                StringComparison.Ordinal
            ),
            "legacy department manager create should preserve its scoped department"
        );
        Require(
            string.Equals(
                savedPermit.EmployeeDepartment,
                creatorDepartmentName,
                StringComparison.Ordinal
            ),
            "legacy employee department should stay aligned with its scoped department"
        );
        Require(
            string.Equals(savedPermit.ManagerName, creatorDisplayName, StringComparison.Ordinal),
            "legacy approval route should remain compatible for existing department managers"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioAdministrationGeneralManagerReplacementFallsBackWhenCurrentManagerLeavesRole(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var currentGeneralManagerUsername = $"gm-current-{Guid.NewGuid():N}";
        var replacementGeneralManagerUsername = $"gm-replacement-{Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            var currentGeneralManager = new UserAccount
            {
                Username = currentGeneralManagerUsername,
                DisplayName = "المدير العام الحالي",
                FullName = "المدير العام الحالي",
                Department = string.Empty,
                JobTitle = "مدير عام قائم",
                PhoneNumber = "0551111111",
                IsActive = true,
                Role = AppRoles.GeneralManager,
            };
            AppPermissions.ApplyRoleDefaults(currentGeneralManager);

            var replacementGeneralManager = new UserAccount
            {
                Username = replacementGeneralManagerUsername,
                DisplayName = "المدير العام البديل",
                FullName = "المدير العام البديل",
                Department = string.Empty,
                JobTitle = "مدير عام بديل",
                PhoneNumber = "0552222222",
                IsActive = true,
                Role = AppRoles.GeneralManager,
            };
            AppPermissions.ApplyRoleDefaults(replacementGeneralManager);

            db.UserAccounts.AddRange(currentGeneralManager, replacementGeneralManager);
            db.AdministrationSettings.Add(
                new AdministrationSettings
                {
                    Id = 1,
                    OrganizationName = "اختبار",
                    DepartmentName = "إدارة الاختبار العامة",
                    GeneralManagerUsername = currentGeneralManagerUsername,
                    ManagerName = currentGeneralManager.DisplayName,
                    ManagerTitle = currentGeneralManager.JobTitle,
                }
            );
            db.SaveChanges();
        }

        var currentGeneralManagerAccount = userAdminService.GetUserAccount(
            currentGeneralManagerUsername
        )!;
        currentGeneralManagerAccount.Role = AppRoles.Employee;
        currentGeneralManagerAccount.JobTitle = "موظف سابق";
        AppPermissions.ApplyRoleDefaults(currentGeneralManagerAccount);

        var updated = userAdminService.UpdateUser(currentGeneralManagerAccount);
        Require(updated, "removing the linked general manager from the role should succeed");

        var settings = userAdminService.GetAdministrationSettings();
        Require(
            string.Equals(
                settings.GeneralManagerUsername,
                replacementGeneralManagerUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "administration settings should fall back to another eligible general manager when the linked one leaves the role"
        );
        Require(
            string.Equals(settings.ManagerName, "المدير العام البديل", StringComparison.Ordinal),
            "administration manager name should sync to the fallback general manager"
        );
        Require(
            string.Equals(settings.ManagerTitle, "مدير عام بديل", StringComparison.Ordinal),
            "administration manager title should sync to the fallback general manager"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDepartmentManagerTransferRequiresReplacementAndReassignsPermissions(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var sourceDepartmentName = $"قسم مصدر {Guid.NewGuid():N}";
        var targetDepartmentName = $"قسم هدف {Guid.NewGuid():N}";
        var currentManagerUsername = $"source-manager-{Guid.NewGuid():N}";
        var replacementManagerUsername = $"replacement-manager-{Guid.NewGuid():N}";
        var movedEmployeeUsername = $"moved-employee-{Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            db.Departments.AddRange(
                new Department
                {
                    Name = sourceDepartmentName,
                    ManagerUsername = currentManagerUsername,
                    ManagerDisplayName = "مدير المصدر",
                    IsActive = true,
                },
                new Department
                {
                    Name = targetDepartmentName,
                    ManagerUsername = string.Empty,
                    ManagerDisplayName = string.Empty,
                    IsActive = true,
                }
            );

            db.UserAccounts.AddRange(
                CreateDepartmentManagerAccount(
                    currentManagerUsername,
                    sourceDepartmentName,
                    "مدير المصدر"
                ),
                CreateDepartmentManagerAccount(
                    replacementManagerUsername,
                    string.Empty,
                    "مدير بديل"
                ),
                new UserAccount
                {
                    Username = movedEmployeeUsername,
                    DisplayName = "موظف منقول",
                    FullName = "موظف منقول",
                    Department = sourceDepartmentName,
                    JobTitle = "موظف",
                    PhoneNumber = "0555555555",
                    Email = string.Empty,
                    IsActive = true,
                    Role = AppRoles.Employee,
                }
            );
            db.SaveChanges();
        }

        var failedTransfer = userAdminService.TransferDepartmentUsers(
            sourceDepartmentName,
            targetDepartmentName,
            new[] { currentManagerUsername, movedEmployeeUsername },
            false,
            "tester"
        );
        Require(
            !failedTransfer,
            "transferring the current department manager should require a replacement first"
        );

        using (var db = dbFactory.CreateDbContext())
        {
            var sourceDepartment = db
                .Departments.AsNoTracking()
                .Single(department => department.Name == sourceDepartmentName);
            var currentManager = db
                .UserAccounts.AsNoTracking()
                .Single(user => user.Username == currentManagerUsername);
            var movedEmployee = db
                .UserAccounts.AsNoTracking()
                .Single(user => user.Username == movedEmployeeUsername);

            Require(
                string.Equals(
                    sourceDepartment.ManagerUsername,
                    currentManagerUsername,
                    StringComparison.OrdinalIgnoreCase
                ),
                "failed transfer should keep the original department manager assignment intact"
            );
            Require(
                string.Equals(
                    currentManager.Department,
                    sourceDepartmentName,
                    StringComparison.OrdinalIgnoreCase
                ),
                "failed transfer should keep the manager in the source department"
            );
            Require(
                string.Equals(
                    movedEmployee.Department,
                    sourceDepartmentName,
                    StringComparison.OrdinalIgnoreCase
                ),
                "failed transfer should not move employees before a replacement is chosen"
            );
        }

        var successfulTransfer = userAdminService.TransferDepartmentUsers(
            sourceDepartmentName,
            targetDepartmentName,
            new[] { currentManagerUsername, movedEmployeeUsername },
            false,
            "tester",
            replacementManagerUsername
        );
        Require(
            successfulTransfer,
            "transferring the current department manager should succeed after choosing a replacement"
        );

        using (var db = dbFactory.CreateDbContext())
        {
            var sourceDepartment = db
                .Departments.AsNoTracking()
                .Single(department => department.Name == sourceDepartmentName);
            var currentManager = db
                .UserAccounts.AsNoTracking()
                .Single(user => user.Username == currentManagerUsername);
            var replacementManager = db
                .UserAccounts.AsNoTracking()
                .Single(user => user.Username == replacementManagerUsername);
            var movedEmployee = db
                .UserAccounts.AsNoTracking()
                .Single(user => user.Username == movedEmployeeUsername);

            Require(
                string.Equals(
                    sourceDepartment.ManagerUsername,
                    replacementManagerUsername,
                    StringComparison.OrdinalIgnoreCase
                ),
                "source department should switch to the selected replacement manager during transfer"
            );
            Require(
                string.Equals(
                    replacementManager.Department,
                    sourceDepartmentName,
                    StringComparison.OrdinalIgnoreCase
                ),
                "replacement manager should be aligned with the source department after handover"
            );
            Require(
                string.Equals(
                    currentManager.Department,
                    targetDepartmentName,
                    StringComparison.OrdinalIgnoreCase
                ),
                "previous manager should move to the target department after transfer"
            );
            Require(
                !currentManager.CanApprovePermit && !currentManager.CanApproveVisits,
                "previous manager should lose department-manager approval permissions after the handover"
            );
            Require(
                replacementManager.CanApprovePermit && replacementManager.CanApproveVisits,
                "replacement manager should keep department-manager approval permissions after assignment"
            );
            Require(
                string.Equals(
                    movedEmployee.Department,
                    targetDepartmentName,
                    StringComparison.OrdinalIgnoreCase
                ),
                "selected employees should move to the target department after the transfer succeeds"
            );
        }

        return Task.CompletedTask;
    }

    private static Task ScenarioDepartmentManagerHandoverRetirementArchivesPreviousManager(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var departmentName = $"قسم إحالة تقاعد {Guid.NewGuid():N}";
        var departmentId = 0;
        var currentManagerUsername = $"retired-manager-{Guid.NewGuid():N}";
        var replacementUsername = $"replacement-employee-{Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            var department = new Department
            {
                Name = departmentName,
                ManagerUsername = currentManagerUsername,
                ManagerDisplayName = "المدير المتقاعد",
                IsActive = true,
            };

            var replacement = new UserAccount
            {
                Username = replacementUsername,
                DisplayName = "المدير البديل",
                FullName = "المدير البديل",
                Department = string.Empty,
                JobTitle = "منسق",
                PhoneNumber = "0555555555",
                Email = string.Empty,
                IsActive = true,
                Role = AppRoles.Employee,
            };
            AppPermissions.ApplyRoleDefaults(replacement);

            db.Departments.Add(department);
            db.UserAccounts.AddRange(
                CreateDepartmentManagerAccount(
                    currentManagerUsername,
                    departmentName,
                    "المدير المتقاعد"
                ),
                replacement
            );
            db.SaveChanges();
            departmentId = department.Id;
        }

        var message = userAdminService.HandoverDepartmentManager(
            new DepartmentManagerHandoverRequest
            {
                DepartmentId = departmentId,
                AssignmentType = DepartmentManagerTransitionTypes.Acting,
                CreateNewManager = false,
                ExistingManagerUsername = replacementUsername,
                ExitAction = DepartmentManagerExitActions.Retirement,
                PreviousManagerNewRole = AppRoles.Employee,
            },
            "tester"
        );

        Require(
            !string.IsNullOrWhiteSpace(message)
                && message.Contains("سحب صلاحيات المدير", StringComparison.Ordinal),
            "retirement handover should return a success message describing permission handover"
        );

        using var assertDb = dbFactory.CreateDbContext();
        var departmentAfter = assertDb
            .Departments.AsNoTracking()
            .Single(department => department.Id == departmentId);
        var retiredManager = assertDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == currentManagerUsername);
        var replacementManager = assertDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == replacementUsername);

        Require(
            string.Equals(
                departmentAfter.ManagerUsername,
                replacementUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "retirement handover should assign the replacement manager to the department"
        );
        Require(
            replacementManager.CanApprovePermit && replacementManager.CanApproveVisits,
            "replacement manager should receive department-manager approval permissions after handover"
        );
        Require(
            !retiredManager.IsActive,
            "retired manager should be archived by deactivating the account"
        );
        Require(
            string.Equals(
                retiredManager.Role,
                AppRoles.Employee,
                StringComparison.OrdinalIgnoreCase
            ),
            "retired manager should drop back to a non-manager role when archived"
        );
        Require(
            string.IsNullOrWhiteSpace(retiredManager.Department),
            "retired manager should no longer remain linked to an internal department after archival"
        );
        Require(
            !retiredManager.CanApprovePermit && !retiredManager.CanApproveVisits,
            "retired manager should lose department-manager permissions after archival"
        );
        var retirementActivities = assertDb.UserActivities.AsNoTracking().ToList();
        Require(
            retirementActivities.Any(activity =>
                activity.Username == currentManagerUsername
                && activity.ActionType == "DepartmentManagerReleased"
                && activity.Message.Contains("التقاعد", StringComparison.Ordinal)
            ),
            "retirement handover should record an activity entry for the previous manager"
        );
        Require(
            assertDb
                .UserActivities.AsNoTracking()
                .Any(activity =>
                    activity.Username == replacementUsername
                    && activity.ActionType == "DepartmentManagerAssigned"
                ),
            "retirement handover should record an assignment activity for the replacement manager"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDepartmentManagerHandoverInternalTransferDemotesPreviousManager(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var sourceDepartmentName = $"قسم نقل داخلي مصدر {Guid.NewGuid():N}";
        var targetDepartmentName = $"قسم نقل داخلي هدف {Guid.NewGuid():N}";
        var sourceDepartmentId = 0;
        var targetDepartmentId = 0;
        var currentManagerUsername = $"internal-source-manager-{Guid.NewGuid():N}";
        var replacementUsername = $"internal-replacement-{Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            var sourceDepartment = new Department
            {
                Name = sourceDepartmentName,
                ManagerUsername = currentManagerUsername,
                ManagerDisplayName = "مدير المصدر الداخلي",
                IsActive = true,
            };
            var targetDepartment = new Department
            {
                Name = targetDepartmentName,
                ManagerUsername = string.Empty,
                ManagerDisplayName = string.Empty,
                IsActive = true,
            };

            var replacement = new UserAccount
            {
                Username = replacementUsername,
                DisplayName = "بديل النقل الداخلي",
                FullName = "بديل النقل الداخلي",
                Department = string.Empty,
                JobTitle = "موظف",
                PhoneNumber = "0555555555",
                Email = string.Empty,
                IsActive = true,
                Role = AppRoles.Employee,
            };
            AppPermissions.ApplyRoleDefaults(replacement);

            db.Departments.AddRange(sourceDepartment, targetDepartment);
            db.UserAccounts.AddRange(
                CreateDepartmentManagerAccount(
                    currentManagerUsername,
                    sourceDepartmentName,
                    "مدير المصدر الداخلي"
                ),
                replacement
            );
            db.SaveChanges();
            sourceDepartmentId = sourceDepartment.Id;
            targetDepartmentId = targetDepartment.Id;
        }

        var message = userAdminService.HandoverDepartmentManager(
            new DepartmentManagerHandoverRequest
            {
                DepartmentId = sourceDepartmentId,
                AssignmentType = DepartmentManagerTransitionTypes.Permanent,
                CreateNewManager = false,
                ExistingManagerUsername = replacementUsername,
                ExitAction = DepartmentManagerExitActions.InternalTransfer,
                PreviousManagerTargetDepartmentId = targetDepartmentId,
                PreviousManagerNewRole = AppRoles.Employee,
            },
            "tester"
        );

        Require(
            !string.IsNullOrWhiteSpace(message),
            "internal transfer handover should succeed when moving the previous manager as an employee"
        );

        using var assertDb = dbFactory.CreateDbContext();
        var sourceDepartmentAfter = assertDb
            .Departments.AsNoTracking()
            .Single(department => department.Id == sourceDepartmentId);
        var targetDepartmentAfter = assertDb
            .Departments.AsNoTracking()
            .Single(department => department.Id == targetDepartmentId);
        var previousManager = assertDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == currentManagerUsername);

        Require(
            string.Equals(
                sourceDepartmentAfter.ManagerUsername,
                replacementUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "internal transfer handover should keep the replacement manager on the source department"
        );
        Require(
            string.IsNullOrWhiteSpace(targetDepartmentAfter.ManagerUsername),
            "demoting the previous manager during internal transfer should not silently assign them to manage the target department"
        );
        Require(
            string.Equals(
                previousManager.Department,
                targetDepartmentName,
                StringComparison.OrdinalIgnoreCase
            ),
            "previous manager should move to the target department during internal transfer"
        );
        Require(
            string.Equals(
                previousManager.Role,
                AppRoles.Employee,
                StringComparison.OrdinalIgnoreCase
            ),
            "previous manager should be demoted to the requested employee role during internal transfer"
        );
        Require(
            !previousManager.CanApprovePermit && !previousManager.CanApproveVisits,
            "previous manager should lose department-manager approval permissions after internal transfer demotion"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDepartmentManagerHandoverInternalTransferCanReassignManagerToAnotherDepartment(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var sourceDepartmentName = $"قسم نقل مدير مصدر {Guid.NewGuid():N}";
        var targetDepartmentName = $"قسم نقل مدير هدف {Guid.NewGuid():N}";
        var sourceDepartmentId = 0;
        var targetDepartmentId = 0;
        var currentManagerUsername = $"manager-transfer-source-{Guid.NewGuid():N}";
        var replacementUsername = $"manager-transfer-replacement-{Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            var sourceDepartment = new Department
            {
                Name = sourceDepartmentName,
                ManagerUsername = currentManagerUsername,
                ManagerDisplayName = "مدير سينتقل لقسم آخر",
                IsActive = true,
            };
            var targetDepartment = new Department
            {
                Name = targetDepartmentName,
                ManagerUsername = string.Empty,
                ManagerDisplayName = string.Empty,
                IsActive = true,
            };

            var replacement = new UserAccount
            {
                Username = replacementUsername,
                DisplayName = "بديل القسم الأصلي",
                FullName = "بديل القسم الأصلي",
                Department = string.Empty,
                JobTitle = "منسق",
                PhoneNumber = "0555555555",
                Email = string.Empty,
                IsActive = true,
                Role = AppRoles.Employee,
            };
            AppPermissions.ApplyRoleDefaults(replacement);

            db.Departments.AddRange(sourceDepartment, targetDepartment);
            db.UserAccounts.AddRange(
                CreateDepartmentManagerAccount(
                    currentManagerUsername,
                    sourceDepartmentName,
                    "مدير سينتقل لقسم آخر"
                ),
                replacement
            );
            db.SaveChanges();
            sourceDepartmentId = sourceDepartment.Id;
            targetDepartmentId = targetDepartment.Id;
        }

        var message = userAdminService.HandoverDepartmentManager(
            new DepartmentManagerHandoverRequest
            {
                DepartmentId = sourceDepartmentId,
                AssignmentType = DepartmentManagerTransitionTypes.Permanent,
                CreateNewManager = false,
                ExistingManagerUsername = replacementUsername,
                ExitAction = DepartmentManagerExitActions.InternalTransfer,
                PreviousManagerTargetDepartmentId = targetDepartmentId,
                PreviousManagerNewRole = AppRoles.DepartmentManager,
            },
            "tester"
        );

        Require(
            !string.IsNullOrWhiteSpace(message),
            "internal transfer handover should allow reassigning the previous manager to another empty department"
        );

        using var assertDb = dbFactory.CreateDbContext();
        var sourceDepartmentAfter = assertDb
            .Departments.AsNoTracking()
            .Single(department => department.Id == sourceDepartmentId);
        var targetDepartmentAfter = assertDb
            .Departments.AsNoTracking()
            .Single(department => department.Id == targetDepartmentId);
        var previousManager = assertDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == currentManagerUsername);

        Require(
            string.Equals(
                sourceDepartmentAfter.ManagerUsername,
                replacementUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "source department should keep the replacement manager after previous manager reassignment"
        );
        Require(
            string.Equals(
                targetDepartmentAfter.ManagerUsername,
                currentManagerUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "previous manager should become the manager of the target department when requested"
        );
        Require(
            string.Equals(
                previousManager.Department,
                targetDepartmentName,
                StringComparison.OrdinalIgnoreCase
            ),
            "reassigned manager should align with the target department after internal transfer"
        );
        Require(
            previousManager.CanApprovePermit && previousManager.CanApproveVisits,
            "reassigned manager should regain department-manager approval permissions for the new department"
        );

        return Task.CompletedTask;
    }

    private static async Task ScenarioDepartmentManagerEditAndDeactivationRequireReplacementFirst(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        const string actorUsername = "tester";
        var sourceDepartmentName = $"قسم مدير حالي {Guid.NewGuid():N}";
        var alternativeDepartmentName = $"قسم بديل {Guid.NewGuid():N}";
        var currentManagerUsername = $"managed-user-{Guid.NewGuid():N}";
        var currentManagerDisplayName = "مدير يحتاج تسليم";
        var sourceDepartmentId = 0;

        using (var db = dbFactory.CreateDbContext())
        {
            var sourceDepartment = new Department
            {
                Name = sourceDepartmentName,
                ManagerUsername = currentManagerUsername,
                ManagerDisplayName = currentManagerDisplayName,
                IsActive = true,
            };
            db.Departments.Add(sourceDepartment);
            db.Departments.Add(
                new Department
                {
                    Name = alternativeDepartmentName,
                    ManagerUsername = string.Empty,
                    ManagerDisplayName = string.Empty,
                    IsActive = true,
                }
            );
            db.UserAccounts.Add(
                CreateDepartmentManagerAccount(
                    currentManagerUsername,
                    sourceDepartmentName,
                    currentManagerDisplayName
                )
            );
            db.SaveChanges();
            sourceDepartmentId = sourceDepartment.Id;
        }

        var managerPrincipal = BuildPrincipal(
            actorUsername,
            AppRoles.GeneralManager,
            AppPermissions.ManageUsers,
            AppPermissions.ManageDepartments
        );

        var editController = CreateUsersController(userAdminService, managerPrincipal);
        var editResult = await editController.Edit(
            new UserEditViewModel
            {
                OriginalUsername = currentManagerUsername,
                Username = currentManagerUsername,
                FullName = currentManagerDisplayName,
                Department = alternativeDepartmentName,
                JobTitle = "مدير قسم",
                PhoneNumber = "0555555555",
                IsActive = true,
                Role = AppRoles.Employee,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = false,
                AutoBindManager = false,
            }
        );

        Require(
            editResult is ViewResult,
            "editing a current department manager away from the managed department should be blocked until replacement"
        );
        Require(
            editController
                .ModelState[string.Empty]
                ?.Errors.Any(error =>
                    error.ErrorMessage.Contains("إعادة تعيين مدير القسم", StringComparison.Ordinal)
                ) == true,
            "blocked department-manager edit should explain that a replacement is required first"
        );

        var unchangedManager =
            userAdminService.GetUserAccount(currentManagerUsername)
            ?? throw new InvalidOperationException(
                "managed department user was not found after failed edit"
            );
        Require(
            string.Equals(
                unchangedManager.Role,
                AppRoles.DepartmentManager,
                StringComparison.OrdinalIgnoreCase
            ),
            "failed edit should preserve the department-manager role"
        );
        Require(
            string.Equals(
                unchangedManager.Department,
                sourceDepartmentName,
                StringComparison.OrdinalIgnoreCase
            ),
            "failed edit should preserve the original managed department"
        );

        var deactivateController = CreateUsersController(userAdminService, managerPrincipal);
        var deactivateResult = deactivateController.Deactivate(currentManagerUsername);

        Require(
            deactivateResult
                is RedirectToActionResult
                {
                    ActionName: nameof(AdministrationController.Leadership),
                    ControllerName: "Administration",
                    RouteValues: not null,
                },
            "deactivating a current department manager should redirect to the department handover screen"
        );

        var deactivateRedirect = (RedirectToActionResult)deactivateResult;
        Require(
            Convert.ToInt32(deactivateRedirect.RouteValues!["managerDepartmentId"])
                == sourceDepartmentId,
            "deactivation redirect should point to the department currently managed by the user"
        );
        Require(
            HasQueuedToast(deactivateController.TempData, "عيّن بديلًا", "warning"),
            "deactivation redirect should explain that the department needs a replacement manager first"
        );
    }

    private static Task ScenarioAdministrationDepartmentsTransferScreenEnforcesManagerHandoverFlow(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService,
        ISystemClock systemClock
    )
    {
        const string actorUsername = "tester";
        var sourceDepartmentName = $"قسم تسليم الشاشة {Guid.NewGuid():N}";
        var targetDepartmentName = $"قسم استقبال الشاشة {Guid.NewGuid():N}";
        var sourceDepartmentId = 0;
        var targetDepartmentId = 0;
        var currentManagerUsername = $"screen-manager-{Guid.NewGuid():N}";
        var currentManagerDisplayName = "مدير الشاشة الحالي";
        var replacementManagerUsername = $"screen-replacement-{Guid.NewGuid():N}";
        var replacementManagerDisplayName = "مدير الشاشة البديل";
        var transferredEmployeeUsername = $"screen-employee-{Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            var sourceDepartment = new Department
            {
                Name = sourceDepartmentName,
                ManagerUsername = currentManagerUsername,
                ManagerDisplayName = currentManagerDisplayName,
                IsActive = true,
            };
            var targetDepartment = new Department
            {
                Name = targetDepartmentName,
                ManagerUsername = string.Empty,
                ManagerDisplayName = string.Empty,
                IsActive = true,
            };

            db.Departments.AddRange(sourceDepartment, targetDepartment);
            db.UserAccounts.AddRange(
                CreateDepartmentManagerAccount(
                    currentManagerUsername,
                    sourceDepartmentName,
                    currentManagerDisplayName
                ),
                CreateDepartmentManagerAccount(
                    replacementManagerUsername,
                    sourceDepartmentName,
                    replacementManagerDisplayName
                ),
                new UserAccount
                {
                    Username = transferredEmployeeUsername,
                    DisplayName = "موظف شاشة",
                    FullName = "موظف شاشة",
                    Department = sourceDepartmentName,
                    JobTitle = "موظف",
                    PhoneNumber = "0555555555",
                    Email = string.Empty,
                    IsActive = true,
                    Role = AppRoles.Employee,
                }
            );
            db.SaveChanges();

            sourceDepartmentId = sourceDepartment.Id;
            targetDepartmentId = targetDepartment.Id;
        }

        var departmentManagerPrincipal = BuildPrincipal(
            actorUsername,
            AppRoles.GeneralManager,
            AppPermissions.ManageDepartments
        );

        var getController = CreateAdministrationController(
            userAdminService,
            departmentManagerPrincipal,
            systemClock
        );
        var getResult = getController.Departments(transferFromId: sourceDepartmentId);

        Require(
            getResult is ViewResult { Model: DepartmentManagementViewModel },
            "departments screen should render the management view model when opening transfer mode"
        );

        var getView = (ViewResult)getResult;
        var getModel = (DepartmentManagementViewModel)getView.Model!;
        Require(
            (getController.ViewData["OpenTransferPanel"] as bool?) == true,
            "departments screen should open the transfer panel when transferFromId is provided"
        );
        Require(
            getModel.TransferSourceDepartmentId == sourceDepartmentId,
            "departments screen should bind the selected source department into the transfer model"
        );
        Require(
            getModel.TransferSourceUsers.Any(user =>
                string.Equals(
                    user.Username,
                    currentManagerUsername,
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "departments screen should list the current manager among source department users"
        );
        Require(
            getModel.TransferReplacementOptions.Any(user =>
                string.Equals(
                    user.Username,
                    replacementManagerUsername,
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "departments screen should expose eligible replacement managers in the transfer model"
        );
        Require(
            !getModel.TransferReplacementOptions.Any(user =>
                string.Equals(
                    user.Username,
                    currentManagerUsername,
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "departments screen should not suggest the current manager as their own replacement"
        );

        var missingReplacementController = CreateAdministrationController(
            userAdminService,
            departmentManagerPrincipal,
            systemClock
        );
        var missingReplacementResult = missingReplacementController.TransferDepartmentUsers(
            new DepartmentManagementViewModel
            {
                TransferSourceDepartmentId = sourceDepartmentId,
                TransferTargetDepartmentId = targetDepartmentId,
                TransferAllUsers = false,
                SelectedTransferUsernames = new List<string>
                {
                    currentManagerUsername,
                    transferredEmployeeUsername,
                },
            }
        );

        Require(
            missingReplacementResult is ViewResult { Model: DepartmentManagementViewModel },
            "transfer post should return the departments view when the current manager has no replacement"
        );
        Require(
            missingReplacementController
                .ModelState[
                    nameof(DepartmentManagementViewModel.TransferReplacementManagerUsername)
                ]
                ?.Errors.Any(error =>
                    error.ErrorMessage.Contains("اختيار بديل", StringComparison.Ordinal)
                ) == true,
            "transfer post should flag the replacement manager field when moving the current manager without handover"
        );

        var movedReplacementController = CreateAdministrationController(
            userAdminService,
            departmentManagerPrincipal,
            systemClock
        );
        var movedReplacementResult = movedReplacementController.TransferDepartmentUsers(
            new DepartmentManagementViewModel
            {
                TransferSourceDepartmentId = sourceDepartmentId,
                TransferTargetDepartmentId = targetDepartmentId,
                TransferAllUsers = false,
                SelectedTransferUsernames = new List<string>
                {
                    currentManagerUsername,
                    replacementManagerUsername,
                    transferredEmployeeUsername,
                },
                TransferReplacementManagerUsername = replacementManagerUsername,
            }
        );

        Require(
            movedReplacementResult is ViewResult { Model: DepartmentManagementViewModel },
            "transfer post should stay on the departments view when the selected replacement is moving in the same request"
        );
        Require(
            movedReplacementController
                .ModelState[
                    nameof(DepartmentManagementViewModel.TransferReplacementManagerUsername)
                ]
                ?.Errors.Any(error =>
                    error.ErrorMessage.Contains("ضمن الموظفين المنقولين", StringComparison.Ordinal)
                ) == true,
            "transfer post should reject a replacement manager who is included in the same transfer set"
        );

        var successfulTransferController = CreateAdministrationController(
            userAdminService,
            departmentManagerPrincipal,
            systemClock
        );
        var successfulTransferResult = successfulTransferController.TransferDepartmentUsers(
            new DepartmentManagementViewModel
            {
                TransferSourceDepartmentId = sourceDepartmentId,
                TransferTargetDepartmentId = targetDepartmentId,
                TransferAllUsers = false,
                SelectedTransferUsernames = new List<string>
                {
                    currentManagerUsername,
                    transferredEmployeeUsername,
                },
                TransferReplacementManagerUsername = replacementManagerUsername,
            }
        );

        Require(
            successfulTransferResult
                is RedirectToActionResult
                {
                    ActionName: nameof(AdministrationController.Leadership),
                    RouteValues: not null,
                },
            "transfer post should redirect back to departments after a valid manager handover"
        );

        var successfulRedirect = (RedirectToActionResult)successfulTransferResult;
        Require(
            Convert.ToInt32(successfulRedirect.RouteValues!["transferFromId"])
                == sourceDepartmentId,
            "successful transfer redirect should keep the source department context for the departments screen"
        );
        Require(
            HasQueuedToast(
                successfulTransferController.TempData,
                replacementManagerDisplayName,
                "success"
            ),
            "successful transfer should publish a confirmation mentioning the replacement manager"
        );

        using (var db = dbFactory.CreateDbContext())
        {
            var sourceDepartment = db
                .Departments.AsNoTracking()
                .Single(department => department.Id == sourceDepartmentId);
            var currentManager = db
                .UserAccounts.AsNoTracking()
                .Single(user => user.Username == currentManagerUsername);
            var replacementManager = db
                .UserAccounts.AsNoTracking()
                .Single(user => user.Username == replacementManagerUsername);

            Require(
                string.Equals(
                    sourceDepartment.ManagerUsername,
                    replacementManagerUsername,
                    StringComparison.OrdinalIgnoreCase
                ),
                "successful transfer through the departments controller should assign the replacement manager to the source department"
            );
            Require(
                string.Equals(
                    currentManager.Department,
                    targetDepartmentName,
                    StringComparison.OrdinalIgnoreCase
                ),
                "successful transfer through the departments controller should move the previous manager to the target department"
            );
            Require(
                string.Equals(
                    replacementManager.Department,
                    sourceDepartmentName,
                    StringComparison.OrdinalIgnoreCase
                ),
                "successful transfer through the departments controller should keep the replacement aligned with the source department"
            );
        }

        return Task.CompletedTask;
    }

    private static Task ScenarioAdministrationHandoverPostAvoidsUnrelatedDepartmentValidation(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService,
        ISystemClock systemClock
    )
    {
        var sourceDepartmentName = $"قسم تسليم {Guid.NewGuid():N}";
        var sourceDepartmentId = 0;
        const string currentManagerUsername = "2100000001";
        const string replacementManagerUsername = "2100000002";

        using (var db = dbFactory.CreateDbContext())
        {
            var sourceDepartment = new Department
            {
                Name = sourceDepartmentName,
                ManagerUsername = currentManagerUsername,
                ManagerDisplayName = "مدير قائم",
                IsActive = true,
            };

            db.Departments.Add(sourceDepartment);
            db.UserAccounts.AddRange(
                CreateDepartmentManagerAccount(
                    currentManagerUsername,
                    sourceDepartmentName,
                    "مدير قائم"
                ),
                CreateDepartmentManagerAccount(
                    replacementManagerUsername,
                    string.Empty,
                    "مدير بديل"
                )
            );
            db.SaveChanges();

            sourceDepartmentId = sourceDepartment.Id;
        }

        var controller = CreateAdministrationController(
            userAdminService,
            BuildPrincipal(
                "system-owner",
                AppRoles.SystemAdmin,
                true,
                AppPermissions.ManageDepartments
            ),
            systemClock
        );

        var result = controller.HandoverDepartmentManager(
            new DepartmentManagerHandoverRequest
            {
                DepartmentId = sourceDepartmentId,
                AssignmentType = DepartmentManagerTransitionTypes.Permanent,
                CreateNewManager = false,
                ExistingManagerUsername = replacementManagerUsername,
                ExitAction = DepartmentManagerExitActions.EndAssignment,
                PreviousManagerNewRole = AppRoles.Employee,
            }
        );

        Require(
            result
                is RedirectToActionResult
                {
                    ActionName: nameof(AdministrationController.Leadership)
                },
            "handover post should redirect back to departments when the dedicated request is valid"
        );
        Require(
            !controller.ModelState.ContainsKey("Department.Name"),
            "handover post should not validate unrelated department-name fields from the create department form"
        );
        Require(
            HasQueuedToast(controller.TempData, "مدير بديل", "success"),
            "successful handover post should queue a success toast mentioning the replacement manager"
        );

        using var verificationDb = dbFactory.CreateDbContext();
        var updatedDepartment = verificationDb.Departments.Single(department =>
            department.Id == sourceDepartmentId
        );
        Require(
            string.Equals(
                updatedDepartment.ManagerUsername,
                replacementManagerUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "handover post should replace the current department manager when the request is valid"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioAdministrationDepartmentsOpenGeneralManagerWizardShowsCurrentState(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService,
        ISystemClock systemClock
    )
    {
        var currentGeneralManagerUsername = $"gm-open-{Guid.NewGuid():N}";

        using (var db = dbFactory.CreateDbContext())
        {
            var currentGeneralManager = new UserAccount
            {
                Username = currentGeneralManagerUsername,
                DisplayName = "المدير العام المفتوح",
                FullName = "المدير العام المفتوح",
                Department = string.Empty,
                JobTitle = "مدير عام",
                PhoneNumber = "0557777777",
                IsActive = true,
                Role = AppRoles.GeneralManager,
            };
            AppPermissions.ApplyRoleDefaults(currentGeneralManager);

            db.UserAccounts.Add(currentGeneralManager);
            db.AdministrationSettings.Add(
                new AdministrationSettings
                {
                    Id = 1,
                    OrganizationName = "اختبار",
                    DepartmentName = "الإدارة العامة",
                    GeneralManagerUsername = currentGeneralManagerUsername,
                    ManagerName = currentGeneralManager.DisplayName,
                    ManagerTitle = currentGeneralManager.JobTitle,
                    ManagerPhoneNumber = currentGeneralManager.PhoneNumber,
                }
            );
            db.SaveChanges();
        }

        var controller = CreateAdministrationController(
            userAdminService,
            BuildPrincipal(
                "system-owner",
                AppRoles.SystemAdmin,
                true,
                AppPermissions.ManageDepartments
            ),
            systemClock
        );

        var result = controller.Departments(openGeneralManagerWizard: true);

        Require(
            result is ViewResult { Model: DepartmentManagementViewModel },
            "departments screen should render the management model when opening the general-manager wizard"
        );

        var viewResult = (ViewResult)result;
        var model = (DepartmentManagementViewModel)viewResult.Model!;
        Require(
            (controller.ViewData["OpenGeneralManagerWizard"] as bool?) == true,
            "departments screen should mark the general-manager wizard as open"
        );
        Require(
            string.Equals(
                model.CurrentGeneralManagerUsername,
                currentGeneralManagerUsername,
                StringComparison.OrdinalIgnoreCase
            )
                && string.Equals(
                    model.CurrentGeneralManagerName,
                    "المدير العام المفتوح",
                    StringComparison.Ordinal
                ),
            "departments screen should load the current general manager into the wizard state"
        );
        Require(
            string.Equals(
                model.GeneralManagerAssignment.SelectionMode,
                GeneralManagerSelectionModes.ExistingUser,
                StringComparison.OrdinalIgnoreCase
            )
                && string.Equals(
                    model.GeneralManagerAssignment.AssignmentType,
                    GeneralManagerAssignmentTypes.Permanent,
                    StringComparison.OrdinalIgnoreCase
                ),
            "departments screen should initialize the general-manager wizard with the default existing-user permanent flow"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioAdministrationDepartmentsPrefillsDepartmentManagerHandoverDraftFromCreateRedirect(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService,
        ISystemClock systemClock
    )
    {
        var departmentName = $"قسم تناقل معبأ {Guid.NewGuid():N}";
        var currentManagerUsername = $"handover-current-{Guid.NewGuid():N}";
        var currentManagerDisplayName = "مدير قائم للتناقل";
        var targetDepartmentId = 0;

        using (var db = dbFactory.CreateDbContext())
        {
            var targetDepartment = new Department
            {
                Name = departmentName,
                ManagerUsername = currentManagerUsername,
                ManagerDisplayName = currentManagerDisplayName,
                IsActive = true,
            };

            db.Departments.Add(targetDepartment);
            db.UserAccounts.Add(
                CreateDepartmentManagerAccount(
                    currentManagerUsername,
                    departmentName,
                    currentManagerDisplayName
                )
            );
            db.SaveChanges();
            targetDepartmentId = targetDepartment.Id;
        }

        var controller = CreateAdministrationController(
            userAdminService,
            BuildPrincipal(
                "system-owner",
                AppRoles.SystemAdmin,
                true,
                AppPermissions.ManageDepartments
            ),
            systemClock
        );

        var result = controller.Departments(
            handoverDepartmentId: targetDepartmentId,
            handoverWizardStep: 2,
            handoverCreateNewManager: true,
            handoverNewManagerUsername: "2100000001",
            handoverNewManagerFullName: "مدير جديد من شاشة المستخدمين",
            handoverNewManagerPhoneNumber: "0502222222",
            handoverAssignmentType: DepartmentManagerTransitionTypes.Permanent,
            handoverExitAction: DepartmentManagerExitActions.EndAssignment,
            handoverPreviousManagerNewRole: AppRoles.Employee
        );

        Require(
            result is ViewResult { Model: DepartmentManagementViewModel },
            "departments screen should render the handover wizard when an occupied department is opened"
        );

        var viewResult = (ViewResult)result;
        var model = (DepartmentManagementViewModel)viewResult.Model!;
        Require(
            (controller.ViewData["OpenManagerHandoverModal"] as bool?) == true,
            "departments screen should open the manager handover modal"
        );
        Require(
            model.Handover.DepartmentId == targetDepartmentId
                && model.Handover.WizardStep == 2
                && model.Handover.CreateNewManager
                && string.Equals(
                    model.Handover.NewManagerUsername,
                    "2100000001",
                    StringComparison.Ordinal
                )
                && string.Equals(
                    model.Handover.NewManagerFullName,
                    "مدير جديد من شاشة المستخدمين",
                    StringComparison.Ordinal
                )
                && string.Equals(
                    model.Handover.NewManagerPhoneNumber,
                    "0502222222",
                    StringComparison.Ordinal
                )
                && string.Equals(
                    model.Handover.AssignmentType,
                    DepartmentManagerTransitionTypes.Permanent,
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(
                    model.Handover.ExitAction,
                    DepartmentManagerExitActions.EndAssignment,
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(
                    model.Handover.PreviousManagerNewRole,
                    AppRoles.Employee,
                    StringComparison.OrdinalIgnoreCase
                ),
            "departments screen should prefill the handover wizard with the draft manager details from the users flow"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioGeneralManagerAssignmentExistingUserSyncsAdministrationAndTransfersPreviousManager(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var currentGeneralManagerUsername = $"gm-current-{Guid.NewGuid():N}";
        var replacementGeneralManagerUsername = $"gm-next-{Guid.NewGuid():N}";
        var targetDepartmentName = $"قسم احتواء المدير السابق {Guid.NewGuid():N}";
        var targetDepartmentId = 0;

        using (var db = dbFactory.CreateDbContext())
        {
            var currentGeneralManager = new UserAccount
            {
                Username = currentGeneralManagerUsername,
                DisplayName = "المدير العام الحالي",
                FullName = "المدير العام الحالي",
                Department = string.Empty,
                JobTitle = "مدير عام قائم",
                PhoneNumber = "0551111111",
                IsActive = true,
                Role = AppRoles.GeneralManager,
            };
            AppPermissions.ApplyRoleDefaults(currentGeneralManager);

            var replacementGeneralManager = new UserAccount
            {
                Username = replacementGeneralManagerUsername,
                DisplayName = "المدير العام البديل",
                FullName = "المدير العام البديل",
                Department = "قسم قديم",
                JobTitle = "موظف مرشح",
                PhoneNumber = "0552222222",
                IsActive = true,
                Role = AppRoles.Employee,
            };
            AppPermissions.ApplyRoleDefaults(replacementGeneralManager);

            var targetDepartment = new Department
            {
                Name = targetDepartmentName,
                ManagerUsername = string.Empty,
                ManagerDisplayName = string.Empty,
                IsActive = true,
            };

            db.Departments.Add(targetDepartment);
            db.UserAccounts.AddRange(currentGeneralManager, replacementGeneralManager);
            db.AdministrationSettings.Add(
                new AdministrationSettings
                {
                    Id = 1,
                    OrganizationName = "اختبار",
                    DepartmentName = "الإدارة العامة",
                    GeneralManagerUsername = currentGeneralManagerUsername,
                    ManagerName = currentGeneralManager.DisplayName,
                    ManagerTitle = currentGeneralManager.JobTitle,
                    ManagerPhoneNumber = currentGeneralManager.PhoneNumber,
                }
            );
            db.SaveChanges();
            targetDepartmentId = targetDepartment.Id;
        }

        var message = userAdminService.AssignGeneralManager(
            new GeneralManagerAssignmentRequest
            {
                SelectionMode = GeneralManagerSelectionModes.ExistingUser,
                ExistingUserUsername = replacementGeneralManagerUsername,
                AssignmentType = GeneralManagerAssignmentTypes.Permanent,
                PreviousGeneralManagerAction = GeneralManagerPreviousActions.InternalTransfer,
                PreviousGeneralManagerTargetDepartmentId = targetDepartmentId,
            },
            "tester"
        );

        Require(
            !string.IsNullOrWhiteSpace(message)
                && message.Contains("تم تبديل المدير العام", StringComparison.Ordinal),
            "existing-user general-manager assignment should return a replacement success message"
        );

        using var assertDb = dbFactory.CreateDbContext();
        var settings = assertDb.AdministrationSettings.AsNoTracking().Single();
        var currentGeneralManagerAfter = assertDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == currentGeneralManagerUsername);
        var replacementGeneralManagerAfter = assertDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == replacementGeneralManagerUsername);

        Require(
            string.Equals(
                settings.GeneralManagerUsername,
                replacementGeneralManagerUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "existing-user assignment should relink administration settings to the replacement general manager"
        );
        Require(
            string.Equals(
                settings.ManagerName,
                replacementGeneralManagerAfter.DisplayName,
                StringComparison.Ordinal
            ),
            "existing-user assignment should sync administration manager name from the replacement account"
        );
        Require(
            string.Equals(settings.ManagerTitle, "مدير عام", StringComparison.Ordinal),
            "existing-user assignment should normalize the replacement job title to the permanent general-manager title"
        );
        Require(
            string.Equals(
                settings.ManagerPhoneNumber,
                replacementGeneralManagerAfter.PhoneNumber,
                StringComparison.Ordinal
            ),
            "existing-user assignment should sync administration manager phone from the replacement account"
        );
        Require(
            string.Equals(
                replacementGeneralManagerAfter.Role,
                AppRoles.GeneralManager,
                StringComparison.OrdinalIgnoreCase
            ) && string.IsNullOrWhiteSpace(replacementGeneralManagerAfter.Department),
            "replacement account should become a departmentless general manager after assignment"
        );
        Require(
            replacementGeneralManagerAfter.CanApprovePermit
                && replacementGeneralManagerAfter.CanApproveVisits,
            "replacement general manager should receive general-manager approval permissions"
        );
        Require(
            string.Equals(
                currentGeneralManagerAfter.Role,
                AppRoles.Employee,
                StringComparison.OrdinalIgnoreCase
            )
                && string.Equals(
                    currentGeneralManagerAfter.Department,
                    targetDepartmentName,
                    StringComparison.OrdinalIgnoreCase
                ),
            "previous general manager should move to the requested internal-transfer department as an employee"
        );
        Require(
            !currentGeneralManagerAfter.IsActive,
            "previous general manager should become inactive after internal transfer so only the replacement remains active"
        );
        Require(
            !currentGeneralManagerAfter.CanApprovePermit
                && !currentGeneralManagerAfter.CanApproveVisits,
            "previous general manager should lose general-manager approval permissions after reassignment"
        );
        Require(
            assertDb
                .UserAccounts.AsNoTracking()
                .Count(user =>
                    user.IsActive
                    && user.Role == AppRoles.GeneralManager
                    && new[]
                    {
                        currentGeneralManagerUsername,
                        replacementGeneralManagerUsername,
                    }.Contains(user.Username)
                ) == 1,
            "existing-user reassignment should leave exactly one active general manager among the accounts involved in the replacement"
        );
        var activities = assertDb.UserActivities.AsNoTracking().ToList();
        Require(
            activities.Any(activity =>
                activity.Username == replacementGeneralManagerUsername
                && activity.ActionType == "GeneralManagerAssigned"
            ),
            "existing-user assignment should record a general-manager assignment activity for the replacement account"
        );
        Require(
            activities.Any(activity =>
                activity.Username == replacementGeneralManagerUsername
                && activity.ActionType == "UpdateAdministrationGeneralManagerLink"
            ),
            "existing-user assignment should record an administration-link sync activity for the replacement account"
        );
        Require(
            activities.Any(activity =>
                activity.Username == currentGeneralManagerUsername
                && activity.ActionType == "GeneralManagerReleased"
                && activity.Message.Contains("النقل الداخلي", StringComparison.Ordinal)
            ),
            "existing-user assignment should record the previous-manager release activity with the selected action label"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioGeneralManagerAssignmentCreateNewManagerSyncsAdministration(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService
    )
    {
        var newGeneralManagerUsername = "general.manager.new";
        var currentGeneralManagerUsername = "2111111110";
        var strayGeneralManagerUsername = "2111111109";
        var seedDepartmentName = $"قسم إنشاء المدير العام {Guid.NewGuid():N}";
        var seedDepartmentId = 0;

        using (var db = dbFactory.CreateDbContext())
        {
            var currentGeneralManager = new UserAccount
            {
                Username = currentGeneralManagerUsername,
                DisplayName = "مدير عام سابق لمسار الإنشاء",
                FullName = "مدير عام سابق لمسار الإنشاء",
                Department = string.Empty,
                JobTitle = "مدير عام",
                PhoneNumber = "0553333301",
                IsActive = true,
                Role = AppRoles.GeneralManager,
            };
            AppPermissions.ApplyRoleDefaults(currentGeneralManager);

            var seedDepartment = new Department
            {
                Name = seedDepartmentName,
                ManagerUsername = string.Empty,
                ManagerDisplayName = string.Empty,
                IsActive = true,
            };
            var strayGeneralManager = new UserAccount
            {
                Username = strayGeneralManagerUsername,
                DisplayName = "مدير عام زائد",
                FullName = "مدير عام زائد",
                Department = string.Empty,
                JobTitle = "مدير عام زائد",
                PhoneNumber = "0553333302",
                IsActive = true,
                Role = AppRoles.GeneralManager,
            };
            AppPermissions.ApplyRoleDefaults(strayGeneralManager);

            db.UserAccounts.AddRange(currentGeneralManager, strayGeneralManager);
            db.Departments.Add(seedDepartment);
            db.AdministrationSettings.Add(
                new AdministrationSettings
                {
                    Id = 1,
                    OrganizationName = "اختبار",
                    DepartmentName = "الإدارة العامة",
                    GeneralManagerUsername = currentGeneralManagerUsername,
                    ManagerName = currentGeneralManager.DisplayName,
                    ManagerTitle = currentGeneralManager.JobTitle,
                    ManagerPhoneNumber = currentGeneralManager.PhoneNumber,
                }
            );
            db.SaveChanges();
            seedDepartmentId = seedDepartment.Id;
        }

        var message = userAdminService.AssignGeneralManager(
            new GeneralManagerAssignmentRequest
            {
                SelectionMode = GeneralManagerSelectionModes.CreateNew,
                NewUserUsername = newGeneralManagerUsername,
                NewUserFullName = "مدير عام جديد",
                NewUserPhoneNumber = "0553333333",
                NewUserPassword = "Aa123456!",
                NewUserConfirmPassword = "Aa123456!",
                NewUserJobTitle = string.Empty,
                NewUserDepartmentId = seedDepartmentId,
                NewUserIsActive = true,
                PreviousGeneralManagerAction = GeneralManagerPreviousActions.EndAssignment,
                AssignmentType = GeneralManagerAssignmentTypes.Acting,
            },
            "tester"
        );

        Require(
            !string.IsNullOrWhiteSpace(message)
                && message.Contains("تم تبديل المدير العام", StringComparison.Ordinal),
            "create-new general-manager assignment should succeed and return a replacement success message when a current manager exists"
        );

        using var assertDb = dbFactory.CreateDbContext();
        var settings = assertDb.AdministrationSettings.AsNoTracking().Single();
        var createdGeneralManager = assertDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == newGeneralManagerUsername);
        var previousGeneralManager = assertDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == currentGeneralManagerUsername);
        var strayGeneralManagerAfter = assertDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == strayGeneralManagerUsername);

        Require(
            string.Equals(
                settings.GeneralManagerUsername,
                newGeneralManagerUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "create-new assignment should link administration settings to the created general manager"
        );
        Require(
            string.Equals(settings.ManagerName, "مدير عام جديد", StringComparison.Ordinal)
                && string.Equals(settings.ManagerTitle, "مدير عام مكلف", StringComparison.Ordinal)
                && string.Equals(
                    settings.ManagerPhoneNumber,
                    "0553333333",
                    StringComparison.Ordinal
                ),
            "create-new assignment should sync administration name, title, and phone from the created account"
        );
        Require(
            string.Equals(
                createdGeneralManager.Role,
                AppRoles.GeneralManager,
                StringComparison.OrdinalIgnoreCase
            )
                && string.IsNullOrWhiteSpace(createdGeneralManager.Department)
                && string.Equals(
                    createdGeneralManager.JobTitle,
                    "مدير عام مكلف",
                    StringComparison.Ordinal
                ),
            "created account should become an acting general manager detached from departments"
        );
        Require(
            createdGeneralManager.IsActive && !createdGeneralManager.MustChangePassword,
            "created general manager should stay active and should not be forced into a password-change flow by this wizard path"
        );
        Require(
            !previousGeneralManager.IsActive
                && string.Equals(
                    previousGeneralManager.Role,
                    AppRoles.Employee,
                    StringComparison.OrdinalIgnoreCase
                )
                && !previousGeneralManager.CanApprovePermit
                && !previousGeneralManager.CanApproveVisits,
            "create-new assignment should deactivate the previous general manager and strip general-manager permissions"
        );
        Require(
            !strayGeneralManagerAfter.IsActive
                && string.Equals(
                    strayGeneralManagerAfter.Role,
                    AppRoles.Employee,
                    StringComparison.OrdinalIgnoreCase
                )
                && !strayGeneralManagerAfter.CanApprovePermit
                && !strayGeneralManagerAfter.CanApproveVisits,
            "create-new assignment should stop any other active general-manager accounts that were not linked in administration settings"
        );
        Require(
            assertDb
                .UserAccounts.AsNoTracking()
                .Count(user =>
                    user.IsActive
                    && user.Role == AppRoles.GeneralManager
                    && new[]
                    {
                        currentGeneralManagerUsername,
                        newGeneralManagerUsername,
                        strayGeneralManagerUsername,
                    }.Contains(user.Username)
                ) == 1,
            "create-new reassignment should leave exactly one active general manager among all related general-manager accounts"
        );
        Require(
            assertDb
                .UserActivities.AsNoTracking()
                .Any(activity =>
                    activity.Username == newGeneralManagerUsername
                    && activity.ActionType == "GeneralManagerAssigned"
                ),
            "create-new assignment should record a general-manager assignment activity for the created account"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioAdministrationAssignGeneralManagerPostBindsNestedWizardRequest(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService,
        ISystemClock systemClock
    )
    {
        var currentGeneralManagerUsername = "2100000001";
        var replacementGeneralManagerUsername = "2100000002";

        using (var db = dbFactory.CreateDbContext())
        {
            var currentGeneralManager = new UserAccount
            {
                Username = currentGeneralManagerUsername,
                DisplayName = "مدير عام قائم",
                FullName = "مدير عام قائم",
                Department = string.Empty,
                JobTitle = "مدير عام قائم",
                PhoneNumber = "0554444441",
                IsActive = true,
                Role = AppRoles.GeneralManager,
            };
            AppPermissions.ApplyRoleDefaults(currentGeneralManager);

            var replacementGeneralManager = new UserAccount
            {
                Username = replacementGeneralManagerUsername,
                DisplayName = "مدير عام بديل من المعالج",
                FullName = "مدير عام بديل من المعالج",
                Department = "إدارة اختبارية",
                JobTitle = "موظف مرشح",
                PhoneNumber = "0554444442",
                IsActive = true,
                Role = AppRoles.Employee,
            };
            AppPermissions.ApplyRoleDefaults(replacementGeneralManager);

            db.UserAccounts.AddRange(currentGeneralManager, replacementGeneralManager);
            db.AdministrationSettings.Add(
                new AdministrationSettings
                {
                    Id = 1,
                    OrganizationName = "اختبار",
                    DepartmentName = "الإدارة العامة",
                    GeneralManagerUsername = currentGeneralManagerUsername,
                    ManagerName = currentGeneralManager.DisplayName,
                    ManagerTitle = currentGeneralManager.JobTitle,
                    ManagerPhoneNumber = currentGeneralManager.PhoneNumber,
                }
            );
            db.SaveChanges();
        }

        var controller = CreateAdministrationController(
            userAdminService,
            BuildPrincipal(
                "system-owner",
                AppRoles.SystemAdmin,
                true,
                AppPermissions.ManageDepartments
            ),
            systemClock
        );

        controller.ControllerContext.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>
            {
                ["GeneralManagerAssignment.WizardStep"] = "5",
                ["GeneralManagerAssignment.SelectionMode"] =
                    GeneralManagerSelectionModes.ExistingUser,
                ["GeneralManagerAssignment.ExistingUserUsername"] =
                    replacementGeneralManagerUsername,
                ["GeneralManagerAssignment.PreviousGeneralManagerAction"] =
                    GeneralManagerPreviousActions.EndAssignment,
                ["GeneralManagerAssignment.PreviousGeneralManagerTargetDepartmentId"] = "0",
                ["GeneralManagerAssignment.AssignmentType"] =
                    GeneralManagerAssignmentTypes.Permanent,
            }
        );
        controller.ModelState.AddModelError("Department.Name", "اسم القسم مطلوب.");

        var result = controller.AssignGeneralManager(new DepartmentManagementViewModel());

        Require(
            result
                is RedirectToActionResult
                {
                    ActionName: nameof(AdministrationController.Leadership)
                },
            "general-manager wizard post should redirect back to departments when the nested request is valid"
        );
        Require(
            !controller.ModelState.ContainsKey("Department.Name"),
            "general-manager wizard post should not validate unrelated create-department fields"
        );
        Require(
            !controller.ModelState.ContainsKey("GeneralManagerAssignment.ExistingUserUsername"),
            "general-manager wizard post should bind the nested existing-user selection from the wizard form"
        );
        Require(
            HasQueuedToast(controller.TempData, "مدير عام بديل من المعالج", "success"),
            "general-manager wizard post should queue a success toast mentioning the replacement manager"
        );

        using var verificationDb = dbFactory.CreateDbContext();
        var settings = verificationDb.AdministrationSettings.AsNoTracking().Single();
        var previousGeneralManager = verificationDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == currentGeneralManagerUsername);

        Require(
            string.Equals(
                settings.GeneralManagerUsername,
                replacementGeneralManagerUsername,
                StringComparison.OrdinalIgnoreCase
            ),
            "general-manager wizard post should update the linked administration general manager"
        );
        Require(
            string.Equals(
                previousGeneralManager.Role,
                AppRoles.Employee,
                StringComparison.OrdinalIgnoreCase
            ),
            "general-manager wizard post should process the previous manager action through the dedicated replacement flow"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioAdministrationAssignGeneralManagerCreateNewPostPreservesCheckedActiveFlag(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserAdminService userAdminService,
        ISystemClock systemClock
    )
    {
        var currentGeneralManagerUsername = "2200000001";
        var newGeneralManagerUsername = "general.manager.wizard";
        var newGeneralManagerFullName = "مدير عام جديد من المعالج";
        var newGeneralManagerPhone = "0556666666";
        var targetDepartmentId = 0;

        using (var db = dbFactory.CreateDbContext())
        {
            var currentGeneralManager = new UserAccount
            {
                Username = currentGeneralManagerUsername,
                DisplayName = "مدير عام قائم للاختبار",
                FullName = "مدير عام قائم للاختبار",
                Department = string.Empty,
                JobTitle = "مدير عام",
                PhoneNumber = "0556666601",
                IsActive = true,
                Role = AppRoles.GeneralManager,
            };
            AppPermissions.ApplyRoleDefaults(currentGeneralManager);

            var department = new Department
            {
                Name = "قسم ربط المدير العام الجديد",
                ManagerUsername = string.Empty,
                ManagerDisplayName = string.Empty,
                IsActive = true,
            };

            db.UserAccounts.Add(currentGeneralManager);
            db.Departments.Add(department);
            db.AdministrationSettings.Add(
                new AdministrationSettings
                {
                    Id = 1,
                    OrganizationName = "اختبار",
                    DepartmentName = "الإدارة العامة",
                    GeneralManagerUsername = currentGeneralManagerUsername,
                    ManagerName = currentGeneralManager.DisplayName,
                    ManagerTitle = currentGeneralManager.JobTitle,
                    ManagerPhoneNumber = currentGeneralManager.PhoneNumber,
                }
            );
            db.SaveChanges();
            targetDepartmentId = department.Id;
        }

        var controller = CreateAdministrationController(
            userAdminService,
            BuildPrincipal(
                "system-owner",
                AppRoles.SystemAdmin,
                true,
                AppPermissions.ManageDepartments
            ),
            systemClock
        );

        controller.ControllerContext.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>
            {
                ["GeneralManagerAssignment.WizardStep"] = "5",
                ["GeneralManagerAssignment.SelectionMode"] = GeneralManagerSelectionModes.CreateNew,
                ["GeneralManagerAssignment.NewUserUsername"] = newGeneralManagerUsername,
                ["GeneralManagerAssignment.NewUserFullName"] = newGeneralManagerFullName,
                ["GeneralManagerAssignment.NewUserPhoneNumber"] = newGeneralManagerPhone,
                ["GeneralManagerAssignment.NewUserPassword"] = "Aa123456!",
                ["GeneralManagerAssignment.NewUserConfirmPassword"] = "Aa123456!",
                ["GeneralManagerAssignment.NewUserJobTitle"] = "مدير عام مكلف",
                ["GeneralManagerAssignment.NewUserDepartmentId"] = targetDepartmentId.ToString(),
                ["GeneralManagerAssignment.NewUserIsActive"] = new StringValues(["true", "false"]),
                ["GeneralManagerAssignment.PreviousGeneralManagerAction"] =
                    GeneralManagerPreviousActions.EndAssignment,
                ["GeneralManagerAssignment.PreviousGeneralManagerTargetDepartmentId"] = "0",
                ["GeneralManagerAssignment.AssignmentType"] = GeneralManagerAssignmentTypes.Acting,
            }
        );
        controller.ModelState.AddModelError("Department.Name", "اسم القسم مطلوب.");

        var result = controller.AssignGeneralManager(new DepartmentManagementViewModel());

        Require(
            result
                is RedirectToActionResult
                {
                    ActionName: nameof(AdministrationController.Leadership)
                },
            "create-new general-manager wizard post should redirect back to departments when the checked active flag is posted"
        );

        using var verificationDb = dbFactory.CreateDbContext();
        var createdGeneralManager = verificationDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == newGeneralManagerUsername);
        var settings = verificationDb.AdministrationSettings.AsNoTracking().Single();
        var previousGeneralManager = verificationDb
            .UserAccounts.AsNoTracking()
            .Single(user => user.Username == currentGeneralManagerUsername);

        Require(
            createdGeneralManager.IsActive,
            "create-new general-manager post should preserve the checked active flag instead of falling back to false from the hidden checkbox input"
        );
        Require(
            string.Equals(
                settings.GeneralManagerUsername,
                newGeneralManagerUsername,
                StringComparison.OrdinalIgnoreCase
            )
                && string.Equals(
                    settings.ManagerName,
                    newGeneralManagerFullName,
                    StringComparison.Ordinal
                ),
            "create-new general-manager post should link administration settings to the newly created active general manager"
        );
        Require(
            !previousGeneralManager.IsActive
                && string.Equals(
                    previousGeneralManager.Role,
                    AppRoles.Employee,
                    StringComparison.OrdinalIgnoreCase
                )
                && verificationDb
                    .UserAccounts.AsNoTracking()
                    .Count(user =>
                        user.IsActive
                        && user.Role == AppRoles.GeneralManager
                        && new[]
                        {
                            currentGeneralManagerUsername,
                            newGeneralManagerUsername,
                        }.Contains(user.Username)
                    ) == 1,
            "create-new general-manager post should deactivate the previous manager so only one active general manager remains among the accounts involved in the replacement"
        );

        return Task.CompletedTask;
    }
}
