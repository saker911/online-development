using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using VehiclePermitSystemWeb.Utilities.Deployment;

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static async Task ScenarioBackupCreatesReadableSqliteArchive()
    {
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"VehiclePermitSystemWeb-backup-test-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(testRoot);

        try
        {
            Directory.SetCurrentDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "VehiclePermitSystemWeb.csproj"),
                "<Project />"
            );

            var databasePath = AppStoragePaths.GetDatabasePath(
                "vehicle-permit-system.db",
                useDevelopmentLocalData: true
            );
            CreateRestoreProbeDatabase(databasePath, "backup-source");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Data:Provider"] = "Sqlite",
                        ["DevelopmentUseLocalData"] = "true",
                        ["Backup:Enabled"] = "true",
                        ["Backup:RetentionDays"] = "30",
                        ["ConnectionStrings:SqliteConnection"] =
                            "Data Source=vehicle-permit-system.db",
                    }
                )
                .Build();

            var backupService = new BackupService(
                configuration,
                NullLogger<BackupService>.Instance,
                new ScenarioHostEnvironment(testRoot),
                new MutableSystemClock(new DateTime(2026, 4, 13, 9, 0, 0))
            );

            var backup = await backupService.CreateBackupAsync("scenario");
            Require(
                backup != null,
                "backup service should create a backup when SQLite backups are enabled"
            );

            var backupPath = Path.Combine(backupService.BackupRootPath, backup!.FileName);
            Require(File.Exists(backupPath), "backup archive should exist on disk");
            Require(backup.SizeBytes > 0, "backup archive should not be empty");

            using (var archive = ZipFile.OpenRead(backupPath))
            {
                var databaseEntry = archive.Entries.SingleOrDefault(entry =>
                    string.Equals(
                        entry.Name,
                        "vehicle-permit-system.db",
                        StringComparison.OrdinalIgnoreCase
                    )
                );
                Require(
                    databaseEntry != null,
                    "backup archive should include the SQLite database file"
                );
                Require(databaseEntry!.Length > 0, "backup database entry should contain data");
            }

            File.Delete(backupPath);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            try
            {
                Directory.Delete(testRoot, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static async Task ScenarioBackupRestoreVerifiesAndRestoresSqliteArchive()
    {
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"VehiclePermitSystemWeb-restore-test-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(testRoot);

        try
        {
            Directory.SetCurrentDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "VehiclePermitSystemWeb.csproj"),
                "<Project />"
            );

            var databasePath = AppStoragePaths.GetDatabasePath(
                "vehicle-permit-system.db",
                useDevelopmentLocalData: true
            );
            CreateRestoreProbeDatabase(databasePath, "original");

            var backupService = CreateBackupScenarioService(
                testRoot,
                new DateTime(2026, 4, 13, 10, 0, 0)
            );

            var backup = await backupService.CreateBackupAsync("restoretest");
            Require(backup != null, "restore test should create a source backup archive");

            UpdateRestoreProbeValue(databasePath, "changed");
            Require(
                ReadRestoreProbeValue(databasePath) == "changed",
                "restore test should mutate the live database before restore"
            );

            var backupPath = Path.Combine(backupService.BackupRootPath, backup!.FileName);
            await using (var stream = File.OpenRead(backupPath))
            {
                var formFile = new FormFile(
                    stream,
                    0,
                    stream.Length,
                    "backupFile",
                    backup.FileName
                );
                await backupService.RestoreBackupAsync(formFile);
            }

            Require(
                ReadRestoreProbeValue(databasePath) == "original",
                "restore should replace the live database with the verified backup contents"
            );

            File.Delete(backupPath);
            foreach (
                var safetyBackup in Directory.GetFiles(
                    backupService.BackupRootPath,
                    "*restorepre*.zip",
                    SearchOption.TopDirectoryOnly
                )
            )
            {
                File.Delete(safetyBackup);
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            try
            {
                Directory.Delete(testRoot, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static async Task ScenarioBackupRestoreRejectsCorruptArchiveAndKeepsCurrentDatabase()
    {
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"VehiclePermitSystemWeb-corrupt-restore-test-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(testRoot);

        try
        {
            Directory.SetCurrentDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "VehiclePermitSystemWeb.csproj"),
                "<Project />"
            );

            var databasePath = AppStoragePaths.GetDatabasePath(
                "vehicle-permit-system.db",
                useDevelopmentLocalData: true
            );
            CreateRestoreProbeDatabase(databasePath, "current");

            var backupService = CreateBackupScenarioService(
                testRoot,
                new DateTime(2026, 4, 13, 11, 0, 0)
            );

            var corruptArchivePath = Path.Combine(testRoot, "corrupt-backup.zip");
            using (var archive = ZipFile.Open(corruptArchivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("vehicle-permit-system.db");
                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream);
                await writer.WriteAsync("not a sqlite database");
            }

            var rejected = false;
            await using (var stream = File.OpenRead(corruptArchivePath))
            {
                var formFile = new FormFile(
                    stream,
                    0,
                    stream.Length,
                    "backupFile",
                    Path.GetFileName(corruptArchivePath)
                );

                try
                {
                    await backupService.RestoreBackupAsync(formFile);
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }
            }

            Require(rejected, "restore should reject a corrupt database inside a ZIP archive");
            Require(
                ReadRestoreProbeValue(databasePath) == "current",
                "failed restore should leave the current database unchanged"
            );

            foreach (
                var safetyBackup in Directory.GetFiles(
                    backupService.BackupRootPath,
                    "*restorepre*.zip",
                    SearchOption.TopDirectoryOnly
                )
            )
            {
                File.Delete(safetyBackup);
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            try
            {
                Directory.Delete(testRoot, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static Task ScenarioInstallerProbeTreatsLegacyDatabaseAsUpgradeCandidate()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"VehiclePermitSystemWeb-probe-test-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(testRoot);

        var databasePath = Path.Combine(testRoot, "legacy.db");
        var runtimeConfigPath = Path.Combine(testRoot, "appsettings.runtime.json");

        try
        {
            File.WriteAllText(runtimeConfigPath, "{}");

            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE UserAccounts (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        Username TEXT NOT NULL
                    );
                    INSERT INTO UserAccounts (Username) VALUES ('1023456789');
                    CREATE TABLE AdministrationSettings (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        OrganizationName TEXT NOT NULL DEFAULT ''
                    );
                    INSERT INTO AdministrationSettings (Id, OrganizationName)
                    VALUES (1, 'جهة قديمة');
                    """;
                command.ExecuteNonQuery();
            }

            var previousOut = Console.Out;
            using var writer = new StringWriter();

            try
            {
                Console.SetOut(writer);

                var handled = InstallerDataProbe.TryHandle([
                    "--probe-install-state",
                    "--database-path",
                    databasePath,
                    "--runtime-config-path",
                    runtimeConfigPath,
                ]);

                Require(handled, "installer probe should handle probe-install-state requests");
            }
            finally
            {
                Console.SetOut(previousOut);
            }

            using var json = JsonDocument.Parse(writer.ToString());
            var result = json.RootElement;

            Require(
                result.GetProperty("HasDatabase").GetBoolean(),
                "installer probe should detect the legacy database file"
            );
            Require(
                result.GetProperty("HasUsers").GetBoolean(),
                "installer probe should recognize persisted users in a legacy database"
            );
            Require(
                result.GetProperty("IsUpgradeCandidate").GetBoolean(),
                "installer probe should still classify a legacy database as an upgrade candidate"
            );
            Require(
                !result.TryGetProperty("Error", out var errorProperty)
                    || errorProperty.ValueKind == JsonValueKind.Null
                    || string.IsNullOrWhiteSpace(errorProperty.GetString()),
                "installer probe should not fail when the old database is missing IsInitialSetupCompleted"
            );
            Require(
                !result.GetProperty("IsInitialSetupCompleted").GetBoolean(),
                "legacy databases without the setup flag should default to not completed during probing"
            );

            return Task.CompletedTask;
        }
        finally
        {
            try
            {
                Directory.Delete(testRoot, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static Task ScenarioPrepareUpgradeBacksUpLegacyDatabaseAndConfig()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"VehiclePermitSystemWeb-prepare-upgrade-test-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(testRoot);

        var installDirectory = Path.Combine(testRoot, "install-root");
        var storageRoot = Path.Combine(testRoot, "storage-root");
        var dataRoot = Path.Combine(storageRoot, "data");
        var configRoot = Path.Combine(storageRoot, "config");
        var logsRoot = Path.Combine(storageRoot, "logs");
        var databasePath = Path.Combine(dataRoot, "vehicle-permit-system.db");
        var runtimeConfigPath = Path.Combine(configRoot, "appsettings.runtime.json");
        var installerLogPath = Path.Combine(logsRoot, "installer.log");
        var bootstrapLogPath = Path.Combine(logsRoot, "setup-bootstrap.log");
        var helperScriptPath = Path.Combine(testRoot, "stub-installer-common.ps1");
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
        );
        var prepareUpgradeScriptPath = Path.Combine(
            repositoryRoot,
            "packaging",
            "windows",
            "prepare-upgrade.ps1"
        );
        var probeAssemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "VehiclePermitSystemWeb.dll"
        );
        var escapedInstallerLogPath = installerLogPath.Replace("\\", "\\\\");
        var escapedStorageRoot = storageRoot.Replace("\\", "\\\\");
        var escapedDatabasePath = databasePath.Replace("\\", "\\\\");
        var escapedRuntimeConfigPath = runtimeConfigPath.Replace("\\", "\\\\");

        try
        {
            Directory.CreateDirectory(installDirectory);
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(configRoot);
            Directory.CreateDirectory(logsRoot);

            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE UserAccounts (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        Username TEXT NOT NULL
                    );
                    INSERT INTO UserAccounts (Username) VALUES ('1023456789');
                    CREATE TABLE AdministrationSettings (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        OrganizationName TEXT NOT NULL DEFAULT ''
                    );
                    INSERT INTO AdministrationSettings (Id, OrganizationName)
                    VALUES (1, 'جهة قديمة');
                    """;
                command.ExecuteNonQuery();
            }

            File.WriteAllText(
                runtimeConfigPath,
                "{" + "\"App\":{\"Urls\":\"http://127.0.0.1:5099\"}}"
            );

            File.WriteAllText(
                helperScriptPath,
                $$"""
                function Get-InstallerContext {
                    param([string]$ServiceName = "VehiclePermitSystem", [string]$TaskName = "VehiclePermitSystemStartup")

                    [pscustomobject]@{
                        ServiceName           = $ServiceName
                        TaskName              = $TaskName
                        ExecutableProcessName = "VehiclePermitSystemWeb"
                    }
                }

                function Get-InstallerLogPath {
                    param($Context)

                    return "{{escapedInstallerLogPath}}"
                }

                function Assert-Administrator {
                }

                function Write-InstallerLog {
                    param([string]$Message, [string]$Level = "INFO", [string]$LogPath)

                    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
                        $logDirectory = Split-Path -Parent $LogPath
                        if (-not [string]::IsNullOrWhiteSpace($logDirectory) -and -not (Test-Path $logDirectory)) {
                            New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
                        }

                        Add-Content -Path $LogPath -Value ("[" + $Level + "] " + $Message) -Encoding UTF8
                    }
                }

                function Get-ExistingInstallationState {
                    param($Context, [string]$InstallDirectory)

                    [pscustomobject]@{
                        HasAnySignal      = $true
                        HasPersistedState = $false
                        StorageRoot       = "{{escapedStorageRoot}}"
                        DatabasePath      = "{{escapedDatabasePath}}"
                        RuntimeConfigPath = "{{escapedRuntimeConfigPath}}"
                        Signals           = [pscustomobject]@{
                            Service       = $false
                            ScheduledTask = $false
                            InstallFolder = $true
                            Database      = $true
                            RuntimeConfig = $true
                            Registry      = $false
                        }
                    }
                }

                function Get-InstallationDataProbeResult {
                    param([string]$ProbeExecutablePath, [string]$DatabasePath, [string]$RuntimeConfigPath, [string]$LogPath)

                    $probeOutput = & dotnet $ProbeExecutablePath --probe-install-state --database-path $DatabasePath --runtime-config-path $RuntimeConfigPath
                    return $probeOutput | ConvertFrom-Json
                }

                function Stop-ServiceIfExists {
                    param([string]$ServiceName, [string]$LogPath)
                }

                function Stop-ScheduledTaskIfExists {
                    param($Context, [string]$LogPath)
                }

                function Unregister-ScheduledTaskIfExists {
                    param($Context, [string]$LogPath)
                }

                function Stop-ExecutableProcesses {
                    param($Context, [string]$LogPath)
                }

                function New-DirectoryIfMissing {
                    param([string]$Path)

                    if (-not (Test-Path $Path)) {
                        New-Item -ItemType Directory -Path $Path -Force | Out-Null
                    }
                }

                function Test-WriteAccess {
                    param([string]$Path)

                    New-DirectoryIfMissing -Path $Path
                    return $true
                }
                """
            );

            Require(
                File.Exists(prepareUpgradeScriptPath),
                "prepare-upgrade script should be available in the test output"
            );
            Require(
                File.Exists(probeAssemblyPath),
                "web application assembly should be available for install-state probing"
            );

            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                ArgumentList =
                {
                    "-ExecutionPolicy",
                    "Bypass",
                    "-NoProfile",
                    "-NonInteractive",
                    "-File",
                    prepareUpgradeScriptPath,
                    "-InstallDirectory",
                    installDirectory,
                    "-HelperScriptPath",
                    helperScriptPath,
                    "-BootstrapLogPath",
                    bootstrapLogPath,
                    "-ProbeExecutablePath",
                    probeAssemblyPath,
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process =
                Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "failed to start pwsh for prepare-upgrade test"
                );
            process.WaitForExit();

            var standardError = process.StandardError.ReadToEnd();
            var standardOutput = process.StandardOutput.ReadToEnd();

            Require(
                process.ExitCode == 0,
                $"prepare-upgrade should complete successfully for legacy data. stdout: {standardOutput} stderr: {standardError}"
            );

            var backupRoot = Path.Combine(storageRoot, "backups", "installer-upgrades");
            var backupFiles = Directory.GetFiles(backupRoot, "*", SearchOption.TopDirectoryOnly);
            Require(
                backupFiles.Any(path =>
                    path.EndsWith(".db.bak", StringComparison.OrdinalIgnoreCase)
                ),
                "prepare-upgrade should create a database backup for legacy upgrade candidates"
            );
            Require(
                backupFiles.Any(path =>
                    path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(path)
                        .StartsWith("appsettings.runtime-", StringComparison.OrdinalIgnoreCase)
                ),
                "prepare-upgrade should create a runtime-config backup for legacy upgrade candidates"
            );
            Require(
                File.ReadAllText(installerLogPath)
                    .Contains("تم أخذ نسخة احتياطية من قاعدة البيانات", StringComparison.Ordinal),
                "prepare-upgrade should log database backup creation"
            );
            Require(
                File.ReadAllText(installerLogPath)
                    .Contains("تم أخذ نسخة احتياطية من إعدادات التشغيل", StringComparison.Ordinal),
                "prepare-upgrade should log runtime-config backup creation"
            );

            return Task.CompletedTask;
        }
        finally
        {
            try
            {
                Directory.Delete(testRoot, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static BackupService CreateBackupScenarioService(string testRoot, DateTime utcNow)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Data:Provider"] = "Sqlite",
                    ["DevelopmentUseLocalData"] = "true",
                    ["Backup:Enabled"] = "true",
                    ["Backup:RetentionDays"] = "30",
                    ["ConnectionStrings:SqliteConnection"] = "Data Source=vehicle-permit-system.db",
                }
            )
            .Build();

        return new BackupService(
            configuration,
            NullLogger<BackupService>.Instance,
            new ScenarioHostEnvironment(testRoot),
            new MutableSystemClock(utcNow)
        );
    }

    private static void CreateRestoreProbeDatabase(string databasePath, string value)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE RestoreProbe (
                Id INTEGER NOT NULL PRIMARY KEY,
                Value TEXT NOT NULL
            );
            INSERT INTO RestoreProbe (Id, Value) VALUES (1, $value);
            """;
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void UpdateRestoreProbeValue(string databasePath, string value)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE RestoreProbe SET Value = $value WHERE Id = 1;";
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static string? ReadRestoreProbeValue(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM RestoreProbe WHERE Id = 1;";
        return Convert.ToString(command.ExecuteScalar());
    }

    private sealed class ScenarioHostEnvironment : IHostEnvironment
    {
        public ScenarioHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
        }

        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "PermitBehaviorChecks";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
