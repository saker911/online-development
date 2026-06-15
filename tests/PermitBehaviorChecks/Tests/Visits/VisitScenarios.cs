namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static Task ScenarioVisitDateChangeRequiresApproval(IVisitService visitService)
    {
        var visitId = CreateApprovedVisit(visitService, "زيارة يوم جديد");

        var currentVisit =
            visitService.GetVisitById(visitId)
            ?? throw new InvalidOperationException("visit not found after approval");

        var updatedVisit = new Visit
        {
            VisitId = visitId,
            VisitorName = currentVisit.VisitorName,
            VisitLocation = currentVisit.VisitLocation,
            NationalId = currentVisit.NationalId,
            PhoneNumber = currentVisit.PhoneNumber,
            Purpose = currentVisit.Purpose,
            VisitedPersonName = currentVisit.VisitedPersonName,
            VisitedPersonType = currentVisit.VisitedPersonType,
            HostName = currentVisit.HostName,
            Companions = currentVisit
                .ActiveCompanions.Select(c => new VisitCompanion
                {
                    Id = c.Id,
                    FullName = c.FullName,
                    NationalId = c.NationalId,
                    PhoneNumber = c.PhoneNumber,
                    Relationship = c.Relationship,
                })
                .ToList(),
            VisitDate = currentVisit.VisitDate.AddDays(1),
            Status = "Active",
        };

        visitService.UpdateVisit(updatedVisit, "tester", resetApprovalStatus: true);

        var changedVisit =
            visitService.GetVisitById(visitId)
            ?? throw new InvalidOperationException("visit not found after date change");

        Require(
            string.Equals(
                changedVisit.ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "moving a visit to a new day should require approval again"
        );
        Require(
            string.Equals(changedVisit.Status, "Active", StringComparison.OrdinalIgnoreCase),
            "moving a visit to a new day should keep it active until approval"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioVisitCompanionsPersistAndFollowScan(IVisitService visitService)
    {
        var visit = new Visit
        {
            VisitorName = "زائر مجموعة",
            VisitLocation = "إدارة التوقيف",
            NationalId = "1234567892",
            PhoneNumber = "0501234567",
            Purpose = "زيارة موقوف",
            VisitedPersonName = "الموقوف أحمد",
            VisitedPersonType = Visit.VisitedPersonTypeDetained,
            VisitDate = AppClock.LocalNow,
            Companions = new List<VisitCompanion>
            {
                new()
                {
                    FullName = "مرافق أول",
                    NationalId = "1234567893",
                    PhoneNumber = "0501234566",
                    Relationship = "أخ",
                },
                new()
                {
                    FullName = "مرافق ثان",
                    NationalId = "1234567894",
                    PhoneNumber = "0501234565",
                    Relationship = "ابن عم",
                },
            },
        };

        visitService.AddVisit(visit);
        visitService.UpdateVisitApprovalStatus(visit.VisitId, "Approved");

        var storedVisit =
            visitService.GetVisitById(visit.VisitId)
            ?? throw new InvalidOperationException("visit with companions not found");

        Require(
            string.Equals(storedVisit.SubjectDisplay, "الموقوف أحمد", StringComparison.Ordinal),
            "visit should persist the visited person name"
        );
        Require(storedVisit.CompanionCount == 2, "visit should persist all companions");

        var entryResult = visitService.RecordVisitScan(visit.VisitId, "gate");
        Require(entryResult.allowed, "approved grouped visit should allow entry scan");

        var insideVisit =
            visitService.GetVisitById(visit.VisitId)
            ?? throw new InvalidOperationException("visit not found after entry scan");

        Require(
            insideVisit.ActiveCompanions.All(c => c.EntryTime.HasValue),
            "entry scan should stamp companion entry times"
        );

        var exitResult = visitService.RecordVisitScan(visit.VisitId, "gate");
        Require(exitResult.allowed, "inside grouped visit should allow exit scan");

        var completedVisit =
            visitService.GetVisitById(visit.VisitId)
            ?? throw new InvalidOperationException("visit not found after exit scan");

        Require(
            completedVisit.ActiveCompanions.All(c => c.ExitTime.HasValue),
            "exit scan should stamp companion exit times"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioVisitApprovalFollowsCurrentDepartmentManager(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IVisitService visitService
    )
    {
        using var db = dbFactory.CreateDbContext();

        var departmentName = $"قسم اعتماد الزيارات {Guid.NewGuid():N}";
        var managerOneUsername = $"visit-manager-a-{Guid.NewGuid():N}";
        var managerTwoUsername = $"visit-manager-b-{Guid.NewGuid():N}";

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

        var firstVisitId = CreatePendingVisitRecord(db, departmentName);
        db.SaveChanges();

        visitService.UpdateVisitApprovalStatus(firstVisitId, "Approved", managerOneUsername);
        Require(
            string.Equals(
                db.Visits.AsNoTracking().First(x => x.VisitId == firstVisitId).ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "current department manager should approve visits for the assigned department"
        );

        department.ManagerUsername = managerTwoUsername;
        department.ManagerDisplayName = "مدير ثان";

        var secondVisitId = CreatePendingVisitRecord(db, departmentName);
        db.SaveChanges();

        visitService.UpdateVisitApprovalStatus(secondVisitId, "Approved", managerOneUsername);
        Require(
            string.Equals(
                db.Visits.AsNoTracking().First(x => x.VisitId == secondVisitId).ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "previous manager should lose visit approval immediately after reassignment"
        );

        visitService.UpdateVisitApprovalStatus(secondVisitId, "Approved", managerTwoUsername);
        Require(
            string.Equals(
                db.Visits.AsNoTracking().First(x => x.VisitId == secondVisitId).ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "newly assigned manager should gain visit approval automatically"
        );

        var secondManager = db.UserAccounts.First(x => x.Username == managerTwoUsername);
        secondManager.Role = AppRoles.Employee;

        var thirdVisitId = CreatePendingVisitRecord(db, departmentName);
        db.SaveChanges();

        visitService.UpdateVisitApprovalStatus(thirdVisitId, "Approved", managerTwoUsername);
        Require(
            string.Equals(
                db.Visits.AsNoTracking().First(x => x.VisitId == thirdVisitId).ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "manager should lose visit approval immediately after role changes to employee"
        );

        secondManager.Role = AppRoles.DepartmentManager;
        secondManager.Department = string.Empty;

        var fourthVisitId = CreatePendingVisitRecord(db, departmentName);
        db.SaveChanges();

        visitService.UpdateVisitApprovalStatus(fourthVisitId, "Approved", managerTwoUsername);
        Require(
            string.Equals(
                db.Visits.AsNoTracking().First(x => x.VisitId == fourthVisitId).ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "manager without a department should not approve visits"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioVisitApprovalFollowsVisitLocationInsteadOfVisitedEmployeeLabel(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IVisitService visitService
    )
    {
        using var db = dbFactory.CreateDbContext();

        var securityDepartmentName = $"شؤون الأمن {Guid.NewGuid():N}";
        var gateDepartmentName = $"قسم البوابة {Guid.NewGuid():N}";
        var securityManagerUsername = $"visit-location-security-{Guid.NewGuid():N}";
        var gateManagerUsername = $"visit-location-gate-{Guid.NewGuid():N}";

        db.Departments.AddRange(
            new Department
            {
                Name = securityDepartmentName,
                ManagerUsername = securityManagerUsername,
                ManagerDisplayName = "مدير شؤون الأمن",
                IsActive = true,
            },
            new Department
            {
                Name = gateDepartmentName,
                ManagerUsername = gateManagerUsername,
                ManagerDisplayName = "مدير البوابة",
                IsActive = true,
            }
        );
        db.UserAccounts.AddRange(
            CreateDepartmentManagerAccount(
                securityManagerUsername,
                securityDepartmentName,
                "مدير شؤون الأمن"
            ),
            CreateDepartmentManagerAccount(gateManagerUsername, gateDepartmentName, "مدير البوابة")
        );
        db.SaveChanges();

        var visitId = CreatePendingVisitRecord(
            db,
            securityDepartmentName,
            visitedEmployeeName: $"موظف البوابة - {gateDepartmentName}"
        );
        db.SaveChanges();

        visitService.UpdateVisitApprovalStatus(visitId, "Approved", gateManagerUsername);
        Require(
            string.Equals(
                db.Visits.AsNoTracking().First(x => x.VisitId == visitId).ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "manager of another department should not approve visit based on visited employee label"
        );

        visitService.UpdateVisitApprovalStatus(visitId, "Approved", securityManagerUsername);
        Require(
            string.Equals(
                db.Visits.AsNoTracking().First(x => x.VisitId == visitId).ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "visit manager should be resolved from visit location even when visited employee label mentions another department"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioVisitApprovalRolesAndPermissionsMatrixIsEnforced(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IVisitService visitService,
        IAccessControlService accessControlService
    )
    {
        using var db = dbFactory.CreateDbContext();

        var managedDepartmentName = $"قسم اعتماد شامل {Guid.NewGuid():N}";
        var detainedDepartmentName = $"قسم توقيف {Guid.NewGuid():N}";
        var otherDepartmentName = $"قسم آخر {Guid.NewGuid():N}";
        var departmentManagerUsername = $"visit-matrix-manager-{Guid.NewGuid():N}";
        var detainedOnlyManagerUsername = $"visit-matrix-detained-{Guid.NewGuid():N}";
        var employeeApproverUsername = $"visit-matrix-employee-{Guid.NewGuid():N}";

        db.Departments.AddRange(
            new Department
            {
                Name = managedDepartmentName,
                ManagerUsername = departmentManagerUsername,
                ManagerDisplayName = "مدير الاعتماد",
                IsActive = true,
            },
            new Department
            {
                Name = detainedDepartmentName,
                ManagerUsername = detainedOnlyManagerUsername,
                ManagerDisplayName = "مدير اعتماد الموقوفين",
                IsActive = true,
            },
            new Department
            {
                Name = otherDepartmentName,
                ManagerUsername = $"other-manager-{Guid.NewGuid():N}",
                ManagerDisplayName = "مدير آخر",
                IsActive = true,
            }
        );

        var departmentManager = CreateDepartmentManagerAccount(
            departmentManagerUsername,
            managedDepartmentName,
            "مدير الاعتماد"
        );

        var detainedOnlyManager = CreateDepartmentManagerAccount(
            detainedOnlyManagerUsername,
            detainedDepartmentName,
            "مدير اعتماد الموقوفين"
        );
        detainedOnlyManager.CanApproveVisits = false;

        var employeeApprover = new UserAccount
        {
            Username = employeeApproverUsername,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            DisplayName = "موظف اعتماد",
            FullName = "موظف اعتماد",
            Department = managedDepartmentName,
            JobTitle = "موظف",
            PhoneNumber = "0555555511",
            Email = string.Empty,
            IsActive = true,
            Role = AppRoles.Employee,
            CanViewVisits = true,
            CanApproveVisits = true,
            CanApproveDetainedVisit = true,
        };

        db.UserAccounts.AddRange(departmentManager, detainedOnlyManager, employeeApprover);

        var generalVisitId = CreatePendingVisitRecord(db, managedDepartmentName);
        var detainedVisitId = $"VISIT-{Guid.NewGuid():N}".ToUpperInvariant();
        db.Visits.Add(
            new Visit
            {
                VisitId = detainedVisitId,
                VisitorName = "زائر موقوف",
                VisitLocation = detainedDepartmentName,
                NationalId = $"{Random.Shared.NextInt64(1000000000, 2999999999)}",
                PhoneNumber = "0555555501",
                Purpose = "زيارة موقوف",
                HostName = "مضيف التوقيف",
                VisitedPersonName = "موقوف 1",
                VisitedPersonType = Visit.VisitedPersonTypeDetained,
                VisitDate = AppClock.LocalNow,
                ApprovalStatus = "Pending",
                Status = "Active",
            }
        );

        var otherDepartmentVisitId = CreatePendingVisitRecord(db, otherDepartmentName);
        db.SaveChanges();

        var generalManager = db.UserAccounts.AsNoTracking().First(x => x.Username == "tester");
        var storedDepartmentManager = db
            .UserAccounts.AsNoTracking()
            .First(x => x.Username == departmentManagerUsername);
        var storedDetainedOnlyManager = db
            .UserAccounts.AsNoTracking()
            .First(x => x.Username == detainedOnlyManagerUsername);
        var storedEmployeeApprover = db
            .UserAccounts.AsNoTracking()
            .First(x => x.Username == employeeApproverUsername);

        var generalVisit = db.Visits.AsNoTracking().First(x => x.VisitId == generalVisitId);
        var detainedVisit = db.Visits.AsNoTracking().First(x => x.VisitId == detainedVisitId);
        var otherDepartmentVisit = db
            .Visits.AsNoTracking()
            .First(x => x.VisitId == otherDepartmentVisitId);

        Require(
            accessControlService.CanApproveVisit(generalVisit, generalManager),
            "general manager should approve visits across all departments"
        );
        Require(
            accessControlService.CanApproveVisit(generalVisit, storedDepartmentManager),
            "department manager with visit approval should approve regular visits in the managed department"
        );
        Require(
            !accessControlService.CanApproveVisit(otherDepartmentVisit, storedDepartmentManager),
            "department manager should not approve visits outside the managed department"
        );
        Require(
            accessControlService.CanApproveVisit(detainedVisit, storedDetainedOnlyManager),
            "detained-only approval should allow detained visits inside the managed department"
        );
        Require(
            !accessControlService.CanApproveVisit(generalVisit, storedDetainedOnlyManager),
            "detained-only approval should not allow regular visits"
        );
        Require(
            !accessControlService.CanApproveVisit(generalVisit, storedEmployeeApprover),
            "regular employee should not approve visits even if approval flags were granted manually"
        );

        visitService.UpdateVisitApprovalStatus(
            generalVisitId,
            "Approved",
            departmentManagerUsername
        );
        visitService.UpdateVisitApprovalStatus(
            detainedVisitId,
            "Approved",
            detainedOnlyManagerUsername
        );
        visitService.UpdateVisitApprovalStatus(
            otherDepartmentVisitId,
            "Approved",
            employeeApproverUsername
        );

        Require(
            string.Equals(
                db.Visits.AsNoTracking().First(x => x.VisitId == generalVisitId).ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "department manager should be able to approve the matching department visit"
        );
        Require(
            string.Equals(
                db.Visits.AsNoTracking().First(x => x.VisitId == detainedVisitId).ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "detained-only manager should be able to approve detained visits"
        );
        Require(
            string.Equals(
                db.Visits.AsNoTracking()
                    .First(x => x.VisitId == otherDepartmentVisitId)
                    .ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "regular employee should not change visit approval status"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioSuspendedVisitsEndpointRequiresAuthorization()
    {
        var suspendedAction =
            typeof(VisitsController).GetMethod(nameof(VisitsController.Suspended))
            ?? throw new InvalidOperationException("VisitsController.Suspended was not found");

        var authorizeAttribute = suspendedAction
            .GetCustomAttributes(
                typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute),
                true
            )
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .SingleOrDefault(attribute =>
                string.Equals(attribute.Policy, AppPolicies.ViewVisits, StringComparison.Ordinal)
            );

        Require(
            authorizeAttribute != null,
            "suspended visits endpoint should require the same ViewVisits policy as the main visits list"
        );

        return Task.CompletedTask;
    }
}
