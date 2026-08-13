using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

namespace VehiclePermitSystemWeb.Services.Bootstrap
{
    public sealed class DatabaseBootstrapService : IDatabaseBootstrapService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;
        private readonly object _syncRoot = new();
        private bool _initialized;

        public DatabaseBootstrapService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IConfiguration configuration
        )
        {
            _dbContextFactory = dbContextFactory;
            _configuration = configuration;
        }

        public void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            lock (_syncRoot)
            {
                if (_initialized)
                {
                    return;
                }

                using var db = _dbContextFactory.CreateDbContext();
                InitializeDatabase(db);
                _initialized = true;
            }
        }

        private void InitializeDatabase(ApplicationDbContext db)
        {
            if (
                db.Database.ProviderName?.Contains(
                    "Npgsql",
                    StringComparison.OrdinalIgnoreCase
                ) == true
            )
            {
                db.Database.Migrate();
            }
            else
            {
                db.Database.EnsureCreated();
            }

            EnsureSqliteSchemaUpgrades(db);
            EnsureDefaultTenant(db);
            NormalizeLegacyTenantAccess(db);

            var existingUsers = db.UserAccounts.ToList();

            foreach (var user in existingUsers)
            {
                if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    user.Role = AppRoles.GeneralManager;
                }
                else if (string.Equals(user.Role, "Gate", StringComparison.OrdinalIgnoreCase))
                {
                    user.Role = AppRoles.GateSecurity;
                }

                if (LooksLikeLegacyUser(user))
                {
                    user.IsActive = true;
                    user.FullName = string.IsNullOrWhiteSpace(user.FullName)
                        ? user.DisplayName
                        : user.FullName;
                    AppPermissions.ApplyRoleDefaults(user);
                }

                ApplyPermissionUpgrades(user);
            }

            EnsureSingleSuperAdmin(existingUsers);

            // Password column removal/migration handled earlier in EnsureSqliteDropPasswordColumnIfExists.

            EnsurePermitAccessModes(db);

            if (!db.AdministrationSettings.Any())
            {
                db.AdministrationSettings.Add(BuildDefaultAdministrationSettings());
            }
            else
            {
                foreach (var settings in db.AdministrationSettings)
                {
                    NormalizeAdministrationSettings(settings);
                }
            }

            EnsureInitialSetupCompletionState(db, existingUsers.Any());

            EnsureDepartmentCatalog(db);

            db.SaveChanges();
        }

        private static void NormalizeLegacyTenantAccess(ApplicationDbContext db)
        {
            var tenants = db.Tenants.IgnoreQueryFilters().ToList();
            var occupiedSlugs = tenants
                .Select(tenant => tenant.Slug)
                .Where(slug => !string.IsNullOrWhiteSpace(slug))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var tenant in tenants.Where(tenant =>
                !string.IsNullOrWhiteSpace(tenant.Slug) && tenant.Slug.All(char.IsDigit)))
            {
                string slug;
                do
                {
                    slug = $"workspace-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant()}";
                } while (!occupiedSlugs.Add(slug));

                tenant.Slug = slug;
            }

            foreach (var tenant in tenants)
            {
                var settings = db.AdministrationSettings.IgnoreQueryFilters()
                    .FirstOrDefault(item => item.TenantId == tenant.TenantId);
                if (settings == null || !string.IsNullOrWhiteSpace(settings.GeneralManagerUsername))
                {
                    continue;
                }

                var users = db.UserAccounts.IgnoreQueryFilters()
                    .Where(user => user.TenantId == tenant.TenantId && user.IsActive && !user.IsSuperAdmin)
                    .Take(2)
                    .ToList();
                if (users.Count != 1)
                {
                    continue;
                }

                var owner = users[0];
                owner.Role = AppRoles.GeneralManager;
                owner.JobTitle = "مسؤول الجهة";
                owner.ManagerUsername = string.Empty;
                AppPermissions.ApplyRoleDefaults(owner);
                settings.GeneralManagerUsername = owner.Username;
                settings.ManagerName = string.IsNullOrWhiteSpace(owner.FullName)
                    ? owner.DisplayName
                    : owner.FullName;
                settings.ManagerTitle = "مسؤول الجهة";
                settings.ManagerPhoneNumber = owner.PhoneNumber;
                settings.Email = owner.Email;
                settings.IsInitialSetupCompleted = true;
            }
        }

        private static void EnsureInitialSetupCompletionState(
            ApplicationDbContext db,
            bool hasAnyUsers
        )
        {
            var settings = db
                .AdministrationSettings.OrderByDescending(x => x.Id == 1)
                .ThenBy(x => x.Id)
                .FirstOrDefault();
            if (settings == null)
            {
                return;
            }

            if (hasAnyUsers)
            {
                settings.IsInitialSetupCompleted = true;
            }
        }

        private static void EnsureDepartmentCatalog(ApplicationDbContext db)
        {
            var existingNames = db
                .Departments.AsNoTracking()
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var configuredNames = new[]
            {
                db
                    .AdministrationSettings.AsNoTracking()
                    .OrderByDescending(x => x.Id == 1)
                    .ThenBy(x => x.Id)
                    .FirstOrDefault()
                    ?.DepartmentName,
            }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var name in configuredNames)
            {
                if (existingNames.Add(name))
                {
                    db.Departments.Add(new Department { Name = name });
                }
            }

            if (!existingNames.Any())
            {
                db.Departments.Add(new Department { Name = "الإدارة العامة", IsActive = true });
            }
        }

        private void EnsureSqliteSchemaUpgrades(ApplicationDbContext db)
        {
            if (!db.Database.IsSqlite())
            {
                return;
            }
            // If the legacy plaintext `Password` column exists, remove it safely by
            // recreating the table without that column and copying data across.
            EnsureSqliteDropPasswordColumnIfExists(db);
            EnsureSqliteColumn(
                db,
                "Tenants",
                "PermitsServiceEnabled",
                "INTEGER NOT NULL DEFAULT 1"
            );
            EnsureSqliteColumn(
                db,
                "Tenants",
                "VisitsServiceEnabled",
                "INTEGER NOT NULL DEFAULT 1"
            );
            EnsureSqliteColumn(
                db,
                "Tenants",
                "SelfServiceEnabled",
                "INTEGER NOT NULL DEFAULT 1"
            );
            EnsureSqliteColumn(
                db,
                "Tenants",
                "QueueServiceEnabled",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(
                db,
                "Tenants",
                "GateServiceEnabled",
                "INTEGER NOT NULL DEFAULT 1"
            );
            EnsureSqliteColumn(db, "Tenants", "OrganizationReference", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "Tenants", "NotificationCenterEnabled", "INTEGER NOT NULL DEFAULT 1");
            EnsureSqliteColumn(db, "Tenants", "PermitNotificationsEnabled", "INTEGER NOT NULL DEFAULT 1");
            EnsureSqliteColumn(db, "Tenants", "VisitNotificationsEnabled", "INTEGER NOT NULL DEFAULT 1");
            EnsureSqliteColumn(db, "Tenants", "SecurityAlertsEnabled", "INTEGER NOT NULL DEFAULT 1");
            EnsureSqliteColumn(db, "Tenants", "FailedOperationAlertsEnabled", "INTEGER NOT NULL DEFAULT 1");
            EnsureSqliteColumn(db, "Tenants", "UnauthorizedMovementAlertsEnabled", "INTEGER NOT NULL DEFAULT 1");
            EnsureSqliteColumn(db, "Tenants", "NotificationRetentionDays", "INTEGER NOT NULL DEFAULT 90");
            EnsureSqliteColumn(db, "Tenants", "EmailOutboxRetentionDays", "INTEGER NOT NULL DEFAULT 30");
            EnsureSqliteColumn(db, "Tenants", "AuditLogRetentionDays", "INTEGER NOT NULL DEFAULT 365");
            EnsureSqliteColumn(db, "Tenants", "LastRetentionRunAtUtc", "TEXT NULL");
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "DisplayBaseUrl",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "DisplayAccessKey",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "IsInitialSetupCompleted",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "GeneralManagerUsername",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "ManagerPhoneNumber",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "WorkStartTime",
                "TEXT NOT NULL DEFAULT '08:00:00'"
            );
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "WorkEndTime",
                "TEXT NOT NULL DEFAULT '16:00:00'"
            );
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "AttendanceGraceMinutes",
                "INTEGER NOT NULL DEFAULT 15"
            );
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "WorkEndExitGraceMinutes",
                "INTEGER NOT NULL DEFAULT 30"
            );
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "LateReturnGraceMinutes",
                "INTEGER NOT NULL DEFAULT 5"
            );
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "LeaveRequestsEnabled",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "OfficialWorkDaysCsv",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(db, "AdministrationSettings", "LastWorkEndClosureAt", "TEXT NULL");
            EnsureSqliteColumn(db, "AdministrationSettings", "SignatureImageData", "BLOB NULL");
            EnsureSqliteColumn(
                db,
                "AdministrationSettings",
                "SignatureImageContentType",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(db, "Permits", "PermitDate", "TEXT NULL");
            EnsureSqliteColumn(db, "Permits", "ArchivedAt", "TEXT NULL");
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "MustChangePassword",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(db, "UserAccounts", "IsSuperAdmin", "INTEGER NOT NULL DEFAULT 0");
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "IsEmailConfirmed",
                "INTEGER NOT NULL DEFAULT 1"
            );
            EnsureSqliteColumn(db, "UserAccounts", "EmployeeNumber", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "UserAccounts", "OperatorBadgeCode", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "UserAccounts", "OperatorPinHash", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "UserAccounts", "OperatorPinSalt", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "MustChangeOperatorPin",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(db, "UserAccounts", "CanStopPermit", "INTEGER NOT NULL DEFAULT 0");
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "CanApproveVisits",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "CanReviewUnauthorizedExit",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "CanManageDepartments",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "CanManageDelegations",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(db, "Permits", "LeaveWindowStartAt", "TEXT NULL");
            EnsureSqliteColumn(db, "Permits", "LeaveWindowEndAt", "TEXT NULL");
            EnsureSqliteColumn(
                db,
                "Permits",
                "HasDailyLeaveSchedule",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(db, "Permits", "DailyLeaveScheduleStartDate", "TEXT NULL");
            EnsureSqliteColumn(db, "Permits", "DailyLeaveScheduleEndDate", "TEXT NULL");
            EnsureSqliteColumn(db, "Permits", "DailyLeaveScheduleExitMinutes", "INTEGER NULL");
            EnsureSqliteColumn(db, "Permits", "DailyLeaveScheduleReturnMinutes", "INTEGER NULL");
            EnsureSqliteColumn(
                db,
                "Permits",
                "DailyLeaveScheduleRequiresReturn",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(
                db,
                "Permits",
                "DailyLeaveScheduleReason",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(db, "Permits", "CreatedBy", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "Permits", "TemporaryExitPermissionUntil", "TEXT NULL");
            EnsureSqliteColumn(
                db,
                "Permits",
                "LateReturnWarningCount",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(
                db,
                "Permits",
                "LastLateReturnWarningForExpectedReturnTime",
                "TEXT NULL"
            );
            EnsureSqliteColumn(
                db,
                "Permits",
                "UnauthorizedExitWarningCount",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(db, "Permits", "LastUnauthorizedExitWarningAt", "TEXT NULL");
            EnsureSqliteColumn(db, "Permits", "PendingUnauthorizedExitAt", "TEXT NULL");
            EnsureSqliteColumn(
                db,
                "Permits",
                "PendingUnauthorizedExitSequenceId",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(db, "Permits", "LastAutomaticWorkEndExitAt", "TEXT NULL");
            EnsureSqliteColumn(db, "Permits", "PendingExitRequest", "INTEGER NOT NULL DEFAULT 0");
            EnsureSqliteColumn(db, "Permits", "LeaveReason", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "Permits", "CurrentState", "TEXT NOT NULL DEFAULT 'Outside'");
            EnsureSqliteColumn(db, "Permits", "VisitLocation", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "Permits", "WorkplaceSiteId", "INTEGER NULL");
            EnsureSqliteColumn(db, "Permits", "WorkplaceSiteEntranceId", "INTEGER NULL");
            EnsureSqliteColumn(db, "Visits", "WorkplaceSiteId", "INTEGER NULL");
            EnsureSqliteColumn(db, "Visits", "WorkplaceSiteEntranceId", "INTEGER NULL");
            EnsureSqliteColumn(db, "Permits", "PlateOrigin", "TEXT NOT NULL DEFAULT 'Saudi'");
            EnsureSqliteColumn(db, "Permits", "RequiresReturn", "INTEGER NOT NULL DEFAULT 1");
            EnsureSqliteColumn(db, "Permits", "AccessMode", "TEXT NOT NULL DEFAULT 'FullAccess'");
            EnsureSqliteColumn(db, "DisplayDevices", "Mode", "TEXT NOT NULL DEFAULT 'Gate'");
            EnsureSqliteColumn(db, "DisplayDevices", "WorkplaceSiteId", "INTEGER NULL");
            EnsureSqliteColumn(
                db,
                "DisplayDevices",
                "WorkplaceSiteEntranceId",
                "INTEGER NULL"
            );
            EnsureSqliteColumn(db, "DisplayDevices", "AppVersion", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "DisplayDevices", "Platform", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "DisplayDevices", "NetworkStatus", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "DisplayDevices", "CameraStatus", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "DisplayDevices", "BatteryLevel", "INTEGER NULL");
            EnsureSqliteColumn(db, "DisplayDevices", "LastHealthError", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "DisplayDevices", "LastHealthReportedAtUtc", "TEXT NULL");
            EnsureSqliteColumn(db, "DisplayDevices", "ConfigurationVersion", "INTEGER NOT NULL DEFAULT 1");
            EnsureSqliteColumn(db, "DisplayDevices", "AppliedConfigurationVersion", "INTEGER NOT NULL DEFAULT 0");
            db.Database.ExecuteSqlRaw(
                @"UPDATE Permits
                  SET AccessMode = CASE
                      WHEN TRIM(COALESCE(AccessMode, '')) <> '' THEN AccessMode
                      WHEN PermitType = 'Permanent' AND COALESCE(RequiresReturn, 1) = 0 THEN 'EntryOnly'
                      ELSE 'FullAccess'
                  END;"
            );
            EnsureSqliteColumn(db, "PermitActivities", "ReasonCode", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "PermitActivities", "LateMinutes", "INTEGER NULL");
            EnsureSqliteColumn(db, "PermitActivities", "SequenceId", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(
                db,
                "PermitActivities",
                "ClassificationStatus",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(db, "PermitActivities", "GateName", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(
                db,
                "PermitActivities",
                "GateOperatorName",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(
                db,
                "PermitActivities",
                "GateOperatorAccount",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(db, "PermitActivities", "DeviceId", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "PermitActivities", "IpAddress", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(
                db,
                "PermitActivities",
                "ExecutionMethod",
                "TEXT NOT NULL DEFAULT ''"
            );
            EnsureSqliteColumn(db, "PermitActivities", "IsAutomated", "INTEGER NOT NULL DEFAULT 0");
            db.Database.ExecuteSqlRaw(
                "CREATE TABLE IF NOT EXISTS Departments (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, ManagerUsername TEXT NOT NULL DEFAULT '', ManagerDisplayName TEXT NOT NULL DEFAULT '', IsActive INTEGER NOT NULL DEFAULT 1);"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Departments_Name ON Departments (Name);"
            );
            EnsureSqliteColumn(db, "Visits", "VisitLocation", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "Visits", "PhoneNumber", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "Visits", "VisitorEmail", "TEXT NULL");
            EnsureSqliteColumn(db, "Visits", "ApprovalStatus", "TEXT NOT NULL DEFAULT 'Approved'");
            EnsureSqliteColumn(db, "Visits", "VisitApproverUsername", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "Visits", "VisitedPersonName", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "Visits", "VisitedPersonType", "TEXT NOT NULL DEFAULT 'Host'");
            EnsureSqliteColumn(db, "Visits", "ArchivedAt", "TEXT NULL");
            EnsureSqliteColumn(db, "Visits", "RequestSource", "TEXT NOT NULL DEFAULT 'Internal'");
            EnsureSqliteColumn(db, "Visits", "RequestedAtUtc", "TEXT NULL");
            EnsureSqliteColumn(db, "Visits", "QueueStatus", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "Visits", "QueuedAtUtc", "TEXT NULL");
            EnsureSqliteColumn(db, "Visits", "CalledAtUtc", "TEXT NULL");
            EnsureSqliteColumn(db, "Visits", "ServiceStartedAtUtc", "TEXT NULL");
            EnsureSqliteColumn(db, "Visits", "QueueCompletedAtUtc", "TEXT NULL");
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_Visits_TenantId_QueueStatus ON Visits (TenantId, QueueStatus);"
            );
            EnsureSqliteColumn(db, "Permits", "HolderEmail", "TEXT NULL");
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "CanApproveLeaveRequest",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(db, "UserAccounts", "ManagerUsername", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "CanViewVisitorPermits",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "CanCreateVisitorPermit",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "CanEditVisitorPermit",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "CanApproveDetainedVisit",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteColumn(db, "UserAccounts", "CanStopPermit", "INTEGER NOT NULL DEFAULT 0");
            EnsureSqliteColumn(
                db,
                "UserAccounts",
                "CanReviewUnauthorizedExit",
                "INTEGER NOT NULL DEFAULT 0"
            );
            EnsureSqliteVisitCompanionsTable(db);
            db.Database.ExecuteSqlRaw(
                "UPDATE Visits SET VisitedPersonName = CASE WHEN TRIM(COALESCE(VisitedPersonName, '')) = '' THEN COALESCE(HostName, '') ELSE VisitedPersonName END;"
            );
            db.Database.ExecuteSqlRaw(
                @"UPDATE Visits
                  SET VisitedPersonType = CASE
                      WHEN TRIM(COALESCE(VisitedPersonType, '')) <> '' THEN VisitedPersonType
                      WHEN Purpose LIKE '%موقوف%' OR Purpose LIKE '%توقيف%' THEN 'Detained'
                      WHEN Purpose LIKE '%مسجون%' OR Purpose LIKE '%سجين%' OR Purpose LIKE '%نزيل%' THEN 'Prisoner'
                      ELSE 'Host'
                  END;"
            );
            EnsureSqlitePermitActivitiesTable(db);
            EnsureSqliteWorkplaceTables(db);
            EnsureSqlitePermitIndexes(db);
            EnsureSqliteUserActivitiesTable(db);
            EnsureSqliteAuditLogsTable(db);
            EnsureSqliteDelegationsTables(db);
            EnsureSqliteDisplayDeviceTables(db);
            EnsureSqlitePlatformSettingsTable(db);
            EnsureSqliteSubscriptionPlansTable(db);
            EnsureSqliteVisitorWorkflowSettingsTable(db);
            EnsureSqliteEmailNotificationOutboxTable(db);
            EnsureSqliteInAppNotificationsTable(db);
            EnsureSqliteTenancySchema(db);
        }

        private static void EnsureSqliteWorkplaceTables(ApplicationDbContext db)
        {
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS WorkplaceSites (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TenantId TEXT NOT NULL DEFAULT 'default',
                    Name TEXT NOT NULL,
                    Code TEXT NOT NULL DEFAULT '',
                    Address TEXT NOT NULL DEFAULT '',
                    Latitude REAL NULL,
                    Longitude REAL NULL,
                    GeofenceRadiusMeters INTEGER NOT NULL DEFAULT 150,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkplaceSites_TenantId_Name ON WorkplaceSites (TenantId, Name);"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_WorkplaceSites_TenantId_Code ON WorkplaceSites (TenantId, Code);"
            );
            EnsureSqliteColumn(db, "WorkplaceSites", "PermitsEnabled", "INTEGER NOT NULL DEFAULT 1");
            EnsureSqliteColumn(db, "WorkplaceSites", "VisitsEnabled", "INTEGER NOT NULL DEFAULT 1");
            EnsureSqliteColumn(db, "WorkplaceSites", "SelfServiceEnabled", "INTEGER NOT NULL DEFAULT 1");
            EnsureSqliteColumn(db, "WorkplaceSites", "QueueEnabled", "INTEGER NOT NULL DEFAULT 0");
            EnsureSqliteColumn(db, "WorkplaceSites", "GateEnabled", "INTEGER NOT NULL DEFAULT 1");
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS WorkplaceSiteEntrances (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TenantId TEXT NOT NULL DEFAULT 'default',
                    WorkplaceSiteId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Code TEXT NOT NULL DEFAULT '',
                    LocationDescription TEXT NOT NULL DEFAULT '',
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (WorkplaceSiteId) REFERENCES WorkplaceSites (Id) ON DELETE CASCADE
                );"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkplaceSiteEntrances_TenantId_WorkplaceSiteId_Name ON WorkplaceSiteEntrances (TenantId, WorkplaceSiteId, Name);"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_WorkplaceSiteEntrances_TenantId_WorkplaceSiteId_Code ON WorkplaceSiteEntrances (TenantId, WorkplaceSiteId, Code);"
            );
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS PersonProfiles (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TenantId TEXT NOT NULL DEFAULT 'default',
                    SourceKey TEXT NOT NULL,
                    PersonType TEXT NOT NULL DEFAULT 'Employee',
                    FullName TEXT NOT NULL DEFAULT '',
                    NationalId TEXT NOT NULL DEFAULT '',
                    PhoneNumber TEXT NOT NULL DEFAULT '',
                    Email TEXT NOT NULL DEFAULT '',
                    EmployeeNumber TEXT NOT NULL DEFAULT '',
                    Department TEXT NOT NULL DEFAULT '',
                    JobTitle TEXT NOT NULL DEFAULT '',
                    Organization TEXT NOT NULL DEFAULT '',
                    LastReference TEXT NOT NULL DEFAULT '',
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    LastSeenAtUtc TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_PersonProfiles_TenantId_SourceKey ON PersonProfiles (TenantId, SourceKey);"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_PersonProfiles_TenantId_PersonType ON PersonProfiles (TenantId, PersonType);"
            );
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS PersonPhotos (
                    PersonProfileId INTEGER NOT NULL PRIMARY KEY,
                    TenantId TEXT NOT NULL DEFAULT 'default',
                    Data BLOB NOT NULL,
                    ContentType TEXT NOT NULL DEFAULT 'image/jpeg',
                    UpdatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (PersonProfileId) REFERENCES PersonProfiles (Id) ON DELETE CASCADE
                );"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_PersonPhotos_TenantId ON PersonPhotos (TenantId);"
            );
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS EmergencySessions (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TenantId TEXT NOT NULL DEFAULT 'default',
                    WorkplaceSiteId INTEGER NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'Active',
                    StartedBy TEXT NOT NULL DEFAULT '',
                    StartedAtUtc TEXT NOT NULL,
                    EndedBy TEXT NOT NULL DEFAULT '',
                    EndedAtUtc TEXT NULL,
                    FOREIGN KEY (WorkplaceSiteId) REFERENCES WorkplaceSites (Id) ON DELETE RESTRICT
                );"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_EmergencySessions_TenantId_WorkplaceSiteId_Status ON EmergencySessions (TenantId, WorkplaceSiteId, Status);"
            );
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS EmergencySessionMembers (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TenantId TEXT NOT NULL DEFAULT 'default',
                    EmergencySessionId INTEGER NOT NULL,
                    PersonProfileId INTEGER NOT NULL,
                    FullName TEXT NOT NULL DEFAULT '',
                    PersonType TEXT NOT NULL DEFAULT 'Employee',
                    Reference TEXT NOT NULL DEFAULT '',
                    Location TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT 'Pending',
                    UpdatedBy TEXT NOT NULL DEFAULT '',
                    UpdatedAtUtc TEXT NULL,
                    FOREIGN KEY (EmergencySessionId) REFERENCES EmergencySessions (Id) ON DELETE CASCADE
                );"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_EmergencySessionMembers_TenantId_EmergencySessionId_Status ON EmergencySessionMembers (TenantId, EmergencySessionId, Status);"
            );
        }

        private static void EnsureDefaultTenant(ApplicationDbContext db)
        {
            var defaultTenant = db.Tenants.FirstOrDefault(tenant =>
                tenant.TenantId == TenantDefaults.DefaultTenantId
            );
            if (defaultTenant != null)
            {
                if (string.IsNullOrWhiteSpace(defaultTenant.Name))
                {
                    defaultTenant.Name = TenantDefaults.DefaultTenantName;
                }

                if (string.IsNullOrWhiteSpace(defaultTenant.Slug))
                {
                    defaultTenant.Slug = TenantDefaults.DefaultTenantId;
                }

                defaultTenant.IsActive = true;
                defaultTenant.SubscriptionStatus = TenantSubscriptionStatuses.Active;
                defaultTenant.PlanName = string.IsNullOrWhiteSpace(defaultTenant.PlanName)
                    ? TenantDefaults.DefaultPlanName
                    : defaultTenant.PlanName;
                return;
            }

            db.Tenants.Add(
                new Tenant
                {
                    TenantId = TenantDefaults.DefaultTenantId,
                    Name = TenantDefaults.DefaultTenantName,
                    Slug = TenantDefaults.DefaultTenantId,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    SubscriptionStatus = TenantSubscriptionStatuses.Active,
                    PlanName = TenantDefaults.DefaultPlanName,
                }
            );
        }

        private static void EnsureSqliteTenancySchema(ApplicationDbContext db)
        {
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS Tenants (
                    TenantId TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL DEFAULT '',
                    Slug TEXT NOT NULL DEFAULT '',
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAtUtc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    SubscriptionStatus TEXT NOT NULL DEFAULT 'Active',
                    PlanName TEXT NOT NULL DEFAULT 'أساسية',
                    SignupPlanCode TEXT NOT NULL DEFAULT '',
                    SignupPlanPrice TEXT NULL,
                    SignupPlanDurationMonths INTEGER NULL,
                    TrialEndsAtUtc TEXT NULL,
                    SubscriptionEndsAtUtc TEXT NULL,
                    MaxUsers INTEGER NULL,
                    MaxPermitsPerMonth INTEGER NULL,
                    MaxVisitsPerMonth INTEGER NULL
                );"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Tenants_Slug ON Tenants (Slug);"
            );
            EnsureSqliteColumn(
                db,
                "Tenants",
                "SubscriptionStatus",
                "TEXT NOT NULL DEFAULT 'Active'"
            );
            EnsureSqliteColumn(db, "Tenants", "PlanName", "TEXT NOT NULL DEFAULT 'أساسية'");
            EnsureSqliteColumn(db, "Tenants", "SignupPlanCode", "TEXT NOT NULL DEFAULT ''");
            EnsureSqliteColumn(db, "Tenants", "SignupPlanPrice", "TEXT NULL");
            EnsureSqliteColumn(db, "Tenants", "SignupPlanDurationMonths", "INTEGER NULL");
            EnsureSqliteColumn(db, "Tenants", "TrialEndsAtUtc", "TEXT NULL");
            EnsureSqliteColumn(db, "Tenants", "SubscriptionEndsAtUtc", "TEXT NULL");
            EnsureSqliteColumn(db, "Tenants", "SignupExpiresAtUtc", "TEXT NULL");
            EnsureSqliteColumn(db, "Tenants", "MaxUsers", "INTEGER NULL");
            EnsureSqliteColumn(db, "Tenants", "MaxPermitsPerMonth", "INTEGER NULL");
            EnsureSqliteColumn(db, "Tenants", "MaxVisitsPerMonth", "INTEGER NULL");
            db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_Tenants_PrimaryDomain;");
            RemoveSqliteColumnIfExists(db, "Tenants", "PrimaryDomain");

            foreach (var tableName in new[]
            {
                "AdministrationSettings",
                "Permits",
                "Visits",
                "VisitCompanions",
                "PermitActivities",
                "AuditLogs",
                "Departments",
                "UserAccounts",
                "UserActivities",
                "SessionRecords",
                "Delegations",
                "DelegationPermissions",
                "DisplayDevices",
                "DisplaySecuritySettings",
                "EmailNotificationOutbox",
                "InAppNotifications",
            })
            {
                EnsureSqliteColumn(
                    db,
                    tableName,
                    "TenantId",
                    $"TEXT NOT NULL DEFAULT '{TenantDefaults.DefaultTenantId}'"
                );
                db.Database.ExecuteSqlRaw(
                    "CREATE INDEX IF NOT EXISTS IX_"
                        + tableName
                        + "_TenantId ON "
                        + tableName
                        + " (TenantId);"
                );
            }

            db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_Departments_Name;");
            db.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Departments_TenantId_Name ON Departments (TenantId, Name);"
            );
        }

        private static void EnsureSqlitePlatformSettingsTable(ApplicationDbContext db)
        {
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS ""PlatformSettings"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PlatformSettings"" PRIMARY KEY AUTOINCREMENT,
                    ""ProviderName"" TEXT NOT NULL DEFAULT '',
                    ""CommercialRegistration"" TEXT NOT NULL DEFAULT '',
                    ""VatNumber"" TEXT NOT NULL DEFAULT '',
                    ""RegisteredAddress"" TEXT NOT NULL DEFAULT '',
                    ""City"" TEXT NOT NULL DEFAULT '',
                    ""Country"" TEXT NOT NULL DEFAULT '',
                    ""OfficialEmail"" TEXT NOT NULL DEFAULT '',
                    ""SupportPhone"" TEXT NOT NULL DEFAULT '',
                    ""WhatsAppNumber"" TEXT NOT NULL DEFAULT '',
                    ""WorkingHours"" TEXT NOT NULL DEFAULT '',
                    ""DataHostingLocation"" TEXT NOT NULL DEFAULT '',
                    ""BackupPolicy"" TEXT NOT NULL DEFAULT '',
                    ""UpdatedAtUtc"" TEXT NULL
                );"
            );
        }

        private static void EnsureSqliteSubscriptionPlansTable(ApplicationDbContext db)
        {
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS ""SubscriptionPlans"" (
                    ""Code"" TEXT NOT NULL CONSTRAINT ""PK_SubscriptionPlans"" PRIMARY KEY,
                    ""Name"" TEXT NOT NULL DEFAULT '',
                    ""Summary"" TEXT NOT NULL DEFAULT '',
                    ""DurationMonths"" INTEGER NOT NULL,
                    ""Price"" TEXT NOT NULL,
                    ""OriginalPrice"" TEXT NULL,
                    ""OfferLabel"" TEXT NOT NULL DEFAULT '',
                    ""OfferStartsAtUtc"" TEXT NULL,
                    ""OfferEndsAtUtc"" TEXT NULL,
                    ""IsFeatured"" INTEGER NOT NULL DEFAULT 0,
                    ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                    ""SortOrder"" INTEGER NOT NULL DEFAULT 0,
                    ""FeaturesJson"" TEXT NOT NULL DEFAULT '[]',
                    ""UpdatedAtUtc"" TEXT NOT NULL
                );"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_SubscriptionPlans_SortOrder ON SubscriptionPlans (SortOrder);"
            );
        }

        private static void EnsureSqliteVisitorWorkflowSettingsTable(ApplicationDbContext db)
        {
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS ""VisitorWorkflowSettings"" (
                    ""TenantId"" TEXT NOT NULL CONSTRAINT ""PK_VisitorWorkflowSettings"" PRIMARY KEY,
                    ""TemplateKey"" TEXT NOT NULL DEFAULT 'Standard',
                    ""IsEnabled"" INTEGER NOT NULL DEFAULT 1,
                    ""ShowNationalId"" INTEGER NOT NULL DEFAULT 1,
                    ""RequireNationalId"" INTEGER NOT NULL DEFAULT 0,
                    ""ShowHostName"" INTEGER NOT NULL DEFAULT 1,
                    ""RequireHostName"" INTEGER NOT NULL DEFAULT 1,
                    ""ShowVisitLocation"" INTEGER NOT NULL DEFAULT 1,
                    ""RequireVisitLocation"" INTEGER NOT NULL DEFAULT 1,
                    ""ShowPurpose"" INTEGER NOT NULL DEFAULT 1,
                    ""RequirePurpose"" INTEGER NOT NULL DEFAULT 1,
                    ""MinimumLeadMinutes"" INTEGER NOT NULL DEFAULT 5,
                    ""MaximumAdvanceDays"" INTEGER NOT NULL DEFAULT 30,
                    ""WelcomeMessage"" TEXT NOT NULL DEFAULT '',
                    ""UpdatedAtUtc"" TEXT NOT NULL
                );"
            );
        }

        private static void EnsureSqliteEmailNotificationOutboxTable(
            ApplicationDbContext db
        )
        {
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS ""EmailNotificationOutbox"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_EmailNotificationOutbox"" PRIMARY KEY AUTOINCREMENT,
                    ""TenantId"" TEXT NOT NULL DEFAULT 'default',
                    ""NotificationType"" TEXT NOT NULL DEFAULT '',
                    ""ReferenceType"" TEXT NOT NULL DEFAULT '',
                    ""ReferenceId"" TEXT NOT NULL DEFAULT '',
                    ""DeduplicationKey"" TEXT NOT NULL DEFAULT '',
                    ""RecipientEmail"" TEXT NOT NULL DEFAULT '',
                    ""RecipientName"" TEXT NOT NULL DEFAULT '',
                    ""Subject"" TEXT NOT NULL DEFAULT '',
                    ""HtmlBody"" TEXT NOT NULL DEFAULT '',
                    ""TextBody"" TEXT NOT NULL DEFAULT '',
                    ""Status"" TEXT NOT NULL DEFAULT 'Pending',
                    ""AttemptCount"" INTEGER NOT NULL DEFAULT 0,
                    ""CreatedAtUtc"" TEXT NOT NULL,
                    ""NextAttemptAtUtc"" TEXT NOT NULL,
                    ""LastAttemptAtUtc"" TEXT NULL,
                    ""SentAtUtc"" TEXT NULL,
                    ""LockToken"" TEXT NOT NULL DEFAULT '',
                    ""LockedAtUtc"" TEXT NULL,
                    ""LastError"" TEXT NOT NULL DEFAULT ''
                );"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_EmailNotificationOutbox_TenantId_DeduplicationKey ON EmailNotificationOutbox (TenantId, DeduplicationKey);"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_EmailNotificationOutbox_Status_NextAttemptAtUtc ON EmailNotificationOutbox (Status, NextAttemptAtUtc);"
            );
        }

        private static void EnsureSqliteInAppNotificationsTable(ApplicationDbContext db)
        {
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS ""InAppNotifications"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_InAppNotifications"" PRIMARY KEY AUTOINCREMENT,
                    ""TenantId"" TEXT NOT NULL DEFAULT 'default',
                    ""RecipientUsername"" TEXT NOT NULL DEFAULT '',
                    ""Category"" TEXT NOT NULL DEFAULT 'System',
                    ""Severity"" TEXT NOT NULL DEFAULT 'Info',
                    ""Title"" TEXT NOT NULL DEFAULT '',
                    ""Message"" TEXT NOT NULL DEFAULT '',
                    ""ActionUrl"" TEXT NOT NULL DEFAULT '',
                    ""SourceKey"" TEXT NOT NULL DEFAULT '',
                    ""OccurredAtUtc"" TEXT NOT NULL,
                    ""CreatedAtUtc"" TEXT NOT NULL,
                    ""ReadAtUtc"" TEXT NULL,
                    ""DismissedAtUtc"" TEXT NULL
                );"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_InAppNotifications_TenantId_RecipientUsername_SourceKey ON InAppNotifications (TenantId, RecipientUsername, SourceKey);"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_InAppNotifications_TenantId_RecipientUsername_DismissedAtUtc_OccurredAtUtc ON InAppNotifications (TenantId, RecipientUsername, DismissedAtUtc, OccurredAtUtc);"
            );
        }

        private static void EnsureSqliteDisplayDeviceTables(ApplicationDbContext db)
        {
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS DisplayDevices (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ScreenName TEXT NOT NULL DEFAULT '',
                    ScreenLocation TEXT NOT NULL DEFAULT '',
                    Description TEXT NOT NULL DEFAULT '',
                    Mode TEXT NOT NULL DEFAULT 'Gate',
                    Status TEXT NOT NULL DEFAULT 'Pending',
                    DeviceTokenHash TEXT NOT NULL DEFAULT '',
                    RequestCode TEXT NOT NULL DEFAULT '',
                    IpAddress TEXT NOT NULL DEFAULT '',
                    LastIpAddress TEXT NOT NULL DEFAULT '',
                    UserAgent TEXT NOT NULL DEFAULT '',
                    CreatedAtUtc TEXT NOT NULL,
                    ApprovedAtUtc TEXT NULL,
                    ApprovedByUserId TEXT NOT NULL DEFAULT '',
                    LastSeenUtc TEXT NULL,
                    DisabledAtUtc TEXT NULL,
                    Notes TEXT NOT NULL DEFAULT ''
                );"
            );
            db.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_DisplayDevices_RequestCode ON DisplayDevices (RequestCode);"
            );
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS DisplaySecuritySettings (
                    Id INTEGER NOT NULL PRIMARY KEY,
                    SetupKeyHash TEXT NOT NULL DEFAULT '',
                    RequireAdminApproval INTEGER NOT NULL DEFAULT 1,
                    HeartbeatSeconds INTEGER NOT NULL DEFAULT 30,
                    DeviceCookieDays INTEGER NOT NULL DEFAULT 365
                );"
            );
        }

        private static void EnsurePermitAccessModes(ApplicationDbContext db)
        {
            foreach (var permit in db.Permits)
            {
                permit.NormalizeAccessModeState();
                if (!permit.IsFullAccessPermit)
                {
                    continue;
                }

                permit.PendingUnauthorizedExitAt = null;
                permit.PendingUnauthorizedExitSequenceId = string.Empty;
                permit.UnauthorizedExitWarningCount = 0;
                permit.LastUnauthorizedExitWarningAt = null;
                permit.LateReturnWarningCount = 0;
                permit.LastLateReturnWarningForExpectedReturnTime = null;
            }
        }

        private static void EnsureSingleSuperAdmin(IReadOnlyCollection<UserAccount> users)
        {
            var selectedSuperAdmin = users.FirstOrDefault(user => user.IsSuperAdmin);
            if (selectedSuperAdmin == null)
            {
                return;
            }

            foreach (var user in users)
            {
                user.IsSuperAdmin = string.Equals(
                    user.Username,
                    selectedSuperAdmin.Username,
                    StringComparison.OrdinalIgnoreCase
                );

                if (!user.IsSuperAdmin)
                {
                    continue;
                }

                user.Role = AppRoles.SystemAdmin;
                user.IsActive = true;
                user.ManagerUsername = string.Empty;
                user.Department = string.Empty;
                AppPermissions.ApplyRoleDefaults(user);
            }
        }

        private static void MigrateUsernameReferences(
            ApplicationDbContext db,
            string oldUsername,
            string newUsername,
            string newDisplayName
        )
        {
            foreach (
                var account in db.UserAccounts.Where(account =>
                    account.ManagerUsername == oldUsername
                )
            )
            {
                account.ManagerUsername = newUsername;
            }

            foreach (
                var department in db.Departments.Where(department =>
                    department.ManagerUsername == oldUsername
                )
            )
            {
                department.ManagerUsername = newUsername;
                department.ManagerDisplayName = newDisplayName;
            }

            var settings = db.AdministrationSettings.OrderBy(settings => settings.Id).FirstOrDefault();
            if (settings != null && settings.GeneralManagerUsername == oldUsername)
            {
                settings.GeneralManagerUsername = newUsername;
                settings.ManagerName = newDisplayName;
            }

            foreach (
                var delegation in db.Delegations.Where(delegation =>
                    delegation.DelegatorUsername == oldUsername
                    || delegation.DelegateeUsername == oldUsername
                    || delegation.CreatedByUsername == oldUsername
                    || delegation.LastUpdatedByUsername == oldUsername
                    || delegation.CancelledByUsername == oldUsername
                )
            )
            {
                if (delegation.DelegatorUsername == oldUsername)
                {
                    delegation.DelegatorUsername = newUsername;
                }

                if (delegation.DelegateeUsername == oldUsername)
                {
                    delegation.DelegateeUsername = newUsername;
                }

                if (delegation.CreatedByUsername == oldUsername)
                {
                    delegation.CreatedByUsername = newUsername;
                }

                if (delegation.LastUpdatedByUsername == oldUsername)
                {
                    delegation.LastUpdatedByUsername = newUsername;
                }

                if (delegation.CancelledByUsername == oldUsername)
                {
                    delegation.CancelledByUsername = newUsername;
                }
            }

            foreach (
                var activity in db.UserActivities.Where(activity =>
                    activity.Username == oldUsername || activity.RecordedBy == oldUsername
                )
            )
            {
                if (activity.Username == oldUsername)
                {
                    activity.Username = newUsername;
                }

                if (activity.RecordedBy == oldUsername)
                {
                    activity.RecordedBy = newUsername;
                }
            }

            foreach (
                var auditLog in db.AuditLogs.Where(auditLog =>
                    auditLog.Username == oldUsername
                    || auditLog.RecordedBy == oldUsername
                    || auditLog.ActualActorUsername == oldUsername
                    || auditLog.DelegatedFromUsername == oldUsername
                )
            )
            {
                if (auditLog.Username == oldUsername)
                {
                    auditLog.Username = newUsername;
                }

                if (auditLog.RecordedBy == oldUsername)
                {
                    auditLog.RecordedBy = newUsername;
                }

                if (auditLog.ActualActorUsername == oldUsername)
                {
                    auditLog.ActualActorUsername = newUsername;
                }

                if (auditLog.DelegatedFromUsername == oldUsername)
                {
                    auditLog.DelegatedFromUsername = newUsername;
                }
            }

            foreach (
                var permitActivity in db.PermitActivities.Where(activity =>
                    activity.RecordedBy == oldUsername
                    || activity.GateOperatorAccount == oldUsername
                )
            )
            {
                if (permitActivity.RecordedBy == oldUsername)
                {
                    permitActivity.RecordedBy = newUsername;
                }

                if (permitActivity.GateOperatorAccount == oldUsername)
                {
                    permitActivity.GateOperatorAccount = newUsername;
                }
            }

            foreach (var permit in db.Permits.Where(permit => permit.CreatedBy == oldUsername))
            {
                permit.CreatedBy = newUsername;
            }

            foreach (
                var visit in db.Visits.Where(visit => visit.VisitApproverUsername == oldUsername)
            )
            {
                visit.VisitApproverUsername = newUsername;
            }

            foreach (
                var session in db.SessionRecords.Where(session => session.Username == oldUsername)
            )
            {
                session.Username = newUsername;
            }
        }

        private static void EnsureSqliteVisitCompanionsTable(ApplicationDbContext db)
        {
            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
            if (shouldCloseConnection)
            {
                connection.Open();
            }

            try
            {
                using var createCommand = connection.CreateCommand();
                createCommand.CommandText =
                    @"CREATE TABLE IF NOT EXISTS ""VisitCompanions"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_VisitCompanions"" PRIMARY KEY AUTOINCREMENT,
                        ""VisitId"" TEXT NOT NULL DEFAULT '',
                        ""FullName"" TEXT NOT NULL DEFAULT '',
                        ""NationalId"" TEXT NOT NULL DEFAULT '',
                        ""PhoneNumber"" TEXT NOT NULL DEFAULT '',
                        ""Relationship"" TEXT NOT NULL DEFAULT '',
                        ""SortOrder"" INTEGER NOT NULL DEFAULT 0,
                        ""EntryTime"" TEXT NULL,
                        ""ExitTime"" TEXT NULL,
                        CONSTRAINT ""FK_VisitCompanions_Visits_VisitId"" FOREIGN KEY (""VisitId"") REFERENCES ""Visits"" (""VisitId"") ON DELETE CASCADE
                    );";
                createCommand.ExecuteNonQuery();

                using var indexCommand = connection.CreateCommand();
                indexCommand.CommandText =
                    @"CREATE INDEX IF NOT EXISTS ""IX_VisitCompanions_VisitId""
                      ON ""VisitCompanions"" (""VisitId"");";
                indexCommand.ExecuteNonQuery();
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    connection.Close();
                }
            }
        }

        private static void EnsureSqlitePermitIndexes(ApplicationDbContext db)
        {
            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
            if (shouldCloseConnection)
            {
                connection.Open();
            }

            try
            {
                using var permitIndexCommand = connection.CreateCommand();
                permitIndexCommand.CommandText =
                    @"CREATE INDEX IF NOT EXISTS ""IX_Permits_PermitNumber""
                      ON ""Permits"" (""PermitNumber"");";
                permitIndexCommand.ExecuteNonQuery();

                using var permitArchivedAtIndexCommand = connection.CreateCommand();
                permitArchivedAtIndexCommand.CommandText =
                    @"CREATE INDEX IF NOT EXISTS ""IX_Permits_ArchivedAt""
                      ON ""Permits"" (""ArchivedAt"");";
                permitArchivedAtIndexCommand.ExecuteNonQuery();

                using var activityPermitIndexCommand = connection.CreateCommand();
                activityPermitIndexCommand.CommandText =
                    @"CREATE INDEX IF NOT EXISTS ""IX_PermitActivities_PermitNumber_OccurredAt""
                      ON ""PermitActivities"" (""PermitNumber"", ""OccurredAt"" DESC);";
                activityPermitIndexCommand.ExecuteNonQuery();

                using var activityOccurredIndexCommand = connection.CreateCommand();
                activityOccurredIndexCommand.CommandText =
                    @"CREATE INDEX IF NOT EXISTS ""IX_PermitActivities_OccurredAt""
                      ON ""PermitActivities"" (""OccurredAt"" DESC);";
                activityOccurredIndexCommand.ExecuteNonQuery();
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    connection.Close();
                }
            }
        }

        private static void EnsureSqlitePermitActivitiesTable(ApplicationDbContext db)
        {
            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
            if (shouldCloseConnection)
            {
                connection.Open();
            }

            try
            {
                using var createCommand = connection.CreateCommand();
                createCommand.CommandText =
                    @"CREATE TABLE IF NOT EXISTS ""PermitActivities"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PermitActivities"" PRIMARY KEY AUTOINCREMENT,
                        ""PermitNumber"" TEXT NOT NULL DEFAULT '',
                        ""DriverName"" TEXT NOT NULL DEFAULT '',
                        ""NationalId"" TEXT NOT NULL DEFAULT '',
                        ""DepartmentName"" TEXT NOT NULL DEFAULT '',
                        ""ActionType"" TEXT NOT NULL DEFAULT '',
                        ""ActionLabel"" TEXT NOT NULL DEFAULT '',
                        ""Message"" TEXT NOT NULL DEFAULT '',
                        ""Source"" TEXT NOT NULL DEFAULT '',
                        ""RecordedBy"" TEXT NOT NULL DEFAULT '',
                        ""ReasonCode"" TEXT NOT NULL DEFAULT '',
                        ""SequenceId"" TEXT NOT NULL DEFAULT '',
                        ""ClassificationStatus"" TEXT NOT NULL DEFAULT '',
                        ""GateName"" TEXT NOT NULL DEFAULT '',
                        ""GateOperatorName"" TEXT NOT NULL DEFAULT '',
                        ""GateOperatorAccount"" TEXT NOT NULL DEFAULT '',
                        ""DeviceId"" TEXT NOT NULL DEFAULT '',
                        ""IpAddress"" TEXT NOT NULL DEFAULT '',
                        ""ExecutionMethod"" TEXT NOT NULL DEFAULT '',
                        ""IsAutomated"" INTEGER NOT NULL DEFAULT 0,
                        ""OccurredAt"" TEXT NOT NULL,
                        ""LateMinutes"" INTEGER NULL
                    );";
                createCommand.ExecuteNonQuery();
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    connection.Close();
                }
            }
        }

        private static void EnsureSqliteUserActivitiesTable(ApplicationDbContext db)
        {
            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
            if (shouldCloseConnection)
            {
                connection.Open();
            }

            try
            {
                using var createCommand = connection.CreateCommand();
                createCommand.CommandText =
                    @"CREATE TABLE IF NOT EXISTS ""UserActivities"" (
                            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_UserActivities"" PRIMARY KEY AUTOINCREMENT,
                            ""Username"" TEXT NOT NULL DEFAULT '',
                            ""DisplayName"" TEXT NOT NULL DEFAULT '',
                            ""ActionType"" TEXT NOT NULL DEFAULT '',
                            ""ActionLabel"" TEXT NOT NULL DEFAULT '',
                            ""Message"" TEXT NOT NULL DEFAULT '',
                            ""Source"" TEXT NOT NULL DEFAULT '',
                            ""RecordedBy"" TEXT NOT NULL DEFAULT '',
                            ""OccurredAt"" TEXT NOT NULL
                        );";
                createCommand.ExecuteNonQuery();

                using var indexCommand = connection.CreateCommand();
                indexCommand.CommandText =
                    @"CREATE INDEX IF NOT EXISTS ""IX_UserActivities_OccurredAt""
                          ON ""UserActivities"" (""OccurredAt"" DESC);";
                indexCommand.ExecuteNonQuery();
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    connection.Close();
                }
            }
        }

        private static void EnsureSqliteAuditLogsTable(ApplicationDbContext db)
        {
            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
            if (shouldCloseConnection)
            {
                connection.Open();
            }

            try
            {
                using var createCommand = connection.CreateCommand();
                createCommand.CommandText =
                    @"CREATE TABLE IF NOT EXISTS ""AuditLogs"" (
                            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_AuditLogs"" PRIMARY KEY AUTOINCREMENT,
                            ""Username"" TEXT NOT NULL DEFAULT '',
                            ""ActionType"" TEXT NOT NULL DEFAULT '',
                            ""ActionLabel"" TEXT NOT NULL DEFAULT '',
                            ""EntityType"" TEXT NOT NULL DEFAULT '',
                            ""EntityId"" TEXT NOT NULL DEFAULT '',
                            ""Message"" TEXT NOT NULL DEFAULT '',
                            ""Source"" TEXT NOT NULL DEFAULT '',
                            ""RecordedBy"" TEXT NOT NULL DEFAULT '',
                            ""OccurredAt"" TEXT NOT NULL,
                            ""IpAddress"" TEXT NOT NULL DEFAULT '',
                            ""Success"" INTEGER NOT NULL DEFAULT 1,
                            ""ActualActorUsername"" TEXT NOT NULL DEFAULT '',
                            ""ActedUnderDelegation"" INTEGER NOT NULL DEFAULT 0,
                            ""DelegatedFromUsername"" TEXT NOT NULL DEFAULT '',
                            ""DelegationId"" INTEGER NULL,
                            ""BeforeJson"" TEXT NOT NULL DEFAULT '',
                            ""AfterJson"" TEXT NOT NULL DEFAULT ''
                        );";
                createCommand.ExecuteNonQuery();

                using var occurredAtIndexCommand = connection.CreateCommand();
                occurredAtIndexCommand.CommandText =
                    @"CREATE INDEX IF NOT EXISTS ""IX_AuditLogs_OccurredAt""
                          ON ""AuditLogs"" (""OccurredAt"" DESC);";
                occurredAtIndexCommand.ExecuteNonQuery();

                using var entityIndexCommand = connection.CreateCommand();
                entityIndexCommand.CommandText =
                    @"CREATE INDEX IF NOT EXISTS ""IX_AuditLogs_EntityType_EntityId""
                          ON ""AuditLogs"" (""EntityType"", ""EntityId"");";
                entityIndexCommand.ExecuteNonQuery();
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    connection.Close();
                }
            }
        }

        private static void EnsureSqliteDelegationsTables(ApplicationDbContext db)
        {
            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
            if (shouldCloseConnection)
            {
                connection.Open();
            }

            try
            {
                using var createDelegationsCommand = connection.CreateCommand();
                createDelegationsCommand.CommandText =
                    @"CREATE TABLE IF NOT EXISTS ""Delegations"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Delegations"" PRIMARY KEY AUTOINCREMENT,
                        ""DelegationNumber"" TEXT NOT NULL DEFAULT '',
                        ""DelegatorUsername"" TEXT NOT NULL DEFAULT '',
                        ""DelegateeUsername"" TEXT NOT NULL DEFAULT '',
                        ""ScopeType"" TEXT NOT NULL DEFAULT 'Custom',
                        ""StartAt"" TEXT NOT NULL,
                        ""EndAt"" TEXT NOT NULL,
                        ""TimeZoneId"" TEXT NOT NULL DEFAULT '',
                        ""Status"" TEXT NOT NULL DEFAULT 'Scheduled',
                        ""Notes"" TEXT NOT NULL DEFAULT '',
                        ""CreatedByUsername"" TEXT NOT NULL DEFAULT '',
                        ""CreatedAt"" TEXT NOT NULL,
                        ""UpdatedAt"" TEXT NULL,
                        ""LastUpdatedByUsername"" TEXT NOT NULL DEFAULT '',
                        ""ActivatedAt"" TEXT NULL,
                        ""CancelledAt"" TEXT NULL,
                        ""CancelledByUsername"" TEXT NOT NULL DEFAULT '',
                        ""CancelReason"" TEXT NOT NULL DEFAULT ''
                    );";
                createDelegationsCommand.ExecuteNonQuery();

                using var createPermissionsCommand = connection.CreateCommand();
                createPermissionsCommand.CommandText =
                    @"CREATE TABLE IF NOT EXISTS ""DelegationPermissions"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_DelegationPermissions"" PRIMARY KEY AUTOINCREMENT,
                        ""DelegationId"" INTEGER NOT NULL,
                        ""PermissionKey"" TEXT NOT NULL DEFAULT '',
                        CONSTRAINT ""FK_DelegationPermissions_Delegations_DelegationId""
                            FOREIGN KEY (""DelegationId"") REFERENCES ""Delegations"" (""Id"") ON DELETE CASCADE
                    );";
                createPermissionsCommand.ExecuteNonQuery();

                using var delegationNumberIndexCommand = connection.CreateCommand();
                delegationNumberIndexCommand.CommandText =
                    @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Delegations_DelegationNumber""
                      ON ""Delegations"" (""DelegationNumber"");";
                delegationNumberIndexCommand.ExecuteNonQuery();

                using var delegateeStatusIndexCommand = connection.CreateCommand();
                delegateeStatusIndexCommand.CommandText =
                    @"CREATE INDEX IF NOT EXISTS ""IX_Delegations_DelegateeUsername_Status""
                      ON ""Delegations"" (""DelegateeUsername"", ""Status"");";
                delegateeStatusIndexCommand.ExecuteNonQuery();

                using var delegatorStatusIndexCommand = connection.CreateCommand();
                delegatorStatusIndexCommand.CommandText =
                    @"CREATE INDEX IF NOT EXISTS ""IX_Delegations_DelegatorUsername_Status""
                      ON ""Delegations"" (""DelegatorUsername"", ""Status"");";
                delegatorStatusIndexCommand.ExecuteNonQuery();

                using var delegationPermissionIndexCommand = connection.CreateCommand();
                delegationPermissionIndexCommand.CommandText =
                    @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_DelegationPermissions_DelegationId_PermissionKey""
                      ON ""DelegationPermissions"" (""DelegationId"", ""PermissionKey"");";
                delegationPermissionIndexCommand.ExecuteNonQuery();

                using var externalLoginCommand = connection.CreateCommand();
                externalLoginCommand.CommandText =
                    @"CREATE TABLE IF NOT EXISTS ""ExternalUserLogins"" (
                        ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""TenantId"" TEXT NOT NULL,
                        ""Username"" TEXT NOT NULL,
                        ""Provider"" TEXT NOT NULL,
                        ""Issuer"" TEXT NOT NULL,
                        ""Subject"" TEXT NOT NULL,
                        ""EmailAtLinkTime"" TEXT NOT NULL,
                        ""LinkedAtUtc"" TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ExternalUserLogins_Provider_Issuer_Subject""
                        ON ""ExternalUserLogins"" (""Provider"", ""Issuer"", ""Subject"");
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ExternalUserLogins_TenantId_Username_Provider""
                        ON ""ExternalUserLogins"" (""TenantId"", ""Username"", ""Provider"");";
                externalLoginCommand.ExecuteNonQuery();

                using var loginAttemptCommand = connection.CreateCommand();
                loginAttemptCommand.CommandText =
                    @"CREATE TABLE IF NOT EXISTS ""LoginAttemptRecords"" (
                        ""KeyHash"" TEXT NOT NULL PRIMARY KEY,
                        ""FailureCount"" INTEGER NOT NULL,
                        ""WindowStartedAtUtc"" TEXT NOT NULL,
                        ""BlockedUntilUtc"" TEXT NULL,
                        ""UpdatedAtUtc"" TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS ""SignupAttemptRecords"" (
                        ""KeyHash"" TEXT NOT NULL PRIMARY KEY,
                        ""SuccessCount"" INTEGER NOT NULL,
                        ""WindowStartedAtUtc"" TEXT NOT NULL,
                        ""UpdatedAtUtc"" TEXT NOT NULL
                    );";
                loginAttemptCommand.ExecuteNonQuery();

                EnsureSqliteColumn(
                    db,
                    "AuditLogs",
                    "ActualActorUsername",
                    "TEXT NOT NULL DEFAULT ''"
                );
                EnsureSqliteColumn(
                    db,
                    "AuditLogs",
                    "ActedUnderDelegation",
                    "INTEGER NOT NULL DEFAULT 0"
                );
                EnsureSqliteColumn(
                    db,
                    "AuditLogs",
                    "DelegatedFromUsername",
                    "TEXT NOT NULL DEFAULT ''"
                );
                EnsureSqliteColumn(db, "AuditLogs", "DelegationId", "INTEGER NULL");
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    connection.Close();
                }
            }
        }

        private static void EnsureSqliteDropPasswordColumnIfExists(ApplicationDbContext db)
        {
            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
            if (shouldCloseConnection)
            {
                connection.Open();
            }

            try
            {
                using var pragmaCommand = connection.CreateCommand();
                pragmaCommand.CommandText = "PRAGMA table_info(\"UserAccounts\")";
                var columns =
                    new List<(string name, string type, int notnull, string dflt_value, int pk)>();
                using (var reader = pragmaCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var name = reader.GetString(1);
                        var type = reader.GetString(2);
                        var notnull = reader.GetInt32(3);
                        var dflt = reader.IsDBNull(4) ? null : reader.GetString(4);
                        var pk = reader.GetInt32(5);
                        columns.Add((name, type, notnull, dflt ?? string.Empty, pk));
                    }
                }

                if (
                    !columns.Any(c =>
                        string.Equals(c.name, "Password", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    return; // nothing to do
                }

                // Build CREATE TABLE for new table without Password column
                var newColumns = columns
                    .Where(c =>
                        !string.Equals(c.name, "Password", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToList();
                var createColsSql = string.Join(
                    ", ",
                    newColumns.Select(c =>
                    {
                        var part = $"\"{c.name}\" {c.type}";
                        if (c.notnull == 1)
                            part += " NOT NULL";
                        if (!string.IsNullOrEmpty(c.dflt_value))
                            part += $" DEFAULT {c.dflt_value}";
                        if (c.pk == 1)
                            part += " PRIMARY KEY";
                        return part;
                    })
                );

                using var createCmd = connection.CreateCommand();
                createCmd.CommandText =
                    $"CREATE TABLE IF NOT EXISTS \"UserAccounts_new\" ({createColsSql});";
                createCmd.ExecuteNonQuery();

                // Copy data across
                var colList = string.Join(", ", newColumns.Select(c => $"\"{c.name}\""));
                using var copyCmd = connection.CreateCommand();
                copyCmd.CommandText =
                    $"INSERT INTO \"UserAccounts_new\" ({colList}) SELECT {colList} FROM \"UserAccounts\";";
                copyCmd.ExecuteNonQuery();

                using var dropCmd = connection.CreateCommand();
                dropCmd.CommandText = "DROP TABLE \"UserAccounts\";";
                dropCmd.ExecuteNonQuery();

                using var renameCmd = connection.CreateCommand();
                renameCmd.CommandText =
                    "ALTER TABLE \"UserAccounts_new\" RENAME TO \"UserAccounts\";";
                renameCmd.ExecuteNonQuery();
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    connection.Close();
                }
            }
        }

        private static void EnsureSqliteColumn(
            ApplicationDbContext db,
            string tableName,
            string columnName,
            string columnDefinition
        )
        {
            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
            if (shouldCloseConnection)
            {
                connection.Open();
            }

            try
            {
                using var pragmaCommand = connection.CreateCommand();
                pragmaCommand.CommandText = $"PRAGMA table_info(\"{tableName}\")";

                using var reader = pragmaCommand.ExecuteReader();
                while (reader.Read())
                {
                    var existingColumnName = reader.GetString(1);
                    if (
                        string.Equals(
                            existingColumnName,
                            columnName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return;
                    }
                }

                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText =
                    $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnDefinition}";
                alterCommand.ExecuteNonQuery();
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    connection.Close();
                }
            }
        }

        private static void RemoveSqliteColumnIfExists(
            ApplicationDbContext db,
            string tableName,
            string columnName
        )
        {
            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
            if (shouldCloseConnection)
            {
                connection.Open();
            }

            try
            {
                var columnExists = false;
                using (var pragmaCommand = connection.CreateCommand())
                {
                    pragmaCommand.CommandText = $"PRAGMA table_info(\"{tableName}\")";
                    using var reader = pragmaCommand.ExecuteReader();
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            columnExists = true;
                            break;
                        }
                    }
                }

                if (!columnExists)
                {
                    return;
                }

                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText =
                    $"ALTER TABLE \"{tableName}\" DROP COLUMN \"{columnName}\"";
                alterCommand.ExecuteNonQuery();
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    connection.Close();
                }
            }
        }

        private static void ApplyPermissionUpgrades(UserAccount user)
        {
            if (
                !user.CanApproveDetainedVisit
                && (
                    string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        user.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                user.CanApproveDetainedVisit = true;
            }

            if (
                !user.CanViewVisitorPermits
                && (
                    string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        user.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                user.CanViewVisitorPermits = true;
            }

            if (
                !user.CanCreateVisitorPermit
                && (
                    string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        user.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                user.CanCreateVisitorPermit = true;
            }

            if (
                !user.CanEditVisitorPermit
                && (
                    string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        user.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                user.CanEditVisitorPermit = true;
            }

            if (
                !user.CanApproveLeaveRequest
                && (
                    string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        user.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                user.CanApproveLeaveRequest = true;
            }

            if (
                !user.CanStopPermit
                && (
                    string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        user.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                user.CanStopPermit = true;
            }

            if (
                !user.CanReviewUnauthorizedExit
                && (
                    string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        user.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                user.CanReviewUnauthorizedExit = true;
            }

            if (
                !user.CanManageDepartments
                && string.Equals(
                    user.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                user.CanManageDepartments = true;
            }

            if (
                !user.CanManageDelegations
                && (
                    string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        user.Role,
                        AppRoles.SystemAdmin,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                user.CanManageDelegations = true;
            }
        }

        private static bool HasAnyPermissionConfigured(UserAccount user)
        {
            return user.CanViewDashboard
                || user.CanViewPermits
                || user.CanViewVisitorPermits
                || user.CanCreatePermit
                || user.CanCreateVisitorPermit
                || user.CanEditPermit
                || user.CanEditVisitorPermit
                || user.CanApprovePermit
                || user.CanApproveLeaveRequest
                || user.CanStopPermit
                || user.CanReviewUnauthorizedExit
                || user.CanViewVisits
                || user.CanCreateVisit
                || user.CanEditVisit
                || user.CanApproveDetainedVisit
                || user.CanViewDisplays
                || user.CanManageUsers
                || user.CanManageDepartments
                || user.CanManageDelegations
                || user.CanManageAdministration;
        }

        private static bool LooksLikeLegacyUser(UserAccount user)
        {
            return string.IsNullOrWhiteSpace(user.FullName) && !HasAnyPermissionConfigured(user);
        }

        private AdministrationSettings BuildDefaultAdministrationSettings()
        {
            var settings = new AdministrationSettings
            {
                Id = 1,
                IsInitialSetupCompleted = false,
                OrganizationName =
                    _configuration["Administration:OrganizationName"] ?? string.Empty,
                DepartmentName = _configuration["Administration:DepartmentName"] ?? string.Empty,
                Address = _configuration["Administration:Address"] ?? string.Empty,
                Phone = _configuration["Administration:Phone"] ?? string.Empty,
                Email = _configuration["Administration:Email"] ?? string.Empty,
                GeneralManagerUsername = string.Empty,
                ManagerName = _configuration["Administration:ManagerName"] ?? string.Empty,
                ManagerTitle = _configuration["Administration:ManagerTitle"] ?? string.Empty,
                ManagerPhoneNumber = string.Empty,
                SignatureText = _configuration["Administration:SignatureText"] ?? string.Empty,
                LogoPath = _configuration["Administration:LogoPath"] ?? string.Empty,
                SignatureImagePath =
                    _configuration["Administration:SignatureImagePath"] ?? string.Empty,
                DisplayBaseUrl = _configuration["Security:DisplayBaseUrl"] ?? string.Empty,
                DisplayAccessKey =
                    _configuration["Security:DisplayAccessKey"]
                    ?? DisplayAccessDefaults.CreateAccessKey(),
                WorkStartTime = new TimeOnly(8, 0),
                WorkEndTime = new TimeOnly(16, 0),
                AttendanceGraceMinutes = 15,
                WorkEndExitGraceMinutes = 30,
                LateReturnGraceMinutes = 5,
                LeaveRequestsEnabled = false,
                OfficialWorkDaysCsv = AdministrationWorkSchedule.DefaultOfficialWorkDaysCsv,
            };

            NormalizeAdministrationSettings(settings);
            return settings;
        }

        private static bool LooksLikePlaceholderSecret(string secret)
        {
            return secret.Length < 32
                || secret.Contains("change_this", StringComparison.OrdinalIgnoreCase)
                || secret.Contains("secure", StringComparison.OrdinalIgnoreCase)
                || secret.Contains("2026", StringComparison.OrdinalIgnoreCase);
        }

        private static void NormalizeAdministrationSettings(AdministrationSettings settings)
        {
            if (
                string.Equals(
                    settings.OrganizationName,
                    "الإدارة العامة لتصاريح المركبات",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                settings.OrganizationName = string.Empty;
            }

            if (
                string.Equals(
                    settings.DepartmentName,
                    "إدارة الأمن والتصاريح",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                settings.DepartmentName = string.Empty;
            }

            if (
                string.Equals(
                    settings.ManagerName,
                    "الأستاذ سامي إبراهيم",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                settings.ManagerName = string.Empty;
            }

            if (
                string.Equals(
                    settings.ManagerTitle,
                    "مدير الإدارة",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                settings.ManagerTitle = string.Empty;
            }

            if (
                string.Equals(
                    settings.SignatureText,
                    "توقيع إلكتروني معتمد",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                settings.SignatureText = string.Empty;
            }

            if (
                string.Equals(
                    settings.LogoPath,
                    "Public Security.png",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                settings.LogoPath = string.Empty;
            }

            if (
                string.Equals(
                    settings.Address,
                    "الرياض - المملكة العربية السعودية",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                settings.Address = string.Empty;
            }

            if (
                string.Equals(
                    settings.Phone,
                    "+966 11 000 0000",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    settings.Phone,
                    "966+ 11 000 0000",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                settings.Phone = string.Empty;
            }

            if (
                string.IsNullOrWhiteSpace(settings.DisplayAccessKey)
                || DisplayAccessDefaults.LooksLikePlaceholder(settings.DisplayAccessKey)
            )
            {
                settings.DisplayAccessKey = DisplayAccessKeyHasher.Hash(
                    DisplayAccessDefaults.CreateAccessKey()
                );
            }
            else
            {
                settings.DisplayAccessKey = DisplayAccessKeyHasher.Hash(
                    settings.DisplayAccessKey
                );
            }

            if (settings.LateReturnGraceMinutes < 0)
            {
                settings.LateReturnGraceMinutes = 0;
            }
            if (settings.AttendanceGraceMinutes < 0)
            {
                settings.AttendanceGraceMinutes = 0;
            }
            if (settings.WorkEndExitGraceMinutes < 0)
            {
                settings.WorkEndExitGraceMinutes = 0;
            }

            settings.OfficialWorkDaysCsv = AdministrationWorkSchedule.NormalizeOfficialWorkDaysCsv(
                settings.OfficialWorkDaysCsv
            );
        }
    }
}
