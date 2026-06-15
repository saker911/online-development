using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

if (args.Length < 1)
{
    Fail("Missing command.");
}

var command = args[0];
try
{
    switch (command)
    {
        case "prepare-entry-only-stale-pending":
            PrepareEntryOnlyStalePending(args);
            break;
        case "read-entry-only-recovery":
            ReadEntryOnlyRecovery(args);
            break;
        default:
            Fail($"Unknown command '{command}'.");
            break;
    }
}
catch (Exception ex)
{
    Fail(ex.Message);
}

static void PrepareEntryOnlyStalePending(string[] args)
{
    if (args.Length < 5)
    {
        Fail(
            "Usage: prepare-entry-only-stale-pending <dbPath> <permitNumber> <entryAt> <pendingAt>"
        );
    }

    var dbPath = args[1];
    var permitNumber = args[2];
    var entryAt = ParseDateTime(args[3]);
    var pendingAt = ParseDateTime(args[4]);
    var sequenceId = Guid.NewGuid().ToString("N");

    using var connection = OpenConnection(dbPath);
    using var transaction = connection.BeginTransaction();

    var settingsUpdated = ExecuteNonQuery(
        connection,
        transaction,
        """
        UPDATE AdministrationSettings
        SET
            WorkStartTime = '08:00:00',
            WorkEndTime = '23:59:00',
            AttendanceGraceMinutes = 15,
            WorkEndExitGraceMinutes = 30,
            LateReturnGraceMinutes = 5,
            OfficialWorkDaysCsv = 'Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday',
            LastWorkEndClosureAt = NULL
        WHERE Id = 1;
        """
    );

    if (settingsUpdated == 0)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO AdministrationSettings (
                Id,
                IsInitialSetupCompleted,
                OrganizationName,
                DepartmentName,
                Address,
                Phone,
                Email,
                GeneralManagerUsername,
                ManagerName,
                ManagerTitle,
                ManagerPhoneNumber,
                SignatureText,
                LogoPath,
                SignatureImagePath,
                DisplayBaseUrl,
                DisplayAccessKey,
                AllowedClientIpRanges,
                WorkStartTime,
                WorkEndTime,
                AttendanceGraceMinutes,
                WorkEndExitGraceMinutes,
                LateReturnGraceMinutes,
                LastWorkEndClosureAt,
                OfficialWorkDaysCsv
            )
            VALUES (
                1,
                1,
                'E2E',
                '',
                '',
                '',
                '',
                '',
                '',
                '',
                '',
                '',
                NULL,
                NULL,
                '',
                '',
                '',
                '08:00:00',
                '23:59:00',
                15,
                30,
                5,
                NULL,
                'Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday'
            );
            """
        );
    }

    var updated = ExecuteNonQuery(
        connection,
        transaction,
        """
        UPDATE Permits
        SET
            PermitType = 'Permanent',
            RequiresReturn = 0,
            AccessMode = 'EntryOnly',
            ApprovalStatus = 'Approved',
            CurrentState = 'Inside',
            PermitDate = $entryAt,
            ReturnTime = $entryAt,
            OutTime = NULL,
            ExpectedReturnTime = NULL,
            LeaveWindowStartAt = NULL,
            LeaveWindowEndAt = NULL,
            PendingExitRequest = 0,
            LeaveReason = '',
            ArchivedAt = NULL,
            PendingUnauthorizedExitAt = $pendingAt,
            PendingUnauthorizedExitSequenceId = $sequenceId,
            LastAutomaticWorkEndExitAt = NULL,
            UnauthorizedExitWarningCount = 0,
            LastUnauthorizedExitWarningAt = NULL
        WHERE PermitNumber = $permitNumber;
        """,
        new Dictionary<string, object?>
        {
            ["$permitNumber"] = permitNumber,
            ["$entryAt"] = entryAt,
            ["$pendingAt"] = pendingAt,
            ["$sequenceId"] = sequenceId,
        }
    );

    if (updated != 1)
    {
        Fail($"Permit '{permitNumber}' was not found for E2E state preparation.");
    }

    ExecuteNonQuery(
        connection,
        transaction,
        "DELETE FROM PermitActivities WHERE PermitNumber = $permitNumber;",
        new Dictionary<string, object?> { ["$permitNumber"] = permitNumber }
    );

    InsertActivity(
        connection,
        transaction,
        permitNumber,
        "Entry",
        "تسجيل دخول",
        "تم تسجيل دخول التصريح ضمن تجهيز اختبار E2E.",
        "E2E setup",
        "Entry",
        string.Empty,
        "Completed",
        entryAt
    );
    InsertActivity(
        connection,
        transaction,
        permitNumber,
        "PendingUnauthorizedExit",
        "محاولة خروج معلقة",
        "تم رفض خروج التصريح ضمن تجهيز اختبار E2E.",
        "E2E setup",
        "PendingUnauthorizedExit",
        sequenceId,
        "Pending",
        pendingAt
    );

    transaction.Commit();
    WriteJson(new { sequenceId });
}

static void ReadEntryOnlyRecovery(string[] args)
{
    if (args.Length < 4)
    {
        Fail("Usage: read-entry-only-recovery <dbPath> <permitNumber> <currentDay>");
    }

    var dbPath = args[1];
    var permitNumber = args[2];
    var currentDay = DateTime.Parse(args[3], CultureInfo.InvariantCulture).Date;

    using var connection = OpenConnection(dbPath);
    using var permitCommand = connection.CreateCommand();
    permitCommand.CommandText = """
        SELECT ApprovalStatus, CurrentState, OutTime, ReturnTime, LastAutomaticWorkEndExitAt,
               PendingUnauthorizedExitAt, PendingUnauthorizedExitSequenceId
        FROM Permits
        WHERE PermitNumber = $permitNumber;
        """;
    permitCommand.Parameters.AddWithValue("$permitNumber", permitNumber);

    using var reader = permitCommand.ExecuteReader();
    if (!reader.Read())
    {
        Fail($"Permit '{permitNumber}' was not found while reading E2E recovery state.");
    }

    var permitState = new
    {
        approvalStatus = ReadNullableString(reader, 0),
        currentState = ReadNullableString(reader, 1),
        outTime = ReadNullableString(reader, 2),
        returnTime = ReadNullableString(reader, 3),
        lastAutomaticWorkEndExitAt = ReadNullableString(reader, 4),
        pendingUnauthorizedExitAt = ReadNullableString(reader, 5),
        pendingUnauthorizedExitSequenceId = ReadNullableString(reader, 6),
    };
    reader.Close();

    var activities = ReadActivities(connection, permitNumber);
    WriteJson(
        new
        {
            permit = permitState,
            unauthorizedExitNeedsReviewCount = activities.Count(activity =>
                IsAction(activity.ActionType, "UnauthorizedExitNeedsReview")
            ),
            workEndExitCount = activities.Count(activity =>
                IsAction(activity.ActionType, "WorkEndExit")
            ),
            currentDayEntryCount = activities.Count(activity =>
                IsAction(activity.ActionType, "Entry") && activity.OccurredAt.Date == currentDay
            ),
            currentDayPendingUnauthorizedExitCount = activities.Count(activity =>
                IsAction(activity.ActionType, "PendingUnauthorizedExit")
                && activity.OccurredAt.Date == currentDay
            ),
        }
    );
}

static SqliteConnection OpenConnection(string dbPath)
{
    if (!File.Exists(dbPath))
    {
        Fail($"Database file was not found: {dbPath}");
    }

    var builder = new SqliteConnectionStringBuilder { DataSource = dbPath };
    var connection = new SqliteConnection(builder.ToString());
    connection.Open();
    return connection;
}

static int ExecuteNonQuery(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string sql,
    IReadOnlyDictionary<string, object?>? parameters = null
)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    if (parameters != null)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
        }
    }

    return command.ExecuteNonQuery();
}

static void InsertActivity(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string permitNumber,
    string actionType,
    string actionLabel,
    string message,
    string source,
    string reasonCode,
    string sequenceId,
    string classificationStatus,
    DateTime occurredAt
)
{
    ExecuteNonQuery(
        connection,
        transaction,
        """
        INSERT INTO PermitActivities (
            PermitNumber,
            DriverName,
            NationalId,
            DepartmentName,
            ActionType,
            ActionLabel,
            Message,
            Source,
            RecordedBy,
            ReasonCode,
            SequenceId,
            ClassificationStatus,
            GateName,
            GateOperatorName,
            GateOperatorAccount,
            DeviceId,
            IpAddress,
            ExecutionMethod,
            IsAutomated,
            OccurredAt,
            LateMinutes
        )
        SELECT
            PermitNumber,
            DriverName,
            NationalId,
            DepartmentName,
            $actionType,
            $actionLabel,
            $message,
            $source,
            'e2e',
            $reasonCode,
            $sequenceId,
            $classificationStatus,
            'E2E setup',
            '',
            '',
            'e2e-state-tool',
            '',
            'setup',
            1,
            $occurredAt,
            NULL
        FROM Permits
        WHERE PermitNumber = $permitNumber;
        """,
        new Dictionary<string, object?>
        {
            ["$permitNumber"] = permitNumber,
            ["$actionType"] = actionType,
            ["$actionLabel"] = actionLabel,
            ["$message"] = message,
            ["$source"] = source,
            ["$reasonCode"] = reasonCode,
            ["$sequenceId"] = sequenceId,
            ["$classificationStatus"] = classificationStatus,
            ["$occurredAt"] = occurredAt,
        }
    );
}

static List<(string ActionType, DateTime OccurredAt)> ReadActivities(
    SqliteConnection connection,
    string permitNumber
)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT ActionType, OccurredAt
        FROM PermitActivities
        WHERE PermitNumber = $permitNumber;
        """;
    command.Parameters.AddWithValue("$permitNumber", permitNumber);

    var activities = new List<(string ActionType, DateTime OccurredAt)>();
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        activities.Add(
            (
                reader.GetString(0),
                DateTime.Parse(
                    Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!,
                    CultureInfo.InvariantCulture
                )
            )
        );
    }

    return activities;
}

static DateTime ParseDateTime(string value)
{
    return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
}

static string? ReadNullableString(SqliteDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal)
        ? null
        : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}

static bool IsAction(string actual, string expected)
{
    return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}

static void WriteJson(object value)
{
    Console.WriteLine(JsonSerializer.Serialize(value));
}

static void Fail(string message)
{
    Console.Error.WriteLine(message);
    Environment.Exit(2);
}
