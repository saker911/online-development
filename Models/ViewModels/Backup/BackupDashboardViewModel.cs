namespace VehiclePermitSystemWeb.Models.ViewModels.Backup
{
    public class BackupDashboardViewModel
    {
        public bool BackupEnabled { get; set; }
        public int RetentionDays { get; set; }
        public bool BackupSupported { get; set; }
        public string BackupRootPath { get; set; } = string.Empty;
        public string DatabasePath { get; set; } = string.Empty;
        public DateTime? LastAutomaticBackupAtUtc { get; set; }
        public List<BackupFileInfo> Backups { get; set; } = new();
    }
}
