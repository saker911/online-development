using System.Reflection;
using Microsoft.AspNetCore.Mvc;
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
    private static Task ScenarioDelegationPostActionsBindNestedEditorPrefix()
    {
        var createAction = typeof(DelegationsController).GetMethod(
            nameof(DelegationsController.Create),
            new[] { typeof(DelegationEditViewModel) }
        );
        var editAction = typeof(DelegationsController).GetMethod(
            nameof(DelegationsController.Edit),
            new[] { typeof(int), typeof(DelegationEditViewModel) }
        );

        var createBindPrefix = createAction
            ?.GetParameters()
            .SingleOrDefault()
            ?.GetCustomAttribute<BindAttribute>()
            ?.Prefix;
        var editBindPrefix = editAction
            ?.GetParameters()
            .LastOrDefault()
            ?.GetCustomAttribute<BindAttribute>()
            ?.Prefix;

        Require(
            string.Equals(createBindPrefix, "Editor", StringComparison.Ordinal),
            "delegation create post should bind nested editor fields using the Editor prefix"
        );
        Require(
            string.Equals(editBindPrefix, "Editor", StringComparison.Ordinal),
            "delegation edit post should bind nested editor fields using the Editor prefix"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioFullDelegationGrantsPermitApprovalScopeAndAudit(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IAccessControlService accessControlService,
        IDelegationService delegationService
    )
    {
        var delegatee = CreateEmployeeAccount(
            $"delegate-permit-{Guid.NewGuid():N}",
            "مفوّض تصاريح"
        );
        string permitNumber;
        using (var db = dbFactory.CreateDbContext())
        {
            db.UserAccounts.Add(delegatee);
            permitNumber = CreatePendingPermitRecord(db, "الإدارة العامة", "المدير العام");
            db.SaveChanges();
        }

        var createResult = delegationService.CreateDelegation(
            new DelegationDefinitionInput
            {
                DelegatorUsername = "tester",
                DelegateeUsername = delegatee.Username,
                ScopeType = DelegationScopeTypes.Full,
                StartAt = AppClock.LocalNow.AddMinutes(-5),
                EndAt = AppClock.LocalNow.AddDays(2),
                Notes = "تفويض عام مؤقت",
            },
            "tester"
        );
        Require(createResult.Succeeded, "full delegation should be created successfully");

        Permit permit;
        UserAccount storedDelegatee;
        using (var db = dbFactory.CreateDbContext())
        {
            permit = db.Permits.AsNoTracking().First(x => x.PermitNumber == permitNumber);
            storedDelegatee = db
                .UserAccounts.AsNoTracking()
                .First(x => x.Username == delegatee.Username);
        }

        Require(
            accessControlService.CanApprovePermit(permit, storedDelegatee),
            "full delegation should grant permit approval scope"
        );
        Require(
            GetPendingPermitNumbersForUser(dbFactory, accessControlService, delegatee.Username)
                .Contains(permitNumber, StringComparer.OrdinalIgnoreCase),
            "delegatee should receive the pending permit queue through delegation"
        );

        permitService.UpdatePermitApprovalStatus(permitNumber, "Approved", delegatee.Username);

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
            "delegated permit approval should execute successfully"
        );
        Require(
            verificationDb
                .AuditLogs.AsNoTracking()
                .Any(log =>
                    log.ActedUnderDelegation
                    && log.EntityType == "Permit"
                    && log.EntityId == permitNumber
                    && log.ActualActorUsername == delegatee.Username
                    && log.DelegatedFromUsername == "tester"
                ),
            "delegated permit approval should be stored in the audit log with actor metadata"
        );
        Require(
            delegationService
                .GetDelegatedActionAuditLogs(delegatee.Username, "tester")
                .Any(log => log.EntityId == permitNumber),
            "delegated action report source should include the delegated permit approval"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioCustomVisitsOnlyDelegationWorks(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IVisitService visitService,
        IAccessControlService accessControlService,
        IDelegationService delegationService
    )
    {
        var delegatee = CreateEmployeeAccount($"delegate-visit-{Guid.NewGuid():N}", "مفوّض زيارات");
        string visitId;
        using (var db = dbFactory.CreateDbContext())
        {
            db.UserAccounts.Add(delegatee);
            visitId = CreatePendingVisitRecord(db, "الإدارة العامة");
            db.SaveChanges();
        }

        var createResult = delegationService.CreateDelegation(
            new DelegationDefinitionInput
            {
                DelegatorUsername = "tester",
                DelegateeUsername = delegatee.Username,
                ScopeType = DelegationScopeTypes.Custom,
                PermissionKeys = new[] { AppPermissions.ViewVisits, AppPermissions.ApproveVisits },
                StartAt = AppClock.LocalNow.AddMinutes(-5),
                EndAt = AppClock.LocalNow.AddDays(1),
                Notes = "تفويض زيارات فقط",
            },
            "tester"
        );
        Require(createResult.Succeeded, "visits-only delegation should be created successfully");

        Visit visit;
        UserAccount storedDelegatee;
        using (var db = dbFactory.CreateDbContext())
        {
            visit = db.Visits.AsNoTracking().First(x => x.VisitId == visitId);
            storedDelegatee = db
                .UserAccounts.AsNoTracking()
                .First(x => x.Username == delegatee.Username);
        }

        Require(
            accessControlService.CanViewVisits(storedDelegatee),
            "visits-only delegation should grant visits visibility"
        );
        Require(
            accessControlService.CanApproveVisit(visit, storedDelegatee),
            "visits-only delegation should grant visit approval"
        );
        Require(
            !accessControlService.CanViewPermits(storedDelegatee),
            "visits-only delegation should not open permit scope"
        );
        Require(
            GetPendingVisitIdsForUser(dbFactory, accessControlService, delegatee.Username)
                .Contains(visitId, StringComparer.OrdinalIgnoreCase),
            "delegated visit queues should include the pending visit"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioCustomPermitsOnlyDelegationWorks(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IAccessControlService accessControlService,
        IDelegationService delegationService
    )
    {
        var delegatee = CreateEmployeeAccount(
            $"delegate-permits-only-{Guid.NewGuid():N}",
            "مفوّض اعتماد تصاريح"
        );
        string permitNumber;
        using (var db = dbFactory.CreateDbContext())
        {
            db.UserAccounts.Add(delegatee);
            permitNumber = CreatePendingPermitRecord(db, "الإدارة العامة", "المدير العام");
            db.SaveChanges();
        }

        var createResult = delegationService.CreateDelegation(
            new DelegationDefinitionInput
            {
                DelegatorUsername = "tester",
                DelegateeUsername = delegatee.Username,
                ScopeType = DelegationScopeTypes.Custom,
                PermissionKeys = new[] { AppPermissions.ViewPermits, AppPermissions.ApprovePermit },
                StartAt = AppClock.LocalNow.AddMinutes(-5),
                EndAt = AppClock.LocalNow.AddDays(1),
                Notes = "اعتماد تصاريح فقط",
            },
            "tester"
        );
        Require(createResult.Succeeded, "permits-only delegation should be created successfully");

        Permit permit;
        UserAccount storedDelegatee;
        using (var db = dbFactory.CreateDbContext())
        {
            permit = db.Permits.AsNoTracking().First(x => x.PermitNumber == permitNumber);
            storedDelegatee = db
                .UserAccounts.AsNoTracking()
                .First(x => x.Username == delegatee.Username);
        }

        Require(
            accessControlService.CanApprovePermit(permit, storedDelegatee),
            "permits-only delegation should allow permit approval"
        );
        Require(
            !accessControlService.CanViewVisits(storedDelegatee),
            "permits-only delegation should not open visits scope"
        );

        Require(
            GetPendingPermitNumbersForUser(dbFactory, accessControlService, delegatee.Username)
                .Contains(permitNumber, StringComparer.OrdinalIgnoreCase),
            "permits-only delegation should expose permit queues"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioExpiredDelegationLosesAccessAutomatically(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IAccessControlService accessControlService,
        IDelegationService delegationService,
        MutableSystemClock clock
    )
    {
        var delegatee = CreateEmployeeAccount(
            $"delegate-expired-{Guid.NewGuid():N}",
            "تفويض منتهي"
        );
        string permitNumber;
        using (var db = dbFactory.CreateDbContext())
        {
            db.UserAccounts.Add(delegatee);
            permitNumber = CreatePendingPermitRecord(db, "الإدارة العامة", "المدير العام");
            db.SaveChanges();
        }

        var createResult = delegationService.CreateDelegation(
            new DelegationDefinitionInput
            {
                DelegatorUsername = "tester",
                DelegateeUsername = delegatee.Username,
                ScopeType = DelegationScopeTypes.Custom,
                PermissionKeys = new[] { AppPermissions.ViewPermits, AppPermissions.ApprovePermit },
                StartAt = AppClock.LocalNow.AddMinutes(-30),
                EndAt = AppClock.LocalNow.AddMinutes(30),
            },
            "tester"
        );
        Require(createResult.Succeeded, "expiring delegation should be created successfully");

        clock.SetLocalNow(AppClock.LocalNow.AddDays(2));

        Permit permit;
        UserAccount storedDelegatee;
        using (var db = dbFactory.CreateDbContext())
        {
            permit = db.Permits.AsNoTracking().First(x => x.PermitNumber == permitNumber);
            storedDelegatee = db
                .UserAccounts.AsNoTracking()
                .First(x => x.Username == delegatee.Username);
        }

        Require(
            !accessControlService.CanApprovePermit(permit, storedDelegatee),
            "expired delegation should remove permit approval automatically"
        );
        Require(
            !GetPendingPermitNumbersForUser(dbFactory, accessControlService, delegatee.Username)
                .Contains(permitNumber, StringComparer.OrdinalIgnoreCase),
            "expired delegation should remove queued permit access automatically"
        );
        Require(
            string.Equals(
                delegationService
                    .GetDelegations(delegateeUsername: delegatee.Username)
                    .Single()
                    .Status,
                DelegationStatuses.Expired,
                StringComparison.OrdinalIgnoreCase
            ),
            "delegation status should switch to expired automatically"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioCancelledDelegationLosesAccessImmediately(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IAccessControlService accessControlService,
        IDelegationService delegationService
    )
    {
        var delegatee = CreateEmployeeAccount(
            $"delegate-cancelled-{Guid.NewGuid():N}",
            "تفويض ملغي"
        );
        string permitNumber;
        using (var db = dbFactory.CreateDbContext())
        {
            db.UserAccounts.Add(delegatee);
            permitNumber = CreatePendingPermitRecord(db, "الإدارة العامة", "المدير العام");
            db.SaveChanges();
        }

        var createResult = delegationService.CreateDelegation(
            new DelegationDefinitionInput
            {
                DelegatorUsername = "tester",
                DelegateeUsername = delegatee.Username,
                ScopeType = DelegationScopeTypes.Custom,
                PermissionKeys = new[] { AppPermissions.ViewPermits, AppPermissions.ApprovePermit },
                StartAt = AppClock.LocalNow.AddMinutes(-5),
                EndAt = AppClock.LocalNow.AddDays(1),
            },
            "tester"
        );
        Require(createResult.Succeeded, "cancellable delegation should be created successfully");
        Require(
            delegationService.CancelDelegation(
                createResult.Delegation!.Id,
                "tester",
                "إلغاء اختباري"
            ),
            "delegation should cancel successfully"
        );

        Permit permit;
        UserAccount storedDelegatee;
        using (var db = dbFactory.CreateDbContext())
        {
            permit = db.Permits.AsNoTracking().First(x => x.PermitNumber == permitNumber);
            storedDelegatee = db
                .UserAccounts.AsNoTracking()
                .First(x => x.Username == delegatee.Username);
        }

        Require(
            !accessControlService.CanApprovePermit(permit, storedDelegatee),
            "cancelled delegation should remove permit approval immediately"
        );
        Require(
            !GetPendingPermitNumbersForUser(dbFactory, accessControlService, delegatee.Username)
                .Contains(permitNumber, StringComparer.OrdinalIgnoreCase),
            "cancelled delegation should remove permit queues immediately"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioCannotDelegatePermissionsNotOwned(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IDelegationService delegationService
    )
    {
        var weakDelegator = CreateEmployeeAccount(
            $"delegate-owner-{Guid.NewGuid():N}",
            "مفوّض لا يملك الصلاحية"
        );
        weakDelegator.CanManageUsers = true;
        var delegatee = CreateEmployeeAccount($"delegate-target-{Guid.NewGuid():N}", "هدف التفويض");
        using (var db = dbFactory.CreateDbContext())
        {
            db.UserAccounts.AddRange(weakDelegator, delegatee);
            db.SaveChanges();
        }

        var createResult = delegationService.CreateDelegation(
            new DelegationDefinitionInput
            {
                DelegatorUsername = weakDelegator.Username,
                DelegateeUsername = delegatee.Username,
                ScopeType = DelegationScopeTypes.Custom,
                PermissionKeys = new[] { AppPermissions.ManageUsers },
                StartAt = AppClock.LocalNow.AddMinutes(-5),
                EndAt = AppClock.LocalNow.AddDays(1),
            },
            weakDelegator.Username
        );

        Require(
            !createResult.Succeeded
                && string.Equals(
                    createResult.ErrorCode,
                    "delegation_permission_not_delegateable",
                    StringComparison.OrdinalIgnoreCase
                ),
            "administrative permissions should remain non-delegateable even when the delegator owns them"
        );

        weakDelegator.IsSuperAdmin = true;
        using (var db = dbFactory.CreateDbContext())
        {
            var stored = db.UserAccounts.Single(user => user.Username == weakDelegator.Username);
            stored.IsSuperAdmin = true;
            db.SaveChanges();
        }

        var protectedResult = delegationService.CreateDelegation(
            new DelegationDefinitionInput
            {
                DelegatorUsername = weakDelegator.Username,
                DelegateeUsername = delegatee.Username,
                ScopeType = DelegationScopeTypes.Full,
                StartAt = AppClock.LocalNow.AddMinutes(-5),
                EndAt = AppClock.LocalNow.AddDays(1),
            },
            weakDelegator.Username
        );
        Require(
            !protectedResult.Succeeded
                && string.Equals(
                    protectedResult.ErrorCode,
                    "delegation_protected_user_not_allowed",
                    StringComparison.OrdinalIgnoreCase
                ),
            "system-owner accounts should never participate in delegation definitions"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDelegationDoesNotMutateDirectPermissionsOrDepartment(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IDelegationService delegationService
    )
    {
        var delegatee = CreateEmployeeAccount(
            $"delegate-invariants-{Guid.NewGuid():N}",
            "ثبات القسم والصلاحيات"
        );
        delegatee.Department = "قسم التشغيل الميداني";

        HashSet<string> delegateeDirectPermissionsBefore;
        HashSet<string> delegatorDirectPermissionsBefore;
        string delegateeDepartmentBefore;
        string delegatorDepartmentBefore;

        using (var db = dbFactory.CreateDbContext())
        {
            db.UserAccounts.Add(delegatee);
            db.SaveChanges();

            var delegator = db.UserAccounts.AsNoTracking().First(x => x.Username == "tester");
            var storedDelegatee = db
                .UserAccounts.AsNoTracking()
                .First(x => x.Username == delegatee.Username);

            delegateeDirectPermissionsBefore = AppPermissions
                .GetGrantedPermissions(storedDelegatee)
                .ToHashSet(StringComparer.Ordinal);
            delegatorDirectPermissionsBefore = AppPermissions
                .GetGrantedPermissions(delegator)
                .ToHashSet(StringComparer.Ordinal);
            delegateeDepartmentBefore = storedDelegatee.Department;
            delegatorDepartmentBefore = delegator.Department;
        }

        var createResult = delegationService.CreateDelegation(
            new DelegationDefinitionInput
            {
                DelegatorUsername = "tester",
                DelegateeUsername = delegatee.Username,
                ScopeType = DelegationScopeTypes.Custom,
                PermissionKeys = new[] { AppPermissions.ViewPermits, AppPermissions.ApprovePermit },
                StartAt = AppClock.LocalNow.AddMinutes(-5),
                EndAt = AppClock.LocalNow.AddDays(1),
                Notes = "اختبار ثبات الحساب",
            },
            "tester"
        );
        Require(createResult.Succeeded, "invariant delegation should be created successfully");

        using var verificationDb = dbFactory.CreateDbContext();
        var delegatorAfter = verificationDb
            .UserAccounts.AsNoTracking()
            .First(x => x.Username == "tester");
        var delegateeAfter = verificationDb
            .UserAccounts.AsNoTracking()
            .First(x => x.Username == delegatee.Username);

        var delegateeDirectPermissionsAfter = AppPermissions
            .GetGrantedPermissions(delegateeAfter)
            .ToHashSet(StringComparer.Ordinal);
        var delegatorDirectPermissionsAfter = AppPermissions
            .GetGrantedPermissions(delegatorAfter)
            .ToHashSet(StringComparer.Ordinal);
        var delegateeEffectivePermissions = delegationService
            .GetEffectivePermissions(delegateeAfter)
            .ToHashSet(StringComparer.Ordinal);

        Require(
            string.Equals(
                delegateeAfter.Department,
                delegateeDepartmentBefore,
                StringComparison.Ordinal
            ),
            "delegation should not change the delegatee department"
        );
        Require(
            string.Equals(
                delegatorAfter.Department,
                delegatorDepartmentBefore,
                StringComparison.Ordinal
            ),
            "delegation should not change the delegator department"
        );
        Require(
            delegateeDirectPermissionsAfter.SetEquals(delegateeDirectPermissionsBefore),
            "delegation should not persist delegated permissions onto the delegatee account"
        );
        Require(
            delegatorDirectPermissionsAfter.SetEquals(delegatorDirectPermissionsBefore),
            "delegation should not mutate the delegator direct permissions"
        );
        Require(
            !delegateeAfter.CanViewPermits && !delegateeAfter.CanApprovePermit,
            "delegatee direct permission flags should remain unchanged after delegation"
        );
        Require(
            delegateeEffectivePermissions.Contains(AppPermissions.ViewPermits)
                && delegateeEffectivePermissions.Contains(AppPermissions.ApprovePermit),
            "delegated permissions should appear only through the effective permission calculation"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioScopedApprovalsStillWorkWithDelegation(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IVisitService visitService,
        IAccessControlService accessControlService,
        IDelegationService delegationService
    )
    {
        var managedDepartmentName = $"قسم نطاق مفوض {Guid.NewGuid():N}";
        var otherDepartmentName = $"قسم نطاق آخر {Guid.NewGuid():N}";
        var managerUsername = $"delegation-manager-{Guid.NewGuid():N}";
        var delegatee = CreateEmployeeAccount(
            $"delegation-scope-{Guid.NewGuid():N}",
            "مفوّض ضمن النطاق"
        );
        string matchingVisitId;
        string foreignVisitId;

        using (var db = dbFactory.CreateDbContext())
        {
            db.Departments.AddRange(
                new Department
                {
                    Name = managedDepartmentName,
                    ManagerUsername = managerUsername,
                    ManagerDisplayName = "مدير النطاق",
                    IsActive = true,
                },
                new Department
                {
                    Name = otherDepartmentName,
                    ManagerUsername = $"delegation-other-{Guid.NewGuid():N}",
                    ManagerDisplayName = "مدير آخر",
                    IsActive = true,
                }
            );
            db.UserAccounts.Add(
                CreateDepartmentManagerAccount(
                    managerUsername,
                    managedDepartmentName,
                    "مدير النطاق"
                )
            );
            db.UserAccounts.Add(delegatee);
            matchingVisitId = CreatePendingVisitRecord(db, managedDepartmentName);
            foreignVisitId = CreatePendingVisitRecord(db, otherDepartmentName);
            db.SaveChanges();
        }

        var createResult = delegationService.CreateDelegation(
            new DelegationDefinitionInput
            {
                DelegatorUsername = managerUsername,
                DelegateeUsername = delegatee.Username,
                ScopeType = DelegationScopeTypes.Custom,
                PermissionKeys = new[] { AppPermissions.ViewVisits, AppPermissions.ApproveVisits },
                StartAt = AppClock.LocalNow.AddMinutes(-5),
                EndAt = AppClock.LocalNow.AddDays(1),
            },
            managerUsername
        );
        Require(createResult.Succeeded, "scoped delegation should be created successfully");

        UserAccount storedDelegatee;
        Visit matchingVisit;
        Visit foreignVisit;
        using (var db = dbFactory.CreateDbContext())
        {
            storedDelegatee = db
                .UserAccounts.AsNoTracking()
                .First(x => x.Username == delegatee.Username);
            matchingVisit = db.Visits.AsNoTracking().First(x => x.VisitId == matchingVisitId);
            foreignVisit = db.Visits.AsNoTracking().First(x => x.VisitId == foreignVisitId);
        }

        Require(
            accessControlService.CanApproveVisit(matchingVisit, storedDelegatee),
            "delegation should preserve the delegator department scope for matching visits"
        );
        Require(
            !accessControlService.CanApproveVisit(foreignVisit, storedDelegatee),
            "delegation should not leak approvals outside the delegator department scope"
        );

        visitService.UpdateVisitApprovalStatus(matchingVisitId, "Approved", delegatee.Username);
        visitService.UpdateVisitApprovalStatus(foreignVisitId, "Approved", delegatee.Username);

        using var verificationDb = dbFactory.CreateDbContext();
        Require(
            string.Equals(
                verificationDb
                    .Visits.AsNoTracking()
                    .First(x => x.VisitId == matchingVisitId)
                    .ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "delegatee should approve visits inside the delegated scope"
        );
        Require(
            string.Equals(
                verificationDb
                    .Visits.AsNoTracking()
                    .First(x => x.VisitId == foreignVisitId)
                    .ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "delegatee should not approve visits outside the delegated scope"
        );

        return Task.CompletedTask;
    }

    private static UserAccount CreateEmployeeAccount(string username, string displayName)
    {
        return new UserAccount
        {
            Username = username,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            DisplayName = displayName,
            FullName = displayName,
            Department = "الإدارة العامة",
            JobTitle = "موظف",
            PhoneNumber = "0555555522",
            Email = string.Empty,
            IsActive = true,
            Role = AppRoles.Employee,
            CanViewDashboard = true,
            CanViewVisits = false,
            CanApproveVisits = false,
            CanApproveDetainedVisit = false,
            CanViewPermits = false,
            CanApprovePermit = false,
        };
    }

    private static IReadOnlyList<string> GetPendingPermitNumbersForUser(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IAccessControlService accessControlService,
        string username
    )
    {
        using var db = dbFactory.CreateDbContext();
        var user = db.UserAccounts.AsNoTracking().First(x => x.Username == username);
        var pendingPermits = db
            .Permits.AsNoTracking()
            .Where(x => x.ApprovalStatus == "Pending" && !x.ArchivedAt.HasValue)
            .ToList();

        return pendingPermits
            .Where(permit => accessControlService.CanApprovePermit(permit, user))
            .Select(permit => permit.PermitNumber)
            .ToList();
    }

    private static IReadOnlyList<string> GetPendingVisitIdsForUser(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IAccessControlService accessControlService,
        string username
    )
    {
        using var db = dbFactory.CreateDbContext();
        var user = db.UserAccounts.AsNoTracking().First(x => x.Username == username);
        var pendingVisits = db
            .Visits.AsNoTracking()
            .Where(x => x.ApprovalStatus == "Pending")
            .ToList();

        return pendingVisits
            .Where(visit => accessControlService.CanApproveVisit(visit, user))
            .Select(visit => visit.VisitId)
            .ToList();
    }
}
