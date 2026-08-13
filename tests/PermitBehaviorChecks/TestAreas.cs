namespace PermitBehaviorChecks;

internal static class TestAreas
{
    public const string Authentication = "Authentication";
    public const string Permits = "Permits";
    public const string VehiclePermits = "VehiclePermits";
    public const string VehicleScan = "VehicleScan";
    public const string Visits = "Visits";
    public const string Attendance = "Attendance";
    public const string Administration = "Administration";
    public const string Security = "Security";
    public const string Database = "Database";
    public const string Notifications = "Notifications";

    public static readonly IReadOnlyList<string> All =
    [
        Authentication,
        Permits,
        VehiclePermits,
        VehicleScan,
        Visits,
        Attendance,
        Administration,
        Security,
        Database,
        Notifications,
    ];
}

internal static class TestAreaClassifier
{
    public static string Resolve(string scenarioName)
    {
        var name = scenarioName.ToLowerInvariant();

        if (ContainsAny(name, "vehicle", "plate", "zebra label"))
        {
            return TestAreas.VehiclePermits;
        }

        if (ContainsAny(name, "spoof", "authorization", "permission", "tenant boundary", "rate limit", "secure qr", "access key", "hardcoded password", "protected action"))
        {
            return TestAreas.Security;
        }

        if (ContainsAny(name, "backup", "restore", "upgrade", "bootstrap", "legacy database", "retention"))
        {
            return TestAreas.Database;
        }

        if (ContainsAny(name, "password", "login", "session invalidates", "account session", "initial system owner"))
        {
            return TestAreas.Authentication;
        }

        if (ContainsAny(name, "notification", "toast", "inline bootstrap alert", "access denied page"))
        {
            return TestAreas.Notifications;
        }

        if (ContainsAny(name, "attendance", "leave", "work hour", "work end", "checkout", "unauthorized exit", "entryonly", "fullaccess", "movement", "late return", "official workday", "emergency session"))
        {
            return TestAreas.Attendance;
        }

        if (ContainsAny(name, "gate", "scan", "display", "operator note", "waiting board", "qr token"))
        {
            return TestAreas.VehicleScan;
        }

        if (ContainsAny(name, "visit", "visitor", "queue"))
        {
            return TestAreas.Visits;
        }

        if (ContainsAny(name, "permit", "employee", "violation", "reactivate"))
        {
            return TestAreas.Permits;
        }

        return TestAreas.Administration;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(value.Contains);
}
