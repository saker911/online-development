namespace VehiclePermitSystemWeb.Utilities.Deployment
{
    public static class AppStoragePaths
    {
        private const string AppFolderName = DeploymentDefaults.ProductFolderName;
        private const string DevelopmentDataFolderName = ".localdata";
        private const string StorageRootEnvironmentVariable = "VehiclePermitSystemWeb__StorageRoot";
        private static readonly string[] LegacyAppFolderNames =
        {
            DeploymentDefaults.LegacyProductFolderName,
        };

        public static string GetAppDataRoot()
        {
            var root = GetPrimaryAppDataRoot();
            EnsurePersistentRootMigrated(root);
            Directory.CreateDirectory(root);
            return root;
        }

        public static string GetPersistentDataPath(string relativePath)
        {
            var sanitizedRelativePath = (relativePath ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            var fullPath = Path.Combine(GetAppDataRoot(), "data", sanitizedRelativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return fullPath;
        }

        public static string GetDevelopmentDataPath(string relativePath)
        {
            var sanitizedRelativePath = (relativePath ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            var storageRootOverride = GetStorageRootOverride();
            if (!string.IsNullOrWhiteSpace(storageRootOverride))
            {
                var overridePath = Path.Combine(
                    storageRootOverride,
                    DevelopmentDataFolderName,
                    sanitizedRelativePath
                );
                var overrideDirectory = Path.GetDirectoryName(overridePath);
                if (!string.IsNullOrWhiteSpace(overrideDirectory))
                {
                    Directory.CreateDirectory(overrideDirectory);
                }

                return overridePath;
            }

            var developmentRoot = ResolveDevelopmentRoot();
            var fullPath = Path.Combine(
                developmentRoot,
                DevelopmentDataFolderName,
                sanitizedRelativePath
            );
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            MigrateLegacyDevelopmentDataFile(sanitizedRelativePath, fullPath);

            return fullPath;
        }

        public static string GetDatabasePath(string fileName, bool useDevelopmentLocalData)
        {
            var sanitizedFileName = Path.GetFileName(
                string.IsNullOrWhiteSpace(fileName) ? "vehicle-permit-system.db" : fileName
            );

            return useDevelopmentLocalData
                ? GetDevelopmentDataPath(sanitizedFileName)
                : GetPersistentDataPath(sanitizedFileName);
        }

        public static string GetUploadsRoot()
        {
            var uploadsRoot = Path.Combine(GetAppDataRoot(), "uploads");
            Directory.CreateDirectory(uploadsRoot);
            return uploadsRoot;
        }

        public static string GetAdministrationUploadsRoot()
        {
            var administrationRoot = Path.Combine(GetUploadsRoot(), "administration");
            Directory.CreateDirectory(administrationRoot);
            return administrationRoot;
        }

        public static string GetBackupsRoot()
        {
            var backupsRoot = Path.Combine(GetAppDataRoot(), "backups");
            Directory.CreateDirectory(backupsRoot);
            return backupsRoot;
        }

        public static string GetConfigurationRoot()
        {
            var configurationRoot = Path.Combine(GetAppDataRoot(), "config");
            Directory.CreateDirectory(configurationRoot);
            return configurationRoot;
        }

        public static string GetRuntimeConfigurationPath(
            string fileName = "appsettings.runtime.json"
        )
        {
            return Path.Combine(GetConfigurationRoot(), fileName);
        }

        public static string ResolveUploadPhysicalPath(string relativePath)
        {
            var normalizedPath = (relativePath ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('~', '/');
            const string uploadsPrefix = "uploads/";

            if (normalizedPath.StartsWith(uploadsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = normalizedPath[uploadsPrefix.Length..];
            }

            var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length == 0
                ? GetUploadsRoot()
                : Path.Combine(new[] { GetUploadsRoot() }.Concat(segments).ToArray());
        }

        public static void MigrateLegacyStorage(
            string legacyBaseDirectory,
            string? legacyWebRootPath
        )
        {
            EnsurePersistentRootMigrated(GetPrimaryAppDataRoot());
            MigrateLegacyDatabase(legacyBaseDirectory);
            MigrateLegacyUploads(ResolveLegacyWebRootPath(legacyBaseDirectory, legacyWebRootPath));
        }

        private static string GetPrimaryAppDataRoot()
        {
            var storageRootOverride = GetStorageRootOverride();
            if (!string.IsNullOrWhiteSpace(storageRootOverride))
            {
                return storageRootOverride;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                AppFolderName
            );
        }

        private static string? GetStorageRootOverride()
        {
            var configuredRoot = Environment.GetEnvironmentVariable(StorageRootEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configuredRoot)
                ? null
                : Path.GetFullPath(configuredRoot);
        }

        private static IEnumerable<string> GetLegacyAppDataRoots()
        {
            var commonAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData
            );

            foreach (var legacyFolderName in LegacyAppFolderNames)
            {
                if (
                    string.IsNullOrWhiteSpace(legacyFolderName)
                    || string.Equals(legacyFolderName, AppFolderName, StringComparison.Ordinal)
                )
                {
                    continue;
                }

                yield return Path.Combine(commonAppData, legacyFolderName);
            }
        }

        private static void EnsurePersistentRootMigrated(string destinationRoot)
        {
            if (Directory.Exists(destinationRoot))
            {
                return;
            }

            var legacyRoot = GetLegacyAppDataRoots().FirstOrDefault(Directory.Exists);
            if (string.IsNullOrWhiteSpace(legacyRoot))
            {
                return;
            }

            CopyDirectoryContents(legacyRoot, destinationRoot);
        }

        private static void MigrateLegacyDatabase(string legacyBaseDirectory)
        {
            var legacyDatabasePath = Path.Combine(legacyBaseDirectory, "vehicle-permit-system.db");
            var persistentDatabasePath = GetPersistentDataPath("vehicle-permit-system.db");

            if (!File.Exists(persistentDatabasePath) && File.Exists(legacyDatabasePath))
            {
                File.Copy(legacyDatabasePath, persistentDatabasePath, overwrite: false);
            }
        }

        private static string? ResolveLegacyWebRootPath(
            string legacyBaseDirectory,
            string? legacyWebRootPath
        )
        {
            if (!string.IsNullOrWhiteSpace(legacyWebRootPath))
            {
                return legacyWebRootPath;
            }

            if (string.IsNullOrWhiteSpace(legacyBaseDirectory))
            {
                return null;
            }

            var inferredWebRootPath = Path.Combine(legacyBaseDirectory, "wwwroot");
            return Directory.Exists(inferredWebRootPath) ? inferredWebRootPath : null;
        }

        private static void MigrateLegacyUploads(string? legacyWebRootPath)
        {
            if (string.IsNullOrWhiteSpace(legacyWebRootPath))
            {
                return;
            }

            var legacyUploadsRoot = Path.Combine(legacyWebRootPath, "uploads");
            var persistentUploadsRoot = GetUploadsRoot();

            if (!Directory.Exists(legacyUploadsRoot))
            {
                return;
            }

            CopyDirectoryContents(legacyUploadsRoot, persistentUploadsRoot);
        }

        private static void CopyDirectoryContents(
            string sourceDirectory,
            string destinationDirectory
        )
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (
                var directory in Directory.GetDirectories(
                    sourceDirectory,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                var relativeDirectory = Path.GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relativeDirectory));
            }

            foreach (
                var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            )
            {
                var relativeFilePath = Path.GetRelativePath(sourceDirectory, file);
                var destinationFilePath = Path.Combine(destinationDirectory, relativeFilePath);
                var destinationFileDirectory = Path.GetDirectoryName(destinationFilePath);

                if (!string.IsNullOrWhiteSpace(destinationFileDirectory))
                {
                    Directory.CreateDirectory(destinationFileDirectory);
                }

                if (!File.Exists(destinationFilePath))
                {
                    File.Copy(file, destinationFilePath, overwrite: false);
                }
            }
        }

        private static string ResolveDevelopmentRoot()
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            if (LooksLikeProjectRoot(currentDirectory))
            {
                return currentDirectory;
            }

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (LooksLikeProjectRoot(directory.FullName))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return currentDirectory;
        }

        private static bool LooksLikeProjectRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            return Directory.EnumerateFiles(path, "*.csproj", SearchOption.TopDirectoryOnly).Any()
                || File.Exists(Path.Combine(path, "appsettings.json"));
        }

        private static void MigrateLegacyDevelopmentDataFile(
            string relativePath,
            string destinationPath
        )
        {
            if (File.Exists(destinationPath))
            {
                return;
            }

            var legacyPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "data", relativePath),
                Path.Combine(ResolveDevelopmentRoot(), "data", relativePath),
            };

            var legacyPath = legacyPaths.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(legacyPath))
            {
                return;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(legacyPath, destinationPath, overwrite: false);
        }
    }
}
