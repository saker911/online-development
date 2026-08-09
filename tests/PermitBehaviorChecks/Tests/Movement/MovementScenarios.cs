using VehiclePermitSystemWeb.Services.Monitoring;

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static Task ScenarioOvernightWorkHoursEnforceEntryOnlyPolicy(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(20, 0),
            new TimeOnly(4, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 13, 21, 30, 0));

        var entryOnlyPermitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        var entry = permitService.RecordPermitScan(entryOnlyPermitNumber, "tester");
        Require(entry.allowed, "entry-only permit should enter during overnight work hours");

        clock.Advance(TimeSpan.FromSeconds(6));
        var blockedExit = permitService.RecordPermitScan(entryOnlyPermitNumber, "tester");
        Require(
            !blockedExit.allowed,
            "entry-only permit should not exit without authorization during overnight work hours"
        );
        Require(
            string.Equals(
                blockedExit.reason,
                "PendingUnauthorizedExit",
                StringComparison.OrdinalIgnoreCase
            ),
            "overnight unauthorized exit should create a pending review state"
        );

        var permitAfterBlockedExit =
            permitService.GetPermitByNumber(entryOnlyPermitNumber)
            ?? throw new InvalidOperationException(
                "entry-only permit not found after blocked overnight exit"
            );
        Require(
            string.Equals(
                permitAfterBlockedExit.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            "blocked overnight exit should keep the permit inside"
        );
        Require(
            permitAfterBlockedExit.PendingUnauthorizedExitAt.HasValue,
            "blocked overnight exit should keep a pending unauthorized exit marker"
        );

        var fullReturnPermitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        var fullReturnEntry = permitService.RecordPermitScan(fullReturnPermitNumber, "tester");
        Require(
            fullReturnEntry.allowed,
            "full-return permit should enter before the overnight exit cycle"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var authorizedExit = permitService.RecordPermitScan(fullReturnPermitNumber, "tester");
        Require(authorizedExit.allowed, "full-access permit should allow an overnight exit");

        var permitAfterAuthorizedExit =
            permitService.GetPermitByNumber(fullReturnPermitNumber)
            ?? throw new InvalidOperationException(
                "full-access permit not found after the overnight exit"
            );
        Require(
            !permitAfterAuthorizedExit.ExpectedReturnTime.HasValue,
            "full-access overnight exit should not create a mandatory return deadline"
        );
        Require(
            string.Equals(
                permitAfterAuthorizedExit.CurrentState,
                "Outside",
                StringComparison.OrdinalIgnoreCase
            ),
            "full-access overnight exit should leave the permit outside until the next entry"
        );

        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday"
        );
        return Task.CompletedTask;
    }

    private static Task ScenarioOfficialWorkDaysPropagateAcrossPermitFlows(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 17, 10, 0, 0));

        var fridayEntryOnlyPermit = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        Require(
            permitService.RecordPermitScan(fridayEntryOnlyPermit, "tester").allowed,
            "permit should enter on Friday even when it is configured as a non-workday"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var fridayExit = permitService.RecordPermitScan(fridayEntryOnlyPermit, "tester");
        Require(fridayExit.allowed, "entry-only permit should be able to exit on a non-workday");
        Require(
            string.Equals(
                fridayExit.reason,
                "ExitFinal recorded",
                StringComparison.OrdinalIgnoreCase
            ),
            "non-workday exit should stay on the final-exit path"
        );

        var fridayScheduledPermit = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        Require(
            permitService.RecordPermitScan(fridayScheduledPermit, "tester").allowed,
            "scheduled permit should enter before the Friday schedule test"
        );
        Require(
            permitService.SubmitLeaveRequest(
                fridayScheduledPermit,
                requiresReturn: false,
                leaveReason: "خروج أسبوعي",
                leaveStartAt: null,
                leaveEndAt: null,
                performedBy: "tester",
                isDailySchedule: true,
                scheduleStartDate: new DateTime(2026, 4, 17),
                scheduleEndDate: new DateTime(2026, 4, 17),
                dailyExitMinutes: 9 * 60,
                dailyReturnMinutes: null
            ),
            "Friday daily schedule should save before Friday becomes an official workday"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var inactiveFridayScheduleExit = permitService.RecordPermitScan(
            fridayScheduledPermit,
            "tester"
        );
        Require(
            inactiveFridayScheduleExit.allowed,
            "non-workday Friday schedule should not block the permit"
        );
        Require(
            string.Equals(
                inactiveFridayScheduleExit.reason,
                "ExitFinal recorded",
                StringComparison.OrdinalIgnoreCase
            ),
            "Friday schedule should not activate while Friday is outside official workdays"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 17, 15, 0, 0));
        var nonWorkdayFridayClosurePermit = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(1)
        );
        Require(
            permitService.RecordPermitScan(nonWorkdayFridayClosurePermit, "tester").allowed,
            "non-workday Friday closure permit should enter before the closure check"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 17, 17, 30, 0));
        _ = permitService.ClosePermitsAtWorkEnd("system");
        var permitAfterNonWorkdayClosure =
            permitService.GetPermitByNumber(nonWorkdayFridayClosurePermit)
            ?? throw new InvalidOperationException(
                "permit not found after non-workday closure check"
            );
        Require(
            string.Equals(
                permitAfterNonWorkdayClosure.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            "Friday closure should not run for permits that entered on a non-workday Friday"
        );

        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 17, 10, 30, 0));
        var officialFridayPermit = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        Require(
            permitService.RecordPermitScan(officialFridayPermit, "tester").allowed,
            "permit should enter after Friday becomes an official workday"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var blockedFridayExit = permitService.RecordPermitScan(officialFridayPermit, "tester");
        Require(!blockedFridayExit.allowed, "Friday work hours should now block unauthorized exit");
        Require(
            string.Equals(
                blockedFridayExit.reason,
                "PendingUnauthorizedExit",
                StringComparison.OrdinalIgnoreCase
            ),
            "Friday should enforce pending unauthorized-exit review after the admin update"
        );

        var activeFridaySchedulePermit = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        Require(
            permitService.RecordPermitScan(activeFridaySchedulePermit, "tester").allowed,
            "Friday scheduled permit should enter before activation"
        );
        Require(
            permitService.SubmitLeaveRequest(
                activeFridaySchedulePermit,
                requiresReturn: false,
                leaveReason: "خروج أسبوعي بعد التفعيل",
                leaveStartAt: null,
                leaveEndAt: null,
                performedBy: "tester",
                isDailySchedule: true,
                scheduleStartDate: new DateTime(2026, 4, 17),
                scheduleEndDate: new DateTime(2026, 4, 17),
                dailyExitMinutes: 9 * 60,
                dailyReturnMinutes: null
            ),
            "Friday daily schedule should save after Friday becomes an official workday"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var activeFridayScheduleExit = permitService.RecordPermitScan(
            activeFridaySchedulePermit,
            "tester"
        );
        Require(
            activeFridayScheduleExit.allowed,
            "Friday daily schedule should activate after the admin update"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 17, 15, 0, 0));
        var fridayClosurePermit = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(1)
        );
        Require(
            permitService.RecordPermitScan(fridayClosurePermit, "tester").allowed,
            "Friday closure permit should enter before work-end closure"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 17, 17, 30, 0));
        Require(
            permitService.ClosePermitsAtWorkEnd("system") >= 1,
            "Friday should participate in automatic work-end closure after the admin update"
        );

        var permitAfterClosure =
            permitService.GetPermitByNumber(fridayClosurePermit)
            ?? throw new InvalidOperationException("permit not found after Friday closure");
        Require(
            permitAfterClosure.LastAutomaticWorkEndExitAt == new DateTime(2026, 4, 17, 16, 30, 0),
            "Friday work-end closure should stamp the configured checkout grace close time"
        );

        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday"
        );
        return Task.CompletedTask;
    }

    private static Task ScenarioDailyScheduledLeaveWithReturn(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 14, 8, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "employee permit should enter before daily schedule request");

        var configured = permitService.SubmitLeaveRequest(
            permitNumber,
            requiresReturn: true,
            leaveReason: "جدولة مراجعة يومية",
            leaveStartAt: null,
            leaveEndAt: null,
            performedBy: "tester",
            isDailySchedule: true,
            scheduleStartDate: new DateTime(2026, 4, 14),
            scheduleEndDate: new DateTime(2026, 4, 16),
            dailyExitMinutes: 10 * 60,
            dailyReturnMinutes: 11 * 60
        );
        Require(configured, "daily schedule with return should be accepted");

        var permitAfterConfiguration =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after daily schedule setup");
        Require(
            permitAfterConfiguration.HasDailyLeaveSchedule,
            "daily schedule should be persisted on the permit"
        );
        Require(
            !permitAfterConfiguration.PendingExitRequest,
            "daily schedule should not create a one-time pending leave request"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 14, 10, 10, 0));
        var exit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(exit.allowed, "daily schedule should allow exit during the daily window");

        var permitAfterExit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after scheduled exit");
        Require(
            permitAfterExit.ExpectedReturnTime == new DateTime(2026, 4, 14, 11, 0, 0),
            "daily schedule should set the expected return time to the configured daily return"
        );
        Require(
            string.Equals(
                permitAfterExit.ApprovalStatus,
                "Out",
                StringComparison.OrdinalIgnoreCase
            ),
            "scheduled exit with return should mark the permit as out"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 14, 10, 40, 0));
        var returnScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(returnScan.allowed, "scheduled leave return should be accepted");

        var permitAfterReturn =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after scheduled return");
        Require(
            string.Equals(
                permitAfterReturn.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            "permit should return to inside after a scheduled return"
        );
        Require(
            permitAfterReturn.HasDailyLeaveSchedule,
            "daily schedule should remain available after the return cycle"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDailyScheduledLeaveWithoutReturn(
        MutableSystemClock clock,
        IPermitService permitService
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 15, 8, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            entry.allowed,
            "employee permit should enter before daily final-exit schedule request"
        );

        var configured = permitService.SubmitLeaveRequest(
            permitNumber,
            requiresReturn: false,
            leaveReason: "خروج يومي بعد انتهاء المهمة",
            leaveStartAt: null,
            leaveEndAt: null,
            performedBy: "tester",
            isDailySchedule: true,
            scheduleStartDate: new DateTime(2026, 4, 15),
            scheduleEndDate: new DateTime(2026, 4, 17),
            dailyExitMinutes: 12 * 60 + 30
        );
        Require(configured, "daily schedule without return should be accepted");

        clock.SetLocalNow(new DateTime(2026, 4, 15, 12, 35, 0));
        var exit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(exit.allowed, "daily schedule without return should allow final exit");

        var permitAfterExit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after scheduled final exit");
        Require(
            string.Equals(
                permitAfterExit.CurrentState,
                "Outside",
                StringComparison.OrdinalIgnoreCase
            ),
            "permit should be outside after scheduled final exit"
        );
        Require(
            !permitAfterExit.ExpectedReturnTime.HasValue,
            "daily schedule without return should not set an expected return time"
        );
        Require(
            permitAfterExit.HasDailyLeaveSchedule,
            "daily schedule without return should persist after exit"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDailyScheduledLeaveCanBeCleared(
        MutableSystemClock clock,
        IPermitService permitService
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 16, 8, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "employee permit should enter before schedule clear test");

        var configured = permitService.SubmitLeaveRequest(
            permitNumber,
            requiresReturn: true,
            leaveReason: "جدولة مؤقتة",
            leaveStartAt: null,
            leaveEndAt: null,
            performedBy: "tester",
            isDailySchedule: true,
            scheduleStartDate: new DateTime(2026, 4, 16),
            scheduleEndDate: new DateTime(2026, 4, 20),
            dailyExitMinutes: 9 * 60,
            dailyReturnMinutes: 10 * 60
        );
        Require(configured, "daily schedule should be configured before clearing it");

        var cleared = permitService.ClearDailyLeaveSchedule(permitNumber, "tester");
        Require(cleared, "daily schedule should be cleared while permit is inside");

        var permitAfterClear =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found after clearing daily schedule"
            );
        Require(
            !permitAfterClear.HasDailyLeaveSchedule,
            "clearing the daily schedule should remove the schedule flag"
        );
        Require(
            !permitAfterClear.DailyLeaveScheduleStartDate.HasValue
                && !permitAfterClear.DailyLeaveScheduleEndDate.HasValue,
            "clearing the daily schedule should remove the configured date range"
        );
        Require(
            !permitAfterClear.DailyLeaveScheduleExitMinutes.HasValue
                && !permitAfterClear.DailyLeaveScheduleReturnMinutes.HasValue,
            "clearing the daily schedule should remove the configured times"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioDuplicatePermitScansAreIgnored(
        MutableSystemClock clock,
        IPermitService permitService
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 13, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(permitService, requiresReturn: true);

        var firstScan = permitService.RecordPermitScan(permitNumber, "display");
        Require(firstScan.allowed, "first scan should be accepted");
        Require(
            string.Equals(firstScan.reason, "Entry recorded", StringComparison.OrdinalIgnoreCase),
            "the first scan of a newly approved permit should be reported as an entry"
        );

        var duplicateScan = permitService.RecordPermitScan(permitNumber, "display");
        Require(!duplicateScan.allowed, "duplicate scan within the window should be rejected");
        Require(
            string.Equals(
                duplicateScan.reason,
                "AlreadyEntered",
                StringComparison.OrdinalIgnoreCase
            ),
            "duplicate scan should report that the permit is already inside"
        );

        var afterDuplicate =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after duplicate scan");
        Require(
            string.Equals(
                afterDuplicate.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            "duplicate scan must not flip the current state"
        );

        clock.Advance(TimeSpan.FromSeconds(6));

        var laterScan = permitService.RecordPermitScan(permitNumber, "display");
        Require(laterScan.allowed, "scan after the duplicate window should proceed normally");

        var afterLaterScan =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after later scan");
        Require(
            string.Equals(
                afterLaterScan.CurrentState,
                "Outside",
                StringComparison.OrdinalIgnoreCase
            ),
            "scan after the duplicate window should apply the next valid movement"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioFullReturnEmployeeKeepsBaseReturnRequirement(
        MutableSystemClock clock,
        IPermitService permitService
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 13, 11, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "full-return employee should enter on first scan");

        clock.Advance(TimeSpan.FromSeconds(6));
        var exit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(exit.allowed, "full-return employee should allow authorized exit");

        var permitAfterExit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after full-return exit");
        Require(
            !permitAfterExit.ExpectedReturnTime.HasValue,
            "full-access employee exit should not keep an expected return deadline"
        );
        Require(
            permitAfterExit.IsFullAccessPermit,
            "authorized exit should preserve the permit full-access mode"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var reentry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(reentry.allowed, "full-return employee should re-enter on next scan");

        var permitAfterReturn =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after full-return return");
        Require(
            permitAfterReturn.IsFullAccessPermit,
            "return scan should preserve the permit full-access mode"
        );
        Require(
            string.Equals(
                permitAfterReturn.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "return scan should restore the permit to approved"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioFullAccessMultipleEntriesAndExitsSameDayAllowed(
        MutableSystemClock clock,
        IPermitService permitService
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 24, 8, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(5)
        );

        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "full-access permit should allow the first entry");

        clock.Advance(TimeSpan.FromSeconds(6));
        var firstExit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(firstExit.allowed, "full-access permit should allow the first exit");

        clock.Advance(TimeSpan.FromSeconds(6));
        var secondEntry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(secondEntry.allowed, "full-access permit should allow re-entry on the same day");

        clock.Advance(TimeSpan.FromSeconds(6));
        var secondExit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            secondExit.allowed,
            "full-access permit should allow a second exit on the same day"
        );

        var permitAfterCycle =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "full-access permit not found after repeated same-day movement"
            );
        Require(permitAfterCycle.IsFullAccessPermit, "permit should remain in full-access mode");
        Require(
            string.Equals(
                permitAfterCycle.CurrentState,
                "Outside",
                StringComparison.OrdinalIgnoreCase
            ),
            "full-access permit should reflect the last movement state"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioFullAccessNeverAutoStopped(
        MutableSystemClock clock,
        IPermitService permitService
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 24, 12, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(5)
        );

        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "full-access permit should enter before passive reads");

        clock.Advance(TimeSpan.FromSeconds(6));
        var exit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(exit.allowed, "full-access permit should exit before passive reads");

        clock.SetLocalNow(new DateTime(2026, 4, 25, 9, 0, 0));
        _ = permitService.GetPermitByNumber(permitNumber);
        _ = permitService.GetAllPermits("tester");
        _ = permitService.GetVisiblePermits("tester");
        _ = permitService.GetPermitActivities(permitNumber, "tester");

        var permitAfterReads =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "full-access permit not found after passive reads"
            );
        Require(
            !string.Equals(
                permitAfterReads.ApprovalStatus,
                "Stopped",
                StringComparison.OrdinalIgnoreCase
            ),
            "full-access permit should not be stopped by passive reads"
        );
        Require(
            permitAfterReads.UnauthorizedExitWarningCount == 0
                && permitAfterReads.LateReturnWarningCount == 0,
            "full-access permit should keep penalty counters at zero"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioFullAccessLogsMovementsOnly(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 24, 14, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(5)
        );

        Require(
            permitService.RecordPermitScan(permitNumber, "tester").allowed,
            "full-access permit should allow entry for logging"
        );
        clock.Advance(TimeSpan.FromSeconds(6));
        Require(
            permitService.RecordPermitScan(permitNumber, "tester").allowed,
            "full-access permit should allow exit for logging"
        );
        clock.Advance(TimeSpan.FromSeconds(6));
        Require(
            permitService.RecordPermitScan(permitNumber, "tester").allowed,
            "full-access permit should allow re-entry for logging"
        );

        using var db = dbFactory.CreateDbContext();
        var permitActivities = db
            .PermitActivities.AsNoTracking()
            .Where(activity => activity.PermitNumber == permitNumber)
            .ToList();
        var auditLogs = db
            .AuditLogs.AsNoTracking()
            .Where(log => log.EntityType == "Permit" && log.EntityId == permitNumber)
            .ToList();

        Require(
            permitActivities.Count(activity =>
                string.Equals(activity.ActionType, "Entry", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    activity.ActionType,
                    "ExitFinal",
                    StringComparison.OrdinalIgnoreCase
                )
            ) >= 3,
            "full-access movements should be captured in permit activities"
        );
        Require(
            auditLogs.Count(log =>
                string.Equals(log.ActionType, "Entry", StringComparison.OrdinalIgnoreCase)
                || string.Equals(log.ActionType, "ExitFinal", StringComparison.OrdinalIgnoreCase)
            ) >= 3,
            "full-access movements should be captured in audit logs"
        );
        Require(
            !permitActivities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "PendingUnauthorizedExit",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    activity.ActionType,
                    "NoReturnWarning",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    activity.ActionType,
                    "NoReturnViolation",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    activity.ActionType,
                    "UnauthorizedExitStopped",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "full-access movements should not create penalty workflow activities"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioEntryOnlyMultipleSameDayViolationsCountOnce(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday"
        );
        clock.SetLocalNow(new DateTime(2026, 4, 24, 9, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(5)
        );

        Require(
            permitService.RecordPermitScan(permitNumber, "tester").allowed,
            "entry-only permit should enter before same-day violations"
        );
        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, permitNumber);
        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, permitNumber);

        var permitAfterSameDayViolations =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "entry-only permit not found after same-day violations"
            );
        Require(
            permitAfterSameDayViolations.UnauthorizedExitWarningCount == 1,
            "multiple confirmed same-day violations should count as one violation day"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioEntryOnlyThreeDistinctViolationDaysEscalate(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday"
        );
        clock.SetLocalNow(new DateTime(2026, 4, 24, 9, 30, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        Require(
            permitService.RecordPermitScan(permitNumber, "tester").allowed,
            "entry-only permit should enter before distinct-day violations"
        );

        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, permitNumber);
        clock.SetLocalNow(new DateTime(2026, 4, 25, 9, 30, 0));
        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, permitNumber);
        clock.SetLocalNow(new DateTime(2026, 4, 26, 9, 30, 0));
        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, permitNumber);

        var stoppedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "entry-only permit not found after distinct-day escalation"
            );
        Require(
            string.Equals(
                stoppedPermit.ApprovalStatus,
                "Stopped",
                StringComparison.OrdinalIgnoreCase
            ),
            "the third distinct violation day should stop the permit"
        );
        Require(
            stoppedPermit.UnauthorizedExitWarningCount == 3,
            "the permit should retain the three counted violation days"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioEntryOnlySameDayReturnDoesNotDuplicateViolationDay(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday"
        );
        clock.SetLocalNow(new DateTime(2026, 4, 24, 11, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(5)
        );

        Require(
            permitService.RecordPermitScan(permitNumber, "tester").allowed,
            "entry-only permit should enter before the same-day return test"
        );
        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, permitNumber);

        var permitAfterReturn =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "entry-only permit not found after same-day return"
            );
        Require(
            permitAfterReturn.UnauthorizedExitWarningCount == 1,
            "returning in the same day should keep the violation count at one day only"
        );

        var activities = permitService.GetPermitActivities(permitNumber).Take(2).ToList();
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "UnauthorizedExitConfirmed",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                && activities.Any(activity =>
                    string.Equals(
                        activity.ActionType,
                        "ReturnAfterUnauthorizedExit",
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
            "the same-day return should be logged without duplicating the violation day"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioForgotToCheckoutAutoClosesPreviousDaySessionWithPenalty(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 23, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(5)
        );

        var initialEntry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(initialEntry.allowed, "entry-only permit should enter on the first day");

        clock.SetLocalNow(new DateTime(2026, 4, 24, 9, 0, 0));
        var nextDayScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            nextDayScan.allowed,
            "next-day scan should record the missed checkout, auto-close yesterday, and allow a fresh entry"
        );

        var permitAfterScan =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found after forgot-checkout recovery"
            );
        Require(
            string.Equals(
                permitAfterScan.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            "next-day recovery scan should leave the permit inside"
        );
        Require(
            string.Equals(
                permitAfterScan.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "next-day recovery scan should keep the permit approved"
        );
        Require(
            permitAfterScan.LastAutomaticWorkEndExitAt == new DateTime(2026, 4, 23, 16, 30, 0),
            "recovery flow should stamp the previous checkout grace close time"
        );
        Require(
            permitAfterScan.UnauthorizedExitWarningCount == 1
                && !permitAfterScan.PendingUnauthorizedExitAt.HasValue,
            "recovery flow should count the missed checkout as one penalty day"
        );

        var activities = permitService.GetPermitActivities(permitNumber).Take(4).ToList();
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "NoCheckoutViolation",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "recovery flow should record a missed checkout violation"
        );
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "WorkEndExit",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "recovery flow should backfill the missing work-end exit"
        );
        Require(
            activities.Any(activity =>
                string.Equals(activity.ActionType, "Entry", StringComparison.OrdinalIgnoreCase)
            ),
            "recovery flow should still record the new-day entry"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioEntryOnlyForgotCheckoutAfterWorkHoursChangeStillRecovers(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 23, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(5)
        );

        var initialEntry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(initialEntry.allowed, "entry-only permit should enter on the first day");

        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(4, 0),
            new TimeOnly(6, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday",
            15,
            30
        );

        clock.SetLocalNow(new DateTime(2026, 4, 24, 5, 0, 0));
        var nextDayScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            nextDayScan.allowed,
            "next-day scan should recover an entry-only stale session after work hours changed"
        );

        var permitAfterScan =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found after work-hours-change stale-session recovery"
            );
        Require(
            string.Equals(
                permitAfterScan.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            "work-hours-change recovery should leave the permit inside after the new entry"
        );
        Require(
            permitAfterScan.ReturnTime == new DateTime(2026, 4, 24, 5, 0, 0),
            "work-hours-change recovery should record the current-day entry time"
        );
        Require(
            permitAfterScan.LastAutomaticWorkEndExitAt == new DateTime(2026, 4, 23, 10, 0, 0),
            "work-hours-change recovery should not stamp an automatic checkout before the stale entry"
        );
        Require(
            permitAfterScan.UnauthorizedExitWarningCount == 1
                && !permitAfterScan.PendingUnauthorizedExitAt.HasValue,
            "work-hours-change recovery should count the missed checkout as one penalty day"
        );

        var activities = permitService.GetPermitActivities(permitNumber).Take(4).ToList();
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "NoCheckoutViolation",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "work-hours-change recovery should record a missed checkout violation"
        );
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "WorkEndExit",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "work-hours-change recovery should backfill the stale work-end exit"
        );
        Require(
            activities.Any(activity =>
                string.Equals(activity.ActionType, "Entry", StringComparison.OrdinalIgnoreCase)
            ),
            "work-hours-change recovery should still record the current-day entry"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioEntryOnlyStalePendingExitRecoversOnNextDayEntry(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday",
            15,
            30
        );
        clock.SetLocalNow(new DateTime(2026, 4, 23, 10, 45, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(5)
        );

        var firstDayEntry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(firstDayEntry.allowed, "entry-only permit should enter on the first day");

        clock.SetLocalNow(new DateTime(2026, 4, 23, 11, 16, 0));
        var unauthorizedExitAttempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            !unauthorizedExitAttempt.allowed,
            "entry-only unauthorized exit should be blocked on the same day"
        );
        Require(
            string.Equals(
                unauthorizedExitAttempt.reason,
                "PendingUnauthorizedExit",
                StringComparison.OrdinalIgnoreCase
            ),
            "same-day unauthorized exit should leave a pending sequence"
        );

        var permitAfterPendingAttempt =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after pending exit attempt");
        Require(
            permitAfterPendingAttempt.PendingUnauthorizedExitAt.HasValue,
            "pending marker should exist before next-day recovery"
        );
        Require(
            string.Equals(
                permitAfterPendingAttempt.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            "blocked exit should keep the permit inside before next-day recovery"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 24, 16, 25, 0));
        var nextDayScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            nextDayScan.allowed,
            $"next-day scan should promote the stale pending exit, close yesterday, and allow a fresh entry, but returned '{nextDayScan.reason}'"
        );
        Require(
            string.Equals(
                nextDayScan.reason,
                "WorkEndEntry recorded",
                StringComparison.OrdinalIgnoreCase
            ),
            "next-day recovery during the current work-end grace should record a fresh entry"
        );

        var permitAfterRecovery =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after stale pending recovery");
        Require(
            string.Equals(
                permitAfterRecovery.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            "next-day recovery should leave the permit inside after the new entry"
        );
        Require(
            string.Equals(
                permitAfterRecovery.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "next-day recovery should keep the permit approved when escalation does not stop it"
        );
        Require(
            permitAfterRecovery.ReturnTime == new DateTime(2026, 4, 24, 16, 25, 0),
            "next-day recovery should record a fresh current-day entry time"
        );
        Require(
            permitAfterRecovery.OutTime == new DateTime(2026, 4, 23, 16, 30, 0),
            "next-day recovery should close the previous day at the checkout grace close time"
        );
        Require(
            permitAfterRecovery.LastAutomaticWorkEndExitAt == new DateTime(2026, 4, 23, 16, 30, 0),
            "next-day recovery should stamp the automatic work-end exit time"
        );
        Require(
            !permitAfterRecovery.PendingUnauthorizedExitAt.HasValue
                && string.IsNullOrWhiteSpace(permitAfterRecovery.PendingUnauthorizedExitSequenceId),
            "next-day entry scan should not leave a new pending unauthorized exit marker"
        );
        Require(
            permitAfterRecovery.UnauthorizedExitWarningCount == 1,
            "the old pending exit should be preserved as one reviewed violation day"
        );

        var activities = permitService.GetPermitActivities(permitNumber).ToList();
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "UnauthorizedExitNeedsReview",
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(
                    activity.ClassificationStatus,
                    "NeedsAdministrativeReview",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "the stale pending exit should be promoted to administrative review"
        );
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "WorkEndExit",
                    StringComparison.OrdinalIgnoreCase
                )
                && activity.OccurredAt == new DateTime(2026, 4, 23, 16, 30, 0)
            ),
            "the previous day session should be closed with a work-end exit activity"
        );
        Require(
            activities.Any(activity =>
                string.Equals(activity.ActionType, "Entry", StringComparison.OrdinalIgnoreCase)
                && activity.OccurredAt == new DateTime(2026, 4, 24, 16, 25, 0)
            ),
            "the current scan should record a new current-day entry"
        );
        Require(
            !activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "PendingUnauthorizedExit",
                    StringComparison.OrdinalIgnoreCase
                )
                && activity.OccurredAt.Date == new DateTime(2026, 4, 24).Date
            ),
            "the current-day scan should not create a fresh pending unauthorized exit activity"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioEntryOnlyPreviousDayInsideWithCurrentDayPendingRecovers(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(14, 7),
            5,
            "Saturday,Sunday,Monday,Tuesday,Wednesday,Thursday",
            15,
            30
        );

        clock.SetLocalNow(new DateTime(2026, 5, 8, 22, 45, 49));
        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(5)
        );

        using (var db = dbFactory.CreateDbContext())
        {
            var permit =
                db.Permits.FirstOrDefault(permit => permit.PermitNumber == permitNumber)
                ?? throw new InvalidOperationException(
                    "permit not found for stale current-day pending simulation"
                );

            permit.CurrentState = "Inside";
            permit.ApprovalStatus = "Approved";
            permit.ReturnTime = new DateTime(2026, 5, 8, 22, 45, 49);
            permit.OutTime = null;
            permit.PendingUnauthorizedExitAt = new DateTime(2026, 5, 9, 9, 42, 0);
            permit.PendingUnauthorizedExitSequenceId = "current-day-pending";
            permit.LastAutomaticWorkEndExitAt = new DateTime(2026, 4, 27, 16, 0, 0);
            db.SaveChanges();
        }

        clock.SetLocalNow(new DateTime(2026, 5, 9, 11, 10, 0));
        var recoveryScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            recoveryScan.allowed,
            $"stale previous-day session with current-day pending should recover and allow entry, but returned '{recoveryScan.reason}'"
        );

        var permitAfterRecovery =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after stale pending recovery");
        Require(
            string.Equals(
                permitAfterRecovery.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            "recovery should leave the permit inside after recording the fresh entry"
        );
        Require(
            permitAfterRecovery.ReturnTime == new DateTime(2026, 5, 9, 11, 10, 0),
            "recovery should replace the previous-day inside time with the current scan entry"
        );
        Require(
            permitAfterRecovery.LastAutomaticWorkEndExitAt == new DateTime(2026, 5, 8, 22, 45, 49),
            "recovery should close the previous-day session at the stale entry time when the computed work-end close predates it"
        );
        Require(
            !permitAfterRecovery.PendingUnauthorizedExitAt.HasValue
                && string.IsNullOrWhiteSpace(permitAfterRecovery.PendingUnauthorizedExitSequenceId),
            "recovery should clear the current-day pending marker that belonged to the stale session"
        );

        var activities = permitService.GetPermitActivities(permitNumber).ToList();
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "DeniedAttemptClosed",
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(
                    activity.ClassificationStatus,
                    "ClosedWithoutViolation",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "recovery should close the current-day pending attempt without violation"
        );
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "WorkEndExit",
                    StringComparison.OrdinalIgnoreCase
                )
                && activity.OccurredAt == new DateTime(2026, 5, 8, 22, 45, 49)
            ),
            "recovery should write a work-end exit for the stale previous-day session"
        );
        Require(
            activities.Any(activity =>
                string.Equals(activity.ActionType, "Entry", StringComparison.OrdinalIgnoreCase)
                && activity.OccurredAt == new DateTime(2026, 5, 9, 11, 10, 0)
            ),
            "recovery should record the current scan as a new entry"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioEntryOnlyOverdueLeaveReturnCountsAsPenalty(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday"
        );
        clock.SetLocalNow(new DateTime(2026, 4, 24, 9, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(5)
        );

        var initialEntry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(initialEntry.allowed, "entry-only permit should enter before leave request");

        var submitted = permitService.SubmitLeaveRequest(
            permitNumber,
            requiresReturn: true,
            leaveReason: "استئذان قصير",
            leaveStartAt: clock.LocalNow.AddMinutes(5),
            leaveEndAt: clock.LocalNow.AddHours(1),
            performedBy: "tester"
        );
        Require(submitted, "leave with return should be accepted for entry-only permit");

        clock.SetLocalNow(new DateTime(2026, 4, 24, 9, 10, 0));
        var exit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(exit.allowed, "leave with return should allow authorized exit");

        clock.SetLocalNow(new DateTime(2026, 4, 25, 9, 0, 0));
        var lateEntry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            lateEntry.allowed,
            "late leave return should still record the entry before stop threshold"
        );

        var permitAfterLateEntry =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after overdue return");
        Require(
            permitAfterLateEntry.UnauthorizedExitWarningCount == 1,
            "overdue leave return should count as one penalty day"
        );

        var activities = permitService.GetPermitActivities(permitNumber).Take(4).ToList();
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "NoReturnViolation",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "overdue leave return should record a no-return violation"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioReadingPermitDataDoesNotCreatePenalties(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IReportsDashboardService reportsDashboardService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday"
        );
        clock.SetLocalNow(new DateTime(2026, 4, 24, 13, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(5)
        );

        Require(
            permitService.RecordPermitScan(permitNumber, "tester").allowed,
            "entry-only permit should enter before read-only checks"
        );
        clock.Advance(TimeSpan.FromSeconds(6));
        var pendingAttempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            !pendingAttempt.allowed,
            "entry-only permit should block the exit before read-only checks"
        );

        _ = permitService.GetPermitByNumber(permitNumber);
        _ = permitService.GetAllPermits("tester");
        _ = permitService.GetVisiblePermits("tester");
        _ = permitService.GetPermitActivities(permitNumber, "tester");
        _ = reportsDashboardService.BuildUnauthorizedExitWorkflowDashboardModel(
            permitNumber,
            "Pending",
            1,
            20,
            "tester"
        );
        _ = reportsDashboardService.BuildStoppedPermitsDashboardModel(
            permitNumber,
            1,
            20,
            "tester"
        );

        var permitAfterReads =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "entry-only permit not found after read-only checks"
            );
        Require(
            permitAfterReads.UnauthorizedExitWarningCount == 0,
            "read-only operations should not create a violation day"
        );
        Require(
            !string.Equals(
                permitAfterReads.ApprovalStatus,
                "Stopped",
                StringComparison.OrdinalIgnoreCase
            ),
            "read-only operations should not stop the permit"
        );
        Require(
            permitAfterReads.PendingUnauthorizedExitAt.HasValue,
            "read-only operations should preserve the pending sequence instead of escalating it"
        );
        Require(
            !permitService
                .GetPermitActivities(permitNumber)
                .Any(activity =>
                    string.Equals(
                        activity.ActionType,
                        "UnauthorizedExitNeedsReview",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        activity.ActionType,
                        "UnauthorizedExitStopped",
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
            "read-only operations should not create review or stop activities"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioUnauthorizedExitPendingClosesAtWorkEnd(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 13, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "employee permit should enter before unauthorized exit attempts");

        clock.Advance(TimeSpan.FromSeconds(6));
        var firstAttempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(!firstAttempt.allowed, "pending unauthorized exit should still be blocked");
        Require(
            string.Equals(
                firstAttempt.reason,
                "PendingUnauthorizedExit",
                StringComparison.OrdinalIgnoreCase
            ),
            "unauthorized exit should create a pending state"
        );

        var permitAfterAttempt =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found after pending unauthorized exit"
            );
        Require(
            permitAfterAttempt.PendingUnauthorizedExitAt.HasValue,
            "permit should keep a pending unauthorized exit marker"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 13, 17, 30, 0));
        permitService.ClosePermitsAtWorkEnd("system");

        var permitAfterClosure =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after work-end closure");
        Require(
            string.Equals(
                permitAfterClosure.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "permit should stay approved when the pending attempt closes at work end"
        );
        Require(
            !permitAfterClosure.PendingUnauthorizedExitAt.HasValue,
            "pending unauthorized exit should be cleared at work end"
        );

        var latestActivity = permitService.GetPermitActivities(permitNumber).FirstOrDefault();
        Require(latestActivity != null, "closing pending attempt should record activity");
        Require(
            string.Equals(
                latestActivity!.ActionType,
                "LateCheckout",
                StringComparison.OrdinalIgnoreCase
            ),
            "latest activity should mark late checkout after pending closure"
        );
        Require(
            permitService
                .GetPermitActivities(permitNumber)
                .Any(activity =>
                    string.Equals(
                        activity.ActionType,
                        "WorkEndExit",
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
            "pending closure should still record the automatic work-end exit"
        );

        var closeActivity = permitService
            .GetPermitActivities(permitNumber)
            .FirstOrDefault(activity =>
                string.Equals(
                    activity.ActionType,
                    "DeniedAttemptClosed",
                    StringComparison.OrdinalIgnoreCase
                )
            );
        Require(closeActivity != null, "pending attempt should be closed without violation");
        Require(
            string.Equals(
                closeActivity!.ClassificationStatus,
                "ClosedWithoutViolation",
                StringComparison.OrdinalIgnoreCase
            ),
            "closed pending attempt should expose a closed-without-violation classification"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioPendingUnauthorizedExitAppearsInMonitoring(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IMonitoringDashboardService monitoringDashboardService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday"
        );
        clock.SetLocalNow(new DateTime(2026, 4, 14, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(2)
        );
        Require(
            permitService.RecordPermitScan(permitNumber, "tester").allowed,
            "entry-only permit should enter before the monitoring rejection test"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var deniedExit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            !deniedExit.allowed
                && string.Equals(
                    deniedExit.reason,
                    "PendingUnauthorizedExit",
                    StringComparison.OrdinalIgnoreCase
                ),
            "unauthorized entry-only exit should be denied and marked pending"
        );

        var dashboard = monitoringDashboardService.BuildDashboard("today");
        Require(dashboard.EntryCount == 1, "monitoring should retain the successful entry count");
        Require(
            dashboard.DeniedCount == 1,
            "monitoring should count the pending unauthorized exit as a denied operation"
        );
        Require(
            dashboard.RecentActivities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "PendingUnauthorizedExit",
                    StringComparison.OrdinalIgnoreCase
                ) && !activity.Success
            ),
            "monitoring should show the denied exit in recent operations"
        );
        Require(
            dashboard.TopRejectionReasons.Sum(item => item.Count) == 1,
            "monitoring should include the denied exit in rejection reasons"
        );

        ResetStandardAdministrationSchedule(dbFactory);
        return Task.CompletedTask;
    }

    private static Task ScenarioExpiredPermitClearsPendingUnauthorizedExit(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 13, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddMinutes(2)
        );

        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "permit should enter before the pending unauthorized attempt");

        clock.Advance(TimeSpan.FromSeconds(6));
        var deniedExit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            !deniedExit.allowed,
            $"unauthorized exit should still be blocked before expiry, but returned '{deniedExit.reason}'"
        );

        var permitAfterAttempt =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found after pending unauthorized exit before expiry"
            );
        Require(
            permitAfterAttempt.PendingUnauthorizedExitAt.HasValue,
            "pending unauthorized exit marker should be present before expiry"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 13, 10, 3, 0));

        var expiredPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after expiry synchronization");
        Require(
            string.Equals(
                expiredPermit.ApprovalStatus,
                "Expired",
                StringComparison.OrdinalIgnoreCase
            ),
            "permit should expire after its expiry time passes"
        );
        Require(
            string.Equals(
                expiredPermit.CurrentState,
                "Outside",
                StringComparison.OrdinalIgnoreCase
            ),
            "expired permit should not remain marked as inside"
        );
        Require(
            !expiredPermit.PendingUnauthorizedExitAt.HasValue,
            "expired permit should clear pending unauthorized exit markers"
        );
        Require(
            string.IsNullOrWhiteSpace(expiredPermit.PendingUnauthorizedExitSequenceId),
            "expired permit should clear the pending unauthorized exit sequence id"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioPendingUnauthorizedExitEscalatesToReviewAndStop(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday"
        );
        clock.SetLocalNow(new DateTime(2026, 4, 14, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "employee permit should enter before the pending-review cycle");

        clock.Advance(TimeSpan.FromSeconds(6));
        var firstAttempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(!firstAttempt.allowed, "the first unauthorized exit should be blocked");

        clock.SetLocalNow(new DateTime(2026, 4, 15, 9, 0, 0));
        var secondDayRecovery = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            secondDayRecovery.allowed,
            "the second-day scan should recover the stale pending exit and record a new entry"
        );

        var permitAfterSecondDayRecovery =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found after second-day pending recovery"
            );
        Require(
            permitAfterSecondDayRecovery.UnauthorizedExitWarningCount == 1,
            "the second-day recovery should count one reviewed violation day"
        );
        Require(
            !permitAfterSecondDayRecovery.PendingUnauthorizedExitAt.HasValue,
            "the second-day recovery should clear the stale pending marker"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var secondAttempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(!secondAttempt.allowed, "the same second-day unauthorized exit should be blocked");
        Require(
            string.Equals(
                secondAttempt.reason,
                "PendingUnauthorizedExit",
                StringComparison.OrdinalIgnoreCase
            ),
            "the second-day attempt should keep the new day pending"
        );

        var permitAfterSecondDay =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after second-day escalation");
        Require(
            permitAfterSecondDay.UnauthorizedExitWarningCount == 1,
            "the second-day attempt should keep the reviewed violation count at one until the next day"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 16, 9, 0, 0));
        var thirdDayRecovery = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            thirdDayRecovery.allowed,
            "the third-day scan should recover the second-day pending exit and record a new entry"
        );

        var permitAfterThirdDayRecovery =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after third-day recovery");
        Require(
            permitAfterThirdDayRecovery.UnauthorizedExitWarningCount == 2,
            "the third-day recovery should count two distinct reviewed violation days"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var thirdAttempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(!thirdAttempt.allowed, "the same third-day unauthorized exit should be blocked");
        Require(
            string.Equals(
                thirdAttempt.reason,
                "PendingUnauthorizedExit",
                StringComparison.OrdinalIgnoreCase
            ),
            "the third-day attempt should also create a fresh pending sequence"
        );

        var permitAfterThirdDay =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after third-day escalation");
        Require(
            permitAfterThirdDay.UnauthorizedExitWarningCount == 2,
            "the third-day attempt should keep two reviewed violation days until the next day"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 17, 9, 0, 0));
        var stopAttempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            !stopAttempt.allowed,
            "the fourth-day recovery scan should be denied after the third distinct violation day stops the permit"
        );
        Require(
            string.Equals(stopAttempt.reason, "permit_stopped", StringComparison.OrdinalIgnoreCase),
            "the stop should be surfaced directly by the triggering scan"
        );

        var stoppedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after review escalation");
        Require(
            string.Equals(
                stoppedPermit.ApprovalStatus,
                "Stopped",
                StringComparison.OrdinalIgnoreCase
            ),
            "permit should be stopped after the third unresolved pending unauthorized exit"
        );

        var reviewActivities = permitService
            .GetPermitActivities(permitNumber)
            .Where(activity =>
                string.Equals(
                    activity.ActionType,
                    "UnauthorizedExitNeedsReview",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToList();
        Require(
            reviewActivities.Count >= 3,
            "each unresolved pending exit should create a review activity"
        );
        Require(
            reviewActivities.All(activity =>
                string.Equals(
                    activity.ClassificationStatus,
                    "NeedsAdministrativeReview",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "review activities should be classified as administrative review"
        );

        var stopActivity = permitService
            .GetPermitActivities(permitNumber)
            .FirstOrDefault(activity =>
                string.Equals(
                    activity.ActionType,
                    "UnauthorizedExitStopped",
                    StringComparison.OrdinalIgnoreCase
                )
            );
        Require(stopActivity != null, "the third unresolved review should stop the permit");

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioStoppedPermitsDashboardSurfacesDirectActionQueue(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IReportsDashboardService reportsDashboardService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 19, 10, 0, 0));

        var violatedPermitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var manualStoppedPermitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        var entry = permitService.RecordPermitScan(violatedPermitNumber, "tester");
        Require(entry.allowed, "entry-only permit should enter before violation attempts");

        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, violatedPermitNumber);
        clock.SetLocalNow(new DateTime(2026, 4, 20, 10, 0, 0));
        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, violatedPermitNumber);
        clock.SetLocalNow(new DateTime(2026, 4, 21, 10, 0, 0));
        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, violatedPermitNumber);

        var manualStopResult = permitService.StopPermit(manualStoppedPermitNumber, "reviewer");
        Require(manualStopResult, "manual stop should succeed for the second permit");

        var dashboard = reportsDashboardService.BuildStoppedPermitsDashboardModel(
            null,
            page: 1,
            pageSize: 10,
            currentUser: "tester"
        );
        Require(
            dashboard.StoppedPermits.Count >= 2,
            "stopped dashboard should list multiple stopped permits"
        );

        var violatedItem = dashboard.StoppedPermits.FirstOrDefault(item =>
            string.Equals(
                item.PermitNumber,
                violatedPermitNumber,
                StringComparison.OrdinalIgnoreCase
            )
        );
        Require(violatedItem != null, "violated permit should appear in stopped dashboard");
        Require(
            !string.IsNullOrWhiteSpace(violatedItem!.StopReason),
            "violated permit should expose a stop reason in the dashboard"
        );

        var manualStoppedItem = dashboard.StoppedPermits.FirstOrDefault(item =>
            string.Equals(
                item.PermitNumber,
                manualStoppedPermitNumber,
                StringComparison.OrdinalIgnoreCase
            )
        );
        Require(
            manualStoppedItem != null,
            "manually stopped permit should appear in stopped dashboard"
        );
        Require(
            !string.IsNullOrWhiteSpace(manualStoppedItem!.RecordedBy),
            "manually stopped permit should expose the recorded-by operator"
        );

        var filteredDashboard = reportsDashboardService.BuildStoppedPermitsDashboardModel(
            violatedPermitNumber,
            page: 1,
            pageSize: 10,
            currentUser: "tester"
        );
        Require(
            filteredDashboard.StoppedPermits.Count == 1,
            "filtered stopped dashboard should isolate the requested permit"
        );
        Require(
            string.Equals(
                filteredDashboard.StoppedPermits[0].PermitNumber,
                violatedPermitNumber,
                StringComparison.OrdinalIgnoreCase
            ),
            "filtered stopped dashboard should return the violated permit directly"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioNotificationCenterAggregatesOperationalQueues(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IReportsDashboardService reportsDashboardService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 26, 10, 0, 0));
        const string notificationManagerUsername = "2000000009";

        using (var db = dbFactory.CreateDbContext())
        {
            if (!db.UserAccounts.Any(user => user.Username == notificationManagerUsername))
            {
                db.UserAccounts.Add(
                    new UserAccount
                    {
                        Username = notificationManagerUsername,
                        DisplayName = "مدير إشعارات",
                        FullName = "مدير إشعارات",
                        Department = "الإدارة العامة",
                        JobTitle = "مدير عام",
                        PhoneNumber = "0559999999",
                        Role = VehiclePermitSystemWeb.Security.AppRoles.GeneralManager,
                        IsActive = true,
                        CanViewPermits = true,
                        CanApprovePermit = true,
                    }
                );
                db.SaveChanges();
            }
        }

        var pendingApprovalPermit = new Permit
        {
            PermitNumber = "PENDING-NOTIFY-01",
            PermitType = Permit.PermitTypePermanent,
            DriverName = "جابر محمد",
            NationalId = "2000000001",
            VehicleType = "Sedan",
            PlateNumber = "AAA1111",
            DepartmentName = "الإدارة العامة",
            ManagerName = "مدير النظام",
            EmployeePhone = "0550000001",
            RequiresReturn = true,
            PermitDate = clock.LocalNow,
            ExpiresAt = clock.LocalNow.AddDays(10),
            ApprovalStatus = "Pending",
            CurrentState = "Outside",
            UnauthorizedExitWarningCount = 0,
            QrToken = "test-pending-notify",
        };
        using (var db = dbFactory.CreateDbContext())
        {
            if (
                !db.Permits.Any(permit => permit.PermitNumber == pendingApprovalPermit.PermitNumber)
            )
            {
                db.Permits.Add(pendingApprovalPermit);
                db.SaveChanges();
            }
        }

        var pendingWorkflowPermitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var pendingEntry = permitService.RecordPermitScan(pendingWorkflowPermitNumber, "tester");
        Require(pendingEntry.allowed, "pending workflow permit should enter before blocked exit");
        clock.Advance(TimeSpan.FromSeconds(6));
        var pendingAttempt = permitService.RecordPermitScan(pendingWorkflowPermitNumber, "tester");
        Require(!pendingAttempt.allowed, "pending workflow permit should block unauthorized exit");

        clock.SetLocalNow(new DateTime(2026, 4, 27, 10, 0, 0));
        var underReviewPermitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var reviewEntry = permitService.RecordPermitScan(underReviewPermitNumber, "tester");
        Require(reviewEntry.allowed, "under-review permit should enter before blocked exit");
        clock.Advance(TimeSpan.FromSeconds(6));
        var reviewAttempt = permitService.RecordPermitScan(underReviewPermitNumber, "tester");
        Require(!reviewAttempt.allowed, "under-review permit should block unauthorized exit");
        clock.SetLocalNow(clock.LocalNow.Date.AddDays(1).AddHours(9));
        clock.Advance(TimeSpan.FromSeconds(6));
        var reviewFollowUpAttempt = permitService.RecordPermitScan(
            underReviewPermitNumber,
            "tester"
        );
        Require(
            reviewFollowUpAttempt.allowed,
            "under-review permit should recover the stale pending exit and record a next-day entry"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 29, 10, 0, 0));
        var stoppedPermitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var stoppedResult = permitService.StopPermit(stoppedPermitNumber, "reviewer");
        Require(stoppedResult, "manual stop should create a stopped notification item");

        var notificationCenter = reportsDashboardService.BuildNotificationCenterModel(
            5,
            notificationManagerUsername
        );
        var pendingWorkflowSection = notificationCenter.Sections.FirstOrDefault(section =>
            string.Equals(section.Key, "PendingWorkflow", StringComparison.OrdinalIgnoreCase)
        );
        var underReviewSection = notificationCenter.Sections.FirstOrDefault(section =>
            string.Equals(section.Key, "UnderReview", StringComparison.OrdinalIgnoreCase)
        );
        var stoppedSection = notificationCenter.Sections.FirstOrDefault(section =>
            string.Equals(section.Key, "StoppedPermits", StringComparison.OrdinalIgnoreCase)
        );
        var pendingPermitsSection = notificationCenter.Sections.FirstOrDefault(section =>
            string.Equals(section.Key, "PendingPermits", StringComparison.OrdinalIgnoreCase)
        );
        var pendingPermitsDebug =
            pendingPermitsSection == null
                ? "section=null"
                : $"count={pendingPermitsSection.Count}; items={string.Join(" | ", pendingPermitsSection.Items.Select(item => $"{item.DriverName}/{item.PermitNumber}/{item.Subtitle}"))}";

        Require(
            notificationCenter.TotalCount >= 4,
            "notification center should aggregate all active queues"
        );
        Require(
            (pendingWorkflowSection?.Count ?? 0) >= 1,
            "notification center should surface pending workflow cases"
        );
        Require(
            (underReviewSection?.Count ?? 0) >= 1,
            "notification center should surface under-review cases"
        );
        Require(
            (stoppedSection?.Count ?? 0) >= 1,
            "notification center should surface stopped permits"
        );
        Require(
            (pendingPermitsSection?.Count ?? 0) >= 1,
            $"notification center should surface pending approvals; {pendingPermitsDebug}"
        );
        Require(
            pendingPermitsSection!.Items.Any(item =>
                string.Equals(
                    item.PermitNumber,
                    pendingApprovalPermit.PermitNumber,
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "pending approval item should be directly addressable from the notification center"
        );
        Require(
            underReviewSection!.Items.All(item => !string.IsNullOrWhiteSpace(item.SequenceId)),
            "under-review notification items should preserve their sequence id for direct opening"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioAdministrativeReviewCanDismiss(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 20, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "employee permit should enter before the review dismissal path");

        clock.Advance(TimeSpan.FromSeconds(6));
        var attempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(!attempt.allowed, "unauthorized exit should still be blocked before dismissal");

        clock.SetLocalNow(clock.LocalNow.Date.AddDays(1).AddHours(9));
        clock.Advance(TimeSpan.FromSeconds(6));
        var nextDayAttempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            nextDayAttempt.allowed,
            "the next-day scan should promote the previous sequence for review and recover into a new entry"
        );

        var permitAfterReview =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after review escalation");
        Require(
            permitAfterReview.UnauthorizedExitWarningCount == 1,
            "review escalation should increment the unauthorized-exit warning count"
        );

        var reviewActivity = permitService
            .GetPermitActivities(permitNumber)
            .FirstOrDefault(activity =>
                string.Equals(
                    activity.ActionType,
                    "UnauthorizedExitNeedsReview",
                    StringComparison.OrdinalIgnoreCase
                )
            );
        Require(reviewActivity != null, "review escalation should create a review activity");

        var resolved = permitService.ResolveUnauthorizedExitReview(
            reviewActivity!.SequenceId,
            confirmViolation: false,
            performedBy: "admin"
        );
        Require(resolved, "administrative review should be dismissible");

        var permitAfterDismiss =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after dismissal");
        Require(
            permitAfterDismiss.UnauthorizedExitWarningCount == 0,
            "dismissing the review should roll back the warning count"
        );

        var latestActivity = permitService.GetPermitActivities(permitNumber).FirstOrDefault();
        Require(latestActivity != null, "dismissal should record a final activity");
        Require(
            string.Equals(
                latestActivity!.ActionType,
                "AdministrativeReviewDismissed",
                StringComparison.OrdinalIgnoreCase
            ),
            "dismissal should record an explicit administrative resolution"
        );
        Require(
            string.Equals(
                latestActivity.ClassificationStatus,
                "ClosedWithoutViolation",
                StringComparison.OrdinalIgnoreCase
            ),
            "dismissal should close the sequence without a violation"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUnauthorizedExitWorkflowDashboardCategorizesAllStages(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IReportsDashboardService reportsDashboardService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday"
        );
        clock.SetLocalNow(new DateTime(2026, 4, 19, 10, 0, 0));
        var closedPermitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var closedEntry = permitService.RecordPermitScan(closedPermitNumber, "tester");
        Require(closedEntry.allowed, "closed-stage permit should enter before blocked exit");
        clock.Advance(TimeSpan.FromSeconds(6));
        var closedAttempt = permitService.RecordPermitScan(closedPermitNumber, "tester");
        Require(!closedAttempt.allowed, "closed-stage permit should create blocked exit attempt");
        clock.SetLocalNow(new DateTime(2026, 4, 19, 17, 30, 0));
        _ = permitService.ClosePermitsAtWorkEnd("system");

        clock.SetLocalNow(new DateTime(2026, 4, 20, 10, 0, 0));
        var stoppedPermitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var stoppedEntry = permitService.RecordPermitScan(stoppedPermitNumber, "tester");
        Require(stoppedEntry.allowed, "stopped-stage permit should enter before repeated attempts");
        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, stoppedPermitNumber);
        clock.SetLocalNow(new DateTime(2026, 4, 21, 10, 0, 0));
        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, stoppedPermitNumber);
        clock.SetLocalNow(new DateTime(2026, 4, 22, 10, 0, 0));
        RecordConfirmedEntryOnlyViolationDay(clock, dbFactory, permitService, stoppedPermitNumber);

        clock.SetLocalNow(new DateTime(2026, 4, 23, 10, 0, 0));
        var reviewPermitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var reviewEntry = permitService.RecordPermitScan(reviewPermitNumber, "tester");
        Require(reviewEntry.allowed, "review-stage permit should enter before blocked exit");
        clock.Advance(TimeSpan.FromSeconds(6));
        var reviewAttempt = permitService.RecordPermitScan(reviewPermitNumber, "tester");
        Require(!reviewAttempt.allowed, "review-stage permit should create blocked exit attempt");
        clock.SetLocalNow(clock.LocalNow.Date.AddDays(1).AddHours(9));
        clock.Advance(TimeSpan.FromSeconds(6));
        var reviewFollowUpAttempt = permitService.RecordPermitScan(reviewPermitNumber, "tester");
        Require(
            reviewFollowUpAttempt.allowed,
            "review-stage permit should promote the previous day and recover into a new entry on a real scan"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 24, 10, 0, 0));
        var pendingPermitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var pendingEntry = permitService.RecordPermitScan(pendingPermitNumber, "tester");
        Require(pendingEntry.allowed, "pending-stage permit should enter before blocked exit");
        clock.Advance(TimeSpan.FromSeconds(6));
        var pendingAttempt = permitService.RecordPermitScan(pendingPermitNumber, "tester");
        Require(!pendingAttempt.allowed, "pending-stage permit should create blocked exit attempt");

        var pendingDashboard = reportsDashboardService.BuildUnauthorizedExitWorkflowDashboardModel(
            pendingPermitNumber,
            "Pending",
            1,
            20,
            "tester"
        );
        Require(
            pendingDashboard.UnauthorizedExitWorkflowItems.Count == 1,
            "workflow dashboard should isolate the pending sequence"
        );
        Require(
            string.Equals(
                pendingDashboard.UnauthorizedExitWorkflowItems[0].WorkflowStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "pending sequence should be categorized as pending"
        );

        var reviewDashboard = reportsDashboardService.BuildUnauthorizedExitWorkflowDashboardModel(
            reviewPermitNumber,
            "UnderReview",
            1,
            20,
            "tester"
        );
        Require(
            reviewDashboard.UnauthorizedExitWorkflowItems.Count == 1,
            "workflow dashboard should isolate the under-review sequence"
        );
        Require(
            reviewDashboard.UnauthorizedExitWorkflowItems[0].CanResolveReview,
            "under-review sequence should expose review actions"
        );

        var stoppedDashboard = reportsDashboardService.BuildUnauthorizedExitWorkflowDashboardModel(
            stoppedPermitNumber,
            "Stopped",
            1,
            20,
            "tester"
        );
        Require(
            stoppedDashboard.UnauthorizedExitWorkflowItems.Count == 1,
            "workflow dashboard should isolate the stopped sequence"
        );
        Require(
            stoppedDashboard.UnauthorizedExitWorkflowItems[0].IsStopped,
            "stopped sequence should be marked as stopped"
        );

        var closedDashboard = reportsDashboardService.BuildUnauthorizedExitWorkflowDashboardModel(
            closedPermitNumber,
            "Closed",
            1,
            20,
            "tester"
        );
        Require(
            closedDashboard.UnauthorizedExitWorkflowItems.Count == 1,
            "workflow dashboard should isolate the closed sequence"
        );
        Require(
            closedDashboard.UnauthorizedExitWorkflowItems[0].IsClosed,
            "closed sequence should be marked as closed"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioAdministrativeReviewCanConfirm(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 22, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "employee permit should enter before the review confirmation path");

        clock.Advance(TimeSpan.FromSeconds(6));
        var attempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(!attempt.allowed, "unauthorized exit should still be blocked before confirmation");

        clock.SetLocalNow(clock.LocalNow.Date.AddDays(1).AddHours(9));
        clock.Advance(TimeSpan.FromSeconds(6));
        var nextDayAttempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            nextDayAttempt.allowed,
            "the next-day scan should promote the previous sequence for confirmation review and recover into a new entry"
        );

        var permitAfterReview =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after review escalation");
        Require(
            permitAfterReview.UnauthorizedExitWarningCount == 1,
            "review escalation should increment the unauthorized-exit warning count"
        );

        var reviewActivity = permitService
            .GetPermitActivities(permitNumber)
            .FirstOrDefault(activity =>
                string.Equals(
                    activity.ActionType,
                    "UnauthorizedExitNeedsReview",
                    StringComparison.OrdinalIgnoreCase
                )
            );
        Require(reviewActivity != null, "review escalation should create a review activity");

        var resolved = permitService.ResolveUnauthorizedExitReview(
            reviewActivity!.SequenceId,
            confirmViolation: true,
            performedBy: "admin"
        );
        Require(resolved, "administrative review should be confirmable");

        var permitAfterConfirm =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after confirmation");
        Require(
            string.Equals(
                permitAfterConfirm.ApprovalStatus,
                "Stopped",
                StringComparison.OrdinalIgnoreCase
            ),
            "confirming the review should keep the permit stopped until a later reactivation"
        );

        var activities = permitService.GetPermitActivities(permitNumber).ToList();
        var latestActivity = activities.FirstOrDefault();
        Require(latestActivity != null, "confirmation should record a final activity");
        Require(
            string.Equals(
                latestActivity!.ActionType,
                "UnauthorizedExitStopped",
                StringComparison.OrdinalIgnoreCase
            ),
            "confirmation should place the workflow into the stopped stage"
        );
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "AdministrativeReviewConfirmed",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "confirmation should still record the administrative approval event"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioConfirmNoKeepsPermitStoppedAndPublishesStatusMessage(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IReportsDashboardService reportsDashboardService,
        IAccessControlService accessControlService,
        IUserAdminService userAdminService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 22, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            entry.allowed,
            "employee permit should enter before the review confirmation toast path"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var attempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            !attempt.allowed,
            "unauthorized exit should still be blocked before review confirmation toast path"
        );

        clock.SetLocalNow(clock.LocalNow.Date.AddDays(1).AddHours(9));
        clock.Advance(TimeSpan.FromSeconds(6));
        var nextDayAttempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            nextDayAttempt.allowed,
            "the next-day scan should promote the previous sequence for confirmation toast path and recover into a new entry"
        );

        var reviewActivity = permitService
            .GetPermitActivities(permitNumber)
            .FirstOrDefault(activity =>
                string.Equals(
                    activity.ActionType,
                    "UnauthorizedExitNeedsReview",
                    StringComparison.OrdinalIgnoreCase
                )
            );
        Require(
            reviewActivity != null,
            "review escalation should create a review activity for the controller path"
        );

        var controller = CreateReportsController(
            permitService,
            reportsDashboardService,
            accessControlService,
            userAdminService,
            BuildPrincipal(
                "tester",
                AppRoles.GeneralManager,
                AppPermissions.ReviewUnauthorizedExit,
                AppPermissions.ViewPermits
            )
        );

        var result = controller.ResolveUnauthorizedExitReview(
            reviewActivity!.SequenceId,
            "confirm",
            permitNumber,
            permitNumber,
            false,
            10
        );
        Require(
            result
                is RedirectToActionResult { ActionName: nameof(ReportsController.PermitActivity) },
            "review confirmation should redirect to permit activity"
        );
        Require(
            HasQueuedToast(
                controller.TempData,
                "تم اعتماد المخالفة والتصريح ما زال موقوفًا",
                "success"
            ),
            "confirming with no reactivation should publish the stopped status message"
        );

        var permitAfterConfirm =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found after controller confirmation"
            );
        Require(
            string.Equals(
                permitAfterConfirm.ApprovalStatus,
                "Stopped",
                StringComparison.OrdinalIgnoreCase
            ),
            "confirming with no reactivation should keep the permit stopped"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioReactivateLaterWorksAfterConfirmedViolationStop(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 22, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );
        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "employee permit should enter before the reactivation-later path");

        clock.Advance(TimeSpan.FromSeconds(6));
        var attempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            !attempt.allowed,
            "unauthorized exit should still be blocked before later reactivation path"
        );

        clock.SetLocalNow(clock.LocalNow.Date.AddDays(1).AddHours(9));
        clock.Advance(TimeSpan.FromSeconds(6));
        var nextDayAttempt = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            nextDayAttempt.allowed,
            "the next-day scan should promote the previous sequence for later reactivation path and recover into a new entry"
        );

        var reviewActivity = permitService
            .GetPermitActivities(permitNumber)
            .FirstOrDefault(activity =>
                string.Equals(
                    activity.ActionType,
                    "UnauthorizedExitNeedsReview",
                    StringComparison.OrdinalIgnoreCase
                )
            );
        Require(reviewActivity != null, "review escalation should create a review activity");

        Require(
            permitService.ResolveUnauthorizedExitReview(
                reviewActivity!.SequenceId,
                confirmViolation: true,
                performedBy: "admin"
            ),
            "administrative review confirmation should succeed before later reactivation"
        );

        var stoppedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after stop-by-confirmation");
        Require(
            string.Equals(
                stoppedPermit.ApprovalStatus,
                "Stopped",
                StringComparison.OrdinalIgnoreCase
            ),
            "confirmed violation should leave the permit stopped before later reactivation"
        );
        Require(
            stoppedPermit.UnauthorizedExitWarningCount > 0,
            "confirmed violation should keep a visible violation count before later reactivation"
        );
        var stoppedViolationCount = stoppedPermit.UnauthorizedExitWarningCount;

        Require(
            permitService.ReactivatePermit(permitNumber, "admin"),
            "reactivating later should succeed after a confirmed stopped violation"
        );

        var reactivatedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after later reactivation");
        Require(
            string.Equals(
                reactivatedPermit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "reactivating later should return the confirmed violation permit to approved"
        );
        Require(
            reactivatedPermit.UnauthorizedExitWarningCount == stoppedViolationCount,
            "reactivating later should not erase the confirmed violation count"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioWorkEndClosureExitsInsideEmployeesOnce(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        ResetStandardAdministrationSchedule(dbFactory);
        clock.SetLocalNow(new DateTime(2026, 4, 23, 9, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(1)
        );
        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "employee permit should enter before work-end closure");

        clock.SetLocalNow(new DateTime(2026, 4, 23, 17, 30, 0));
        var closedCount = permitService.ClosePermitsAtWorkEnd("system");
        Require(
            closedCount >= 1,
            "work-end closure should close at least one inside employee permit"
        );

        var permitAfterClosure =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after work-end closure");
        Require(
            string.Equals(
                permitAfterClosure.CurrentState,
                "Outside",
                StringComparison.OrdinalIgnoreCase
            ),
            "work-end closure should move the employee outside"
        );
        Require(
            string.Equals(
                permitAfterClosure.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "work-end closure should keep the employee permit approved"
        );
        Require(
            permitAfterClosure.LastAutomaticWorkEndExitAt == new DateTime(2026, 4, 23, 16, 30, 0),
            "work-end closure should stamp the configured end-of-day exit grace close time"
        );
        Require(
            permitAfterClosure.RequiresReturn,
            "work-end closure should not change the permit base return setting"
        );

        var secondClosure = permitService.ClosePermitsAtWorkEnd("system");
        Require(
            secondClosure == 0,
            "work-end closure should be idempotent for the same closure time"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioAttendanceGraceRecordsLateAttendance(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IReportsDashboardService reportsDashboardService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday",
            attendanceGraceMinutes: 15,
            workEndExitGraceMinutes: 30
        );
        clock.SetLocalNow(new DateTime(2026, 4, 23, 8, 20, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(1)
        );
        var scan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(scan.allowed, "late employee attendance should still be allowed to enter");

        var activities = permitService.GetPermitActivities(permitNumber).ToList();
        var lateAttendance =
            activities.FirstOrDefault(activity =>
                string.Equals(
                    activity.ActionType,
                    "LateAttendance",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            ?? throw new InvalidOperationException(
                "late attendance should be recorded after grace period"
            );
        Require(
            lateAttendance.LateMinutes == 5,
            "late attendance should count only minutes after the attendance grace window"
        );

        var dashboard = reportsDashboardService.BuildPermitActivityDashboardModel(
            permitNumber,
            null,
            1,
            20,
            "tester"
        );
        Require(
            dashboard.AttendanceEntryCount == 1,
            "late attendance should count as one attendance day"
        );
        Require(
            dashboard.LateAttendanceCount == 1,
            "late attendance should be counted in the report"
        );
        Require(
            dashboard.AttendanceCommitmentPercent == 0,
            "one late attendance day should produce zero attendance commitment"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioAfterHoursScansDoNotRecordAttendanceViolations(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday",
            attendanceGraceMinutes: 15,
            workEndExitGraceMinutes: 30
        );
        clock.SetLocalNow(new DateTime(2026, 4, 23, 20, 30, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(1)
        );
        Require(
            permitService.RecordPermitScan(permitNumber, "tester").allowed,
            "after-hours employee entry should still be recorded"
        );

        clock.Advance(TimeSpan.FromMinutes(1));
        Require(
            permitService.RecordPermitScan(permitNumber, "tester").allowed,
            "after-hours employee exit should still be recorded"
        );

        var activities = permitService.GetPermitActivities(permitNumber).ToList();
        Require(
            !activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "LateAttendance",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "after-hours entry should not be classified as late attendance"
        );
        Require(
            !activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "LateCheckout",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "after-hours exit without an in-shift entry should not be classified as late checkout"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioWorkEndClosureWaitsForCheckoutGraceAndMarksLateCheckout(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService,
        IReportsDashboardService reportsDashboardService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(14, 30),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday",
            attendanceGraceMinutes: 15,
            workEndExitGraceMinutes: 30
        );
        clock.SetLocalNow(new DateTime(2026, 4, 23, 9, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(1)
        );
        Require(
            permitService.RecordPermitScan(permitNumber, "tester").allowed,
            "employee permit should enter before checkout grace test"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 23, 14, 45, 0));
        Require(
            permitService.ClosePermitsAtWorkEnd("system") == 0,
            "work-end closure should wait until the checkout grace closes"
        );
        var permitBeforeGraceClose =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found before checkout grace closes");
        Require(
            string.Equals(
                permitBeforeGraceClose.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            "permit should remain inside during checkout grace"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 23, 15, 1, 0));
        Require(
            permitService.ClosePermitsAtWorkEnd("system") == 1,
            "work-end closure should close permits after checkout grace"
        );
        var permitAfterGraceClose =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after checkout grace closes");
        Require(
            string.Equals(
                permitAfterGraceClose.CurrentState,
                "Outside",
                StringComparison.OrdinalIgnoreCase
            ),
            "permit should move outside after checkout grace closes"
        );
        Require(
            permitAfterGraceClose.LastAutomaticWorkEndExitAt == new DateTime(2026, 4, 23, 15, 0, 0),
            "automatic checkout should use work end plus checkout grace"
        );

        var activities = permitService.GetPermitActivities(permitNumber).ToList();
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "WorkEndExit",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "automatic checkout should record work-end exit"
        );
        Require(
            activities.Any(activity =>
                string.Equals(
                    activity.ActionType,
                    "LateCheckout",
                    StringComparison.OrdinalIgnoreCase
                )
            ),
            "automatic checkout after grace should record late checkout"
        );

        var dashboard = reportsDashboardService.BuildPermitActivityDashboardModel(
            permitNumber,
            null,
            1,
            20,
            "tester"
        );
        Require(dashboard.CheckoutExitCount == 1, "late checkout should count as one checkout day");
        Require(dashboard.LateCheckoutCount == 1, "late checkout should be counted in the report");
        Require(
            dashboard.CheckoutCommitmentPercent == 0,
            "one late checkout day should produce zero checkout commitment"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }
}
