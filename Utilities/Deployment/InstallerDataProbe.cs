using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace VehiclePermitSystemWeb.Utilities.Deployment
{
    public static class InstallerDataProbe
    {
        public static bool TryHandle(string[] args)
        {
            if (
                !args.Any(arg =>
                    string.Equals(arg, "--probe-install-state", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return false;
            }

            var databasePath = GetArgumentValue(args, "--database-path");
            var runtimeConfigPath = GetArgumentValue(args, "--runtime-config-path");
            var result = Probe(databasePath, runtimeConfigPath);

            Console.Out.Write(JsonSerializer.Serialize(result));
            return true;
        }

        private static InstallerProbeResult Probe(string? databasePath, string? runtimeConfigPath)
        {
            var result = new InstallerProbeResult
            {
                DatabasePath = databasePath ?? string.Empty,
                RuntimeConfigPath = runtimeConfigPath ?? string.Empty,
                HasDatabase = !string.IsNullOrWhiteSpace(databasePath) && File.Exists(databasePath),
                RuntimeConfigExists =
                    !string.IsNullOrWhiteSpace(runtimeConfigPath) && File.Exists(runtimeConfigPath),
            };

            if (!result.HasDatabase)
            {
                return result;
            }

            try
            {
                using var connection = new SqliteConnection($"Data Source={databasePath}");
                connection.Open();

                result.HasUserTable = TableExists(connection, "UserAccounts");
                result.HasAdministrationSettingsTable = TableExists(
                    connection,
                    "AdministrationSettings"
                );

                if (result.HasUserTable)
                {
                    using var usersCommand = connection.CreateCommand();
                    usersCommand.CommandText = "SELECT COUNT(*) FROM UserAccounts;";
                    result.UserCount = Convert.ToInt32(usersCommand.ExecuteScalar() ?? 0);
                    result.HasUsers = result.UserCount > 0;
                }

                if (result.HasAdministrationSettingsTable)
                {
                    if (
                        ColumnExists(
                            connection,
                            "AdministrationSettings",
                            "IsInitialSetupCompleted"
                        )
                    )
                    {
                        using var setupCommand = connection.CreateCommand();
                        setupCommand.CommandText =
                            "SELECT COALESCE(IsInitialSetupCompleted, 0) FROM AdministrationSettings ORDER BY Id LIMIT 1;";
                        result.IsInitialSetupCompleted =
                            Convert.ToInt32(setupCommand.ExecuteScalar() ?? 0) == 1;
                    }
                }
            }
            catch (Exception exception)
            {
                result.Error = exception.Message;
            }

            result.IsUpgradeCandidate =
                result.HasDatabase
                && (
                    result.HasUsers || result.IsInitialSetupCompleted || result.RuntimeConfigExists
                );
            result.RequiresFreshInstall = !result.HasUsers && !result.IsInitialSetupCompleted;
            return result;
        }

        private static bool TableExists(SqliteConnection connection, string tableName)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
            command.Parameters.AddWithValue("$name", tableName);
            return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
        }

        private static bool ColumnExists(
            SqliteConnection connection,
            string tableName,
            string columnName
        )
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (
                    string.Equals(
                        reader.GetString(1),
                        columnName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static string? GetArgumentValue(string[] args, string key)
        {
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        private sealed class InstallerProbeResult
        {
            public string DatabasePath { get; set; } = string.Empty;

            public string RuntimeConfigPath { get; set; } = string.Empty;

            public bool HasDatabase { get; set; }

            public bool RuntimeConfigExists { get; set; }

            public bool HasUserTable { get; set; }

            public bool HasAdministrationSettingsTable { get; set; }

            public int UserCount { get; set; }

            public bool HasUsers { get; set; }

            public bool IsInitialSetupCompleted { get; set; }

            public bool IsUpgradeCandidate { get; set; }

            public bool RequiresFreshInstall { get; set; }

            public string? Error { get; set; }
        }
    }
}
