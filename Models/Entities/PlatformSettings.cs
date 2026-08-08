namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class PlatformSettings
    {
        public int Id { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public string CommercialRegistration { get; set; } = string.Empty;
        public string VatNumber { get; set; } = string.Empty;
        public string RegisteredAddress { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string OfficialEmail { get; set; } = string.Empty;
        public string SupportPhone { get; set; } = string.Empty;
        public string WhatsAppNumber { get; set; } = string.Empty;
        public string WorkingHours { get; set; } = string.Empty;
        public string DataHostingLocation { get; set; } = string.Empty;
        public string BackupPolicy { get; set; } = string.Empty;
        public DateTime? UpdatedAtUtc { get; set; }
    }
}
