using Npgsql;

namespace VehiclePermitSystemWeb.Data;

public static class PostgreSqlConnectionStringNormalizer
{
    public static string Normalize(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var trimmedConnectionString = connectionString.Trim();
        if (
            !Uri.TryCreate(trimmedConnectionString, UriKind.Absolute, out var connectionUri)
            || (
                !string.Equals(
                    connectionUri.Scheme,
                    "postgresql",
                    StringComparison.OrdinalIgnoreCase
                )
                && !string.Equals(
                    connectionUri.Scheme,
                    "postgres",
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            return trimmedConnectionString;
        }

        var userInfo = connectionUri.UserInfo.Split(':', 2);
        var database = Uri.UnescapeDataString(connectionUri.AbsolutePath.TrimStart('/'));
        if (userInfo.Length == 0 || string.IsNullOrWhiteSpace(userInfo[0]))
        {
            throw new InvalidOperationException(
                "The PostgreSQL URL must include a username."
            );
        }

        if (string.IsNullOrWhiteSpace(connectionUri.Host) || string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException(
                "The PostgreSQL URL must include a host and database name."
            );
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = connectionUri.Host,
            Port = connectionUri.IsDefaultPort ? 5432 : connectionUri.Port,
            Database = database,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length == 2 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            SslMode = SslMode.Require,
        };

        return builder.ConnectionString;
    }
}
