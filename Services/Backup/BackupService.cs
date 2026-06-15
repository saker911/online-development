using System.Data.Common;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
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

namespace VehiclePermitSystemWeb.Services.Backup
{
    public class BackupService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<BackupService> _logger;
        private readonly IHostEnvironment _environment;
        private readonly ISystemClock _systemClock;
        private readonly SemaphoreSlim _backupLock = new(1, 1);

        public BackupService(
            IConfiguration configuration,
            ILogger<BackupService> logger,
            IHostEnvironment environment,
            ISystemClock systemClock
        )
        {
            _configuration = configuration;
            _logger = logger;
            _environment = environment;
            _systemClock = systemClock;
        }

        public bool IsEnabled =>
            !string.Equals(
                _configuration["Backup:Enabled"],
                "false",
                StringComparison.OrdinalIgnoreCase
            );

        public bool IsSupported =>
            string.Equals(
                _configuration["Data:Provider"] ?? "Sqlite",
                "Sqlite",
                StringComparison.OrdinalIgnoreCase
            );

        public int RetentionDays
        {
            get
            {
                return
                    int.TryParse(_configuration["Backup:RetentionDays"], out var value) && value > 0
                    ? value
                    : 30;
            }
        }

        public string BackupRootPath => AppStoragePaths.GetBackupsRoot();

        public string DatabasePath => ResolveDatabasePath();

        public IReadOnlyList<BackupFileInfo> GetBackups()
        {
            if (!Directory.Exists(BackupRootPath))
            {
                return Array.Empty<BackupFileInfo>();
            }

            return Directory
                .GetFiles(BackupRootPath, "*.zip", SearchOption.TopDirectoryOnly)
                .Select(filePath => new BackupFileInfo
                {
                    FileName = Path.GetFileName(filePath),
                    CreatedAtUtc = File.GetCreationTimeUtc(filePath),
                    SizeBytes = new FileInfo(filePath).Length,
                })
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToList();
        }

        public async Task<BackupFileInfo?> CreateBackupAsync(
            string? triggerName = null,
            CancellationToken cancellationToken = default
        )
        {
            if (!IsEnabled || !IsSupported)
            {
                return null;
            }

            await _backupLock.WaitAsync(cancellationToken);
            try
            {
                var createdAtUtc = _systemClock.UtcNow;
                var fileName =
                    $"VehiclePermitSystemWeb-{createdAtUtc:yyyyMMdd-HHmmss}-{SanitizeTriggerName(triggerName)}.zip";
                var backupPath = Path.Combine(BackupRootPath, fileName);
                var stagingRoot = Path.Combine(
                    Path.GetTempPath(),
                    $"VehiclePermitSystemWeb-backup-{Guid.NewGuid():N}"
                );
                Directory.CreateDirectory(stagingRoot);

                var databasePath = DatabasePath;
                if (File.Exists(databasePath))
                {
                    CopySqliteDatabaseSnapshot(
                        databasePath,
                        Path.Combine(stagingRoot, Path.GetFileName(databasePath))
                    );
                }

                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                await Task.Run(
                    () =>
                        ZipFile.CreateFromDirectory(
                            stagingRoot,
                            backupPath,
                            CompressionLevel.Optimal,
                            includeBaseDirectory: false
                        ),
                    cancellationToken
                );

                CleanupDirectory(stagingRoot);
                CleanupOldBackups();

                var createdInfo = new FileInfo(backupPath);
                _logger.LogInformation("Created backup {BackupFile}", backupPath);

                return new BackupFileInfo
                {
                    FileName = createdInfo.Name,
                    CreatedAtUtc = createdInfo.CreationTimeUtc,
                    SizeBytes = createdInfo.Length,
                };
            }
            finally
            {
                _backupLock.Release();
            }
        }

        public async Task<BackupFileInfo?> EnsureDailyBackupAsync(
            CancellationToken cancellationToken = default
        )
        {
            if (!IsEnabled || !IsSupported)
            {
                return null;
            }

            var todayUtc = _systemClock.UtcNow.Date;
            if (GetBackups().Any(item => item.CreatedAtUtc.Date == todayUtc))
            {
                return null;
            }

            return await CreateBackupAsync("auto", cancellationToken);
        }

        public async Task RestoreBackupAsync(
            IFormFile backupFile,
            CancellationToken cancellationToken = default
        )
        {
            if (!IsEnabled || !IsSupported)
            {
                throw new InvalidOperationException(
                    "Backup is not enabled for the current data provider."
                );
            }

            if (backupFile.Length == 0)
            {
                throw new InvalidOperationException("Backup file is empty.");
            }

            var restoreSafetyBackup = await CreateBackupAsync("restore-pre", cancellationToken);
            if (restoreSafetyBackup != null)
            {
                _logger.LogInformation(
                    "Created safety backup {BackupFile} before restore",
                    restoreSafetyBackup.FileName
                );
            }

            await _backupLock.WaitAsync(cancellationToken);
            string? rollbackDatabasePath = null;
            string? restoreDatabasePath = null;
            string? workingRoot = null;
            try
            {
                workingRoot = Path.Combine(
                    Path.GetTempPath(),
                    $"VehiclePermitSystemWeb-restore-{Guid.NewGuid():N}"
                );
                var uploadPath = Path.Combine(workingRoot, Path.GetFileName(backupFile.FileName));
                Directory.CreateDirectory(workingRoot);

                await using (var fileStream = System.IO.File.Create(uploadPath))
                {
                    await backupFile.CopyToAsync(fileStream, cancellationToken);
                }

                var extractRoot = Path.Combine(workingRoot, "extract");
                Directory.CreateDirectory(extractRoot);
                ExtractBackupArchive(uploadPath, extractRoot);

                var extractedDatabasePath = FindExtractedDatabasePath(extractRoot);
                if (
                    string.IsNullOrWhiteSpace(extractedDatabasePath)
                    || !File.Exists(extractedDatabasePath)
                )
                {
                    throw new InvalidOperationException(
                        "The backup archive does not contain a database file."
                    );
                }

                ValidateSqliteDatabase(extractedDatabasePath, "The backup database is not valid.");
                SqliteConnection.ClearAllPools();

                var databasePath = DatabasePath;
                restoreDatabasePath = databasePath;
                var databaseDirectory = Path.GetDirectoryName(databasePath);
                if (!string.IsNullOrWhiteSpace(databaseDirectory))
                {
                    Directory.CreateDirectory(databaseDirectory);
                }

                if (File.Exists(databasePath))
                {
                    rollbackDatabasePath = Path.Combine(
                        workingRoot,
                        "current-database.rollback.db"
                    );
                    File.Copy(databasePath, rollbackDatabasePath, overwrite: true);
                }

                File.Copy(extractedDatabasePath, databasePath, overwrite: true);
                ValidateSqliteDatabase(
                    databasePath,
                    "The restored database could not be verified."
                );

                _logger.LogInformation("Restored backup from {BackupFile}", backupFile.FileName);
            }
            catch
            {
                TryRollbackDatabase(rollbackDatabasePath, restoreDatabasePath);
                throw;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(workingRoot))
                {
                    CleanupDirectory(workingRoot);
                }

                _backupLock.Release();
            }
        }

        public string? GetLatestAutomaticBackupText()
        {
            var latestAutoBackup = GetBackups()
                .Where(item => item.FileName.Contains("-auto", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            return latestAutoBackup == null
                ? null
                : VehiclePermitSystemWeb.Utilities.Dates.DateHelper.ToGregorianDateTime12(
                    latestAutoBackup.CreatedAtUtc
                );
        }

        private string ResolveDatabasePath()
        {
            var connectionString =
                _configuration.GetConnectionString("SqliteConnection")
                ?? "Data Source=vehicle-permit-system.db";
            var developmentUseLocalData = _configuration.GetValue<bool>("DevelopmentUseLocalData");

            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var databaseFileName = "vehicle-permit-system.db";
            if (
                builder.TryGetValue("Data Source", out var dataSourceValue)
                && dataSourceValue is string dataSource
                && !string.IsNullOrWhiteSpace(dataSource)
            )
            {
                databaseFileName = Path.GetFileName(dataSource);
            }

            return AppStoragePaths.GetDatabasePath(
                databaseFileName,
                _environment.IsDevelopment() && developmentUseLocalData
            );
        }

        private static string SanitizeTriggerName(string? triggerName)
        {
            var normalized = string.IsNullOrWhiteSpace(triggerName) ? "manual" : triggerName.Trim();
            var cleaned = new string(normalized.Where(char.IsLetterOrDigit).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "manual" : cleaned.ToLowerInvariant();
        }

        private void CleanupOldBackups()
        {
            var retentionCutoff = _systemClock.UtcNow.AddDays(-RetentionDays);
            foreach (var backupFile in GetBackups())
            {
                if (backupFile.CreatedAtUtc < retentionCutoff)
                {
                    var fullPath = Path.Combine(BackupRootPath, backupFile.FileName);
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }
                }
            }
        }

        private static string? FindExtractedDatabasePath(string extractRoot)
        {
            var preferredPath = Path.Combine(extractRoot, "data", "vehicle-permit-system.db");
            if (File.Exists(preferredPath))
            {
                return preferredPath;
            }

            return Directory
                .GetFiles(extractRoot, "*.db", SearchOption.AllDirectories)
                .FirstOrDefault();
        }

        private static void CopySqliteDatabaseSnapshot(string sourcePath, string destinationPath)
        {
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            var sourceConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString();
            var destinationConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            using (var sourceConnection = new SqliteConnection(sourceConnectionString))
            using (var destinationConnection = new SqliteConnection(destinationConnectionString))
            {
                sourceConnection.Open();
                destinationConnection.Open();
                sourceConnection.BackupDatabase(destinationConnection);
            }

            SqliteConnection.ClearAllPools();

            ValidateSqliteDatabase(
                destinationPath,
                "The created backup database could not be verified."
            );
        }

        private static void ExtractBackupArchive(string archivePath, string extractRoot)
        {
            var normalizedExtractRoot = Path.GetFullPath(extractRoot);
            if (!normalizedExtractRoot.EndsWith(Path.DirectorySeparatorChar))
            {
                normalizedExtractRoot += Path.DirectorySeparatorChar;
            }

            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count == 0)
            {
                throw new InvalidOperationException("The backup archive is empty.");
            }

            foreach (var entry in archive.Entries)
            {
                var destinationPath = Path.GetFullPath(Path.Combine(extractRoot, entry.FullName));
                if (
                    !destinationPath.StartsWith(
                        normalizedExtractRoot,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidOperationException(
                        "The backup archive contains an unsafe file path."
                    );
                }

                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }

        private static void ValidateSqliteDatabase(string databasePath, string failureMessage)
        {
            if (!File.Exists(databasePath) || new FileInfo(databasePath).Length == 0)
            {
                throw new InvalidOperationException(failureMessage);
            }

            try
            {
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadOnly,
                }.ToString();

                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA quick_check;";
                var result = Convert.ToString(command.ExecuteScalar());
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(failureMessage);
                }
            }
            catch (SqliteException ex)
            {
                throw new InvalidOperationException(failureMessage, ex);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }
        }

        private static void TryRollbackDatabase(string? rollbackDatabasePath, string? databasePath)
        {
            if (
                string.IsNullOrWhiteSpace(rollbackDatabasePath)
                || string.IsNullOrWhiteSpace(databasePath)
                || !File.Exists(rollbackDatabasePath)
            )
            {
                return;
            }

            try
            {
                File.Copy(rollbackDatabasePath, databasePath, overwrite: true);
            }
            catch
            {
                // Best-effort rollback. The original exception is more useful to callers.
            }
        }

        private static void CleanupDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            try
            {
                Directory.Delete(directoryPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
