namespace VehiclePermitSystemWeb.Models.ViewModels.Backup
{
    public class BackupFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public long SizeBytes { get; set; }

        public string CreatedAtLocalText =>
            VehiclePermitSystemWeb.Utilities.Dates.DateHelper.ToGregorianDateTime12(CreatedAtUtc);

        public string SizeText
        {
            get
            {
                if (SizeBytes < 1024)
                {
                    return $"{SizeBytes} B";
                }

                var size = (double)SizeBytes;
                var units = new[] { "KB", "MB", "GB", "TB" };
                var unitIndex = 0;

                while (size >= 1024 && unitIndex < units.Length - 1)
                {
                    size /= 1024;
                    unitIndex++;
                }

                return $"{size:0.##} {units[unitIndex]}";
            }
        }
    }
}
