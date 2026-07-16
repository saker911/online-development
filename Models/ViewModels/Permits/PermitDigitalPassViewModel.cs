namespace VehiclePermitSystemWeb.Models.ViewModels.Permits
{
    public sealed class PermitDigitalPassViewModel
    {
        public bool IsValid { get; set; }
        public bool IsAuthorized { get; set; }
        public bool IsExpired { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string HolderName { get; set; } = string.Empty;
        public string PermitNumber { get; set; } = string.Empty;
        public string PermitTypeDisplay { get; set; } = string.Empty;
        public string LocationDisplay { get; set; } = string.Empty;
        public string PlateNumberDisplay { get; set; } = string.Empty;
        public string QrImageUrl { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
    }
}
