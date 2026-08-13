using Npgsql;
using VehiclePermitSystemWeb.Data;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Database)]
public sealed class PostgreSqlConnectionStringNormalizerTests
{
    [Fact]
    public void ConvertsPostgreSqlUrlToNpgsqlConnectionString()
    {
        var normalized = PostgreSqlConnectionStringNormalizer.Normalize(
            "postgresql://owner:p%40ss%3Bword@db.example.com:5433/permit_db?sslmode=require"
        );

        var parsed = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("db.example.com", parsed.Host);
        Assert.Equal(5433, parsed.Port);
        Assert.Equal("permit_db", parsed.Database);
        Assert.Equal("owner", parsed.Username);
        Assert.Equal("p@ss;word", parsed.Password);
        Assert.Equal(SslMode.Require, parsed.SslMode);
    }

    [Fact]
    public void PreservesStandardNpgsqlConnectionString()
    {
        const string connectionString =
            "Host=127.0.0.1;Port=5432;Database=permit_db;Username=postgres";

        var normalized = PostgreSqlConnectionStringNormalizer.Normalize(connectionString);

        Assert.Equal(connectionString, normalized);
    }
}
