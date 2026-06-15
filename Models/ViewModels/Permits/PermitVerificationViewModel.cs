namespace VehiclePermitSystemWeb.Models.ViewModels.Permits
{
    public class PermitVerificationViewModel
    {
        public bool IsValid { get; set; }
        public bool IsAuthorized { get; set; }
        public bool IsExpired { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string BadgeClass { get; set; } = "bg-secondary";
        public string StatusText { get; set; } = string.Empty;
        public Permit? Permit { get; set; }
    }
}
