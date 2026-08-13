using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Services.Users;

if (args.Length == 4 && string.Equals(args[0], "--reset-password", StringComparison.OrdinalIgnoreCase))
{
    var resetDatabasePath = Path.GetFullPath(args[1]);
    if (!File.Exists(resetDatabasePath))
    {
        Console.Error.WriteLine($"Database was not found: {resetDatabasePath}");
        return 2;
    }

    var passwordCarrier = new UserAccount();
    UserAccountService.SetPassword(passwordCarrier, args[3]);
    await using var resetConnection = new SqliteConnection($"Data Source={resetDatabasePath}");
    await resetConnection.OpenAsync();
    await using var resetCommand = resetConnection.CreateCommand();
    resetCommand.CommandText =
        "UPDATE UserAccounts SET PasswordHash = $hash, PasswordSalt = $salt, MustChangePassword = 0, IsActive = 1 WHERE Username = $username;";
    resetCommand.Parameters.AddWithValue("$hash", passwordCarrier.PasswordHash);
    resetCommand.Parameters.AddWithValue("$salt", passwordCarrier.PasswordSalt);
    resetCommand.Parameters.AddWithValue("$username", args[2]);
    var resetRows = await resetCommand.ExecuteNonQueryAsync();
    if (resetRows != 1)
    {
        Console.Error.WriteLine($"User was not found: {args[2]}");
        return 3;
    }

    Console.WriteLine($"Password reset completed for {args[2]}.");
    return 0;
}

if (args.Length != 5)
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project tests/LocalTenantFixtures -- <sqlite-db-path> <alpha-password> <beta-password> <gamma-password> <delta-password>"
    );
    Console.Error.WriteLine(
        "   or: dotnet run --project tests/LocalTenantFixtures -- --reset-password <sqlite-db-path> <username> <password>"
    );
    return 1;
}

var databasePath = Path.GetFullPath(args[0]);
if (!File.Exists(databasePath))
{
    Console.Error.WriteLine($"Database was not found: {databasePath}");
    return 2;
}

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlite($"Data Source={databasePath}")
    .Options;
await using var db = new ApplicationDbContext(options, new DefaultTenantContext());

var fixtures = new[]
{
    new TenantFixture("qa-alpha", "جهة اختبار ألف", "1999000001", "2999000001", args[1]),
    new TenantFixture("qa-beta", "جهة اختبار باء", "1999000002", "2999000002", args[2]),
    new TenantFixture("qa-gamma", "جهة اختبار جيم", "1999000003", "2999000003", args[3]),
    new TenantFixture("qa-delta", "جهة اختبار دال", "1999000004", "2999000004", args[4]),
};

var nextSettingsId =
    (await db.AdministrationSettings.IgnoreQueryFilters().MaxAsync(item => (int?)item.Id) ?? 0) + 1;
var platformSettings = await db
    .AdministrationSettings.IgnoreQueryFilters()
    .SingleOrDefaultAsync(item => item.TenantId == TenantDefaults.DefaultTenantId);
if (platformSettings == null)
{
    platformSettings = new AdministrationSettings
    {
        Id = nextSettingsId++,
        TenantId = TenantDefaults.DefaultTenantId,
        OrganizationName = "منصة الاختبار المحلية",
        DepartmentName = "إدارة المنصة",
    };
    db.AdministrationSettings.Add(platformSettings);
}
platformSettings.IsInitialSetupCompleted = true;

foreach (var fixture in fixtures)
{
    var tenant = await db.Tenants.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
        item.TenantId == fixture.TenantId
    );
    if (tenant == null)
    {
        tenant = new Tenant { TenantId = fixture.TenantId };
        db.Tenants.Add(tenant);
    }

    tenant.Name = fixture.Name;
    tenant.Slug = fixture.TenantId;
    tenant.OrganizationReference = $"QA-{fixture.TenantId[3..].ToUpperInvariant()}";
    tenant.IsActive = true;
    tenant.SubscriptionStatus = TenantSubscriptionStatuses.Active;
    tenant.PlanName = "اختبار تشغيلي";
    tenant.PermitsServiceEnabled = true;
    tenant.VisitsServiceEnabled = true;
    tenant.SelfServiceEnabled = true;
    tenant.QueueServiceEnabled = true;
    tenant.GateServiceEnabled = true;

    var departmentName = $"إدارة {fixture.Name}";
    var settings = await db
        .AdministrationSettings.IgnoreQueryFilters()
        .SingleOrDefaultAsync(item => item.TenantId == fixture.TenantId);
    if (settings == null)
    {
        settings = new AdministrationSettings
        {
            Id = nextSettingsId++,
            TenantId = fixture.TenantId,
        };
        db.AdministrationSettings.Add(settings);
    }

    settings.OrganizationName = fixture.Name;
    settings.DepartmentName = departmentName;
    settings.GeneralManagerUsername = fixture.ManagerUsername;
    settings.ManagerName = $"مدير {fixture.Name}";
    settings.ManagerTitle = "مدير الجهة";
    settings.ManagerPhoneNumber = $"055900000{fixture.ManagerUsername[^1]}";
    settings.IsInitialSetupCompleted = true;

    if (
        !await db.Departments.IgnoreQueryFilters().AnyAsync(item =>
            item.TenantId == fixture.TenantId && item.Name == departmentName
        )
    )
    {
        db.Departments.Add(
            new Department
            {
                TenantId = fixture.TenantId,
                Name = departmentName,
                IsActive = true,
            }
        );
    }

    UpsertUser(
        db,
        fixture.TenantId,
        fixture.ManagerUsername,
        $"مدير {fixture.Name}",
        departmentName,
        "مدير الجهة",
        $"055900000{fixture.ManagerUsername[^1]}",
        $"manager-{fixture.TenantId}@example.test",
        AppRoles.GeneralManager,
        fixture.Password
    );
    UpsertUser(
        db,
        fixture.TenantId,
        fixture.EmployeeUsername,
        $"موظف {fixture.Name}",
        departmentName,
        "موظف اختبار",
        $"056900000{fixture.EmployeeUsername[^1]}",
        $"employee-{fixture.TenantId}@example.test",
        AppRoles.Employee,
        fixture.Password
    );
}

await db.SaveChangesAsync();

foreach (var fixture in fixtures)
{
    Console.WriteLine(
        $"{fixture.Name}|{fixture.TenantId}|{fixture.ManagerUsername}|{fixture.Password}|{fixture.EmployeeUsername}"
    );
}

return 0;

static void UpsertUser(
    ApplicationDbContext db,
    string tenantId,
    string username,
    string fullName,
    string department,
    string jobTitle,
    string phone,
    string email,
    string role,
    string password
)
{
    var user = db.UserAccounts.IgnoreQueryFilters().SingleOrDefault(item => item.Username == username);
    if (user == null)
    {
        user = new UserAccount { Username = username };
        db.UserAccounts.Add(user);
    }

    user.TenantId = tenantId;
    user.DisplayName = fullName;
    user.FullName = fullName;
    user.Department = department;
    user.EmployeeNumber = $"QA-{username[^4..]}";
    user.JobTitle = jobTitle;
    user.PhoneNumber = phone;
    user.Email = email;
    user.IsEmailConfirmed = true;
    user.IsActive = true;
    user.IsSuperAdmin = false;
    user.MustChangePassword = false;
    user.Role = role;
    user.ManagerUsername = string.Empty;
    AppPermissions.ApplyRoleDefaults(user);
    UserAccountService.SetPassword(user, password);
}

internal sealed record TenantFixture(
    string TenantId,
    string Name,
    string ManagerUsername,
    string EmployeeUsername,
    string Password
);
