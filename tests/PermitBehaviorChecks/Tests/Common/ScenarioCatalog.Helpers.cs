using Microsoft.EntityFrameworkCore;
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
    private static void ExpirePermitDirectly(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        string permitNumber
    )
    {
        using var db = dbFactory.CreateDbContext();
        var permit =
            db.Permits.FirstOrDefault(x => x.PermitNumber == permitNumber)
            ?? throw new InvalidOperationException("permit not found for direct expiration");

        var now = AppClock.LocalNow;
        permit.CurrentState = "Outside";
        permit.ApprovalStatus = "Expired";
        permit.ArchivedAt = now;
        permit.OutTime = now;
        permit.ReturnTime = null;
        permit.RequiresReturn = false;
        permit.ExpiresAt = now;
        permit.QrToken = permit.QrToken ?? string.Empty;
        db.SaveChanges();
    }

    private static UserAccount CreateDepartmentManagerAccount(
        string username,
        string departmentName,
        string displayName
    )
    {
        return new UserAccount
        {
            Username = username,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            DisplayName = displayName,
            FullName = displayName,
            Department = departmentName,
            JobTitle = "مدير قسم",
            PhoneNumber = "0555555555",
            Email = string.Empty,
            IsActive = true,
            Role = AppRoles.DepartmentManager,
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
            CanApproveVisits = true,
        };
    }

    private static string CreatePendingPermitRecord(
        ApplicationDbContext db,
        string departmentName,
        string managerName
    )
    {
        var permitNumber = $"PERMIT-{Guid.NewGuid():N}".ToUpperInvariant();
        db.Permits.Add(
            new Permit
            {
                PermitNumber = permitNumber,
                PermitType = Permit.PermitTypePermanent,
                DriverName = "مصرح اعتماد",
                NationalId = $"{Random.Shared.NextInt64(1000000000, 2999999999)}",
                VehicleType = "Sedan",
                PlateNumber = $"P{Random.Shared.Next(1000, 9999)}",
                DepartmentName = departmentName,
                EmployeeDepartment = departmentName,
                ManagerName = managerName,
                EmployeePhone = "0555555555",
                PermitDate = AppClock.LocalNow.AddHours(-1),
                ExpiresAt = AppClock.LocalNow.AddHours(3),
                ApprovalStatus = "Pending",
                CurrentState = "Outside",
            }
        );

        return permitNumber;
    }

    private static string CreatePendingVisitRecord(
        ApplicationDbContext db,
        string departmentName,
        string visitedEmployeeName = "مسؤول القسم"
    )
    {
        var visitId = $"VISIT-{Guid.NewGuid():N}".ToUpperInvariant();
        db.Visits.Add(
            new Visit
            {
                VisitId = visitId,
                VisitorName = "زائر اعتماد",
                VisitLocation = departmentName,
                NationalId = $"{Random.Shared.NextInt64(1000000000, 2999999999)}",
                PhoneNumber = "0555555555",
                Purpose = "مراجعة",
                HostName = "مضيف القسم",
                VisitedPersonName = visitedEmployeeName,
                VisitedPersonType = Visit.VisitedPersonTypeEmployee,
                VisitDate = AppClock.LocalNow,
                ApprovalStatus = "Pending",
                Status = "Active",
            }
        );

        return visitId;
    }

    private static void StopAndArchivePermitDirectly(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        string permitNumber
    )
    {
        using var db = dbFactory.CreateDbContext();
        var permit =
            db.Permits.FirstOrDefault(x => x.PermitNumber == permitNumber)
            ?? throw new InvalidOperationException("permit not found for direct stop and archive");

        var now = AppClock.LocalNow;
        permit.CurrentState = "Outside";
        permit.ApprovalStatus = "Stopped";
        permit.ArchivedAt = now;
        permit.OutTime = now;
        permit.ReturnTime = null;
        permit.RequiresReturn = false;
        permit.ExpiresAt = now;
        permit.QrToken = string.Empty;
        db.SaveChanges();
    }

    private static string CreateApprovedVisitorPermit(
        IPermitService permitService,
        string driverName
    )
    {
        var permit = new Permit
        {
            PermitType = Permit.PermitTypeVisitor,
            DriverName = driverName,
            NationalId = "2876543210",
            VehicleType = "Sedan",
            PlateNumber = "GHI5678",
            DepartmentName = "بوابة المركبات",
            VisitLocation = "بوابة المركبات",
            EmployeePhone = "0559876543",
            RequiresReturn = false,
            AccessMode = Permit.AccessModeFullAccess,
            PermitDate = AppClock.LocalNow,
            ExpiresAt = AppClock.LocalNow.AddHours(12),
        };

        permitService.AddPermit(permit);
        permitService.UpdatePermitApprovalStatus(permit.PermitNumber, "Approved");
        return permit.PermitNumber;
    }

    private static string CreateApprovedEmployeePermit(
        IPermitService permitService,
        bool requiresReturn = true,
        DateTime? expiresAt = null
    )
    {
        var permit = new Permit
        {
            PermitType = Permit.PermitTypePermanent,
            DriverName = "Employee active",
            NationalId = "1234567890",
            VehicleType = "Sedan",
            PlateNumber = "DEF5678",
            DepartmentName = "الإدارة العامة",
            ManagerName = "مدير النظام",
            EmployeePhone = "0554444444",
            RequiresReturn = requiresReturn,
            AccessMode = requiresReturn ? Permit.AccessModeFullAccess : Permit.AccessModeEntryOnly,
            PermitDate = AppClock.LocalNow.AddHours(-1),
            ExpiresAt = expiresAt ?? AppClock.LocalNow.AddHours(1),
        };

        permitService.AddPermit(permit);
        permitService.UpdatePermitApprovalStatus(permit.PermitNumber, "Approved");
        return permit.PermitNumber;
    }

    private static void MarkPermitOutsideForUnauthorizedReturn(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        string permitNumber,
        DateTime outTime
    )
    {
        using var db = dbFactory.CreateDbContext();
        var permit =
            db.Permits.FirstOrDefault(x => x.PermitNumber == permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found for unauthorized-return simulation"
            );

        permit.CurrentState = "Outside";
        permit.ApprovalStatus = "Out";
        permit.OutTime = outTime;
        permit.ReturnTime = null;
        db.SaveChanges();
    }

    private static void RecordConfirmedEntryOnlyViolationDay(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        string permitNumber,
        string performedBy = "tester"
    )
    {
        var permitBeforeAttempt =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found before same-day violation");
        Require(
            permitBeforeAttempt.IsEntryOnlyPermit,
            "same-day violation helper expects an entry-only permit"
        );

        if (
            permitBeforeAttempt.ReturnTime.HasValue
            && permitBeforeAttempt.ReturnTime.Value.Date < clock.LocalNow.Date
        )
        {
            var recoveryScan = permitService.RecordPermitScan(permitNumber, performedBy);
            Require(
                recoveryScan.allowed,
                $"entry-only permit should recover the stale previous-day session before the next violation attempt, but returned '{recoveryScan.reason}'"
            );

            permitBeforeAttempt =
                permitService.GetPermitByNumber(permitNumber)
                ?? throw new InvalidOperationException(
                    "permit not found after stale-session recovery scan"
                );
        }

        Require(
            string.Equals(
                permitBeforeAttempt.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            $"entry-only permit should be inside before the unauthorized exit attempt, but state was {permitBeforeAttempt.CurrentState}"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var deniedExit = permitService.RecordPermitScan(permitNumber, performedBy);
        var permitAfterAttempt =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found after same-day unauthorized exit attempt"
            );
        Require(
            !deniedExit.allowed,
            $"entry-only permit should block the unauthorized exit attempt, but scan was allowed with reason '{deniedExit.reason}', state '{permitAfterAttempt.CurrentState}', status '{permitAfterAttempt.ApprovalStatus}', access mode '{permitAfterAttempt.AccessMode}', effective mode '{permitAfterAttempt.EffectiveAccessMode}', requires return '{permitAfterAttempt.RequiresReturn}'"
        );
        Require(
            string.Equals(
                deniedExit.reason,
                "PendingUnauthorizedExit",
                StringComparison.OrdinalIgnoreCase
            ),
            "entry-only unauthorized exit should open a pending sequence"
        );

        MarkPermitOutsideForUnauthorizedReturn(
            dbFactory,
            permitNumber,
            clock.LocalNow.AddSeconds(1)
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var returnScan = permitService.RecordPermitScan(permitNumber, performedBy);
        Require(returnScan.allowed, "return after unauthorized exit should still be recorded");
        Require(
            string.Equals(
                returnScan.reason,
                "ReturnAfterUnauthorizedExit recorded",
                StringComparison.OrdinalIgnoreCase
            ),
            "the return should close the sequence as a confirmed unauthorized exit"
        );
    }

    private static string CreateApprovedVisit(IVisitService visitService, string visitorName)
    {
        var visit = new Visit
        {
            VisitorName = visitorName,
            VisitLocation = "بوابة المركبات",
            NationalId = "1234567891",
            PhoneNumber = "0501234568",
            Purpose = "مراجعة",
            VisitedPersonName = "المدير العام",
            VisitedPersonType = Visit.VisitedPersonTypeHost,
            HostName = "المدير العام",
            VisitDate = AppClock.LocalNow,
        };

        visitService.AddVisit(visit);
        visitService.UpdateVisitApprovalStatus(visit.VisitId, "Approved");
        return visit.VisitId;
    }

    private static void SetAdministrationWorkHours(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        TimeOnly workStartTime,
        TimeOnly workEndTime,
        int lateReturnGraceMinutes,
        string officialWorkDaysCsv = "Sunday,Monday,Tuesday,Wednesday,Thursday",
        int attendanceGraceMinutes = 15,
        int workEndExitGraceMinutes = 30
    )
    {
        using var db = dbFactory.CreateDbContext();
        var settings = db.AdministrationSettings.SingleOrDefault(x => x.Id == 1);
        if (settings == null)
        {
            settings = new AdministrationSettings { Id = 1 };
            db.AdministrationSettings.Add(settings);
        }

        settings.WorkStartTime = workStartTime;
        settings.WorkEndTime = workEndTime;
        settings.AttendanceGraceMinutes = attendanceGraceMinutes;
        settings.WorkEndExitGraceMinutes = workEndExitGraceMinutes;
        settings.LateReturnGraceMinutes = lateReturnGraceMinutes;
        settings.OfficialWorkDaysCsv = officialWorkDaysCsv;
        settings.LastWorkEndClosureAt = null;
        db.SaveChanges();
    }

    private static void ResetStandardAdministrationSchedule(
        IDbContextFactory<ApplicationDbContext> dbFactory
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday",
            15,
            30
        );
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
