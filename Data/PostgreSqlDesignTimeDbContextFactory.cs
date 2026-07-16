using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VehiclePermitSystemWeb.Data;

public sealed class PostgreSqlDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSqlConnection")
            ?? "Host=127.0.0.1;Port=5432;Database=vehicle_permit_online_design;Username=postgres";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(PostgreSqlConnectionStringNormalizer.Normalize(connectionString))
            .Options;

        return new ApplicationDbContext(options);
    }
}
