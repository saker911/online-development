using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Platform
{
    public sealed class PlatformSettingsViewModel
    {
        public int Id { get; set; } = 1;

        [StringLength(256)]
        public string ProviderName { get; set; } = string.Empty;

        [StringLength(64)]
        public string CommercialRegistration { get; set; } = string.Empty;

        [StringLength(64)]
        public string VatNumber { get; set; } = string.Empty;

        [StringLength(512)]
        public string RegisteredAddress { get; set; } = string.Empty;

        [StringLength(128)]
        public string City { get; set; } = string.Empty;

        [StringLength(128)]
        public string Country { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "أدخل بريدًا إلكترونيًا صحيحًا.")]
        [StringLength(256)]
        public string OfficialEmail { get; set; } = string.Empty;

        [StringLength(32)]
        public string SupportPhone { get; set; } = string.Empty;

        [StringLength(32)]
        public string WhatsAppNumber { get; set; } = string.Empty;

        [StringLength(256)]
        public string WorkingHours { get; set; } = string.Empty;

        [StringLength(256)]
        public string DataHostingLocation { get; set; } = string.Empty;

        [StringLength(1000)]
        public string BackupPolicy { get; set; } = string.Empty;

        public DateTime? UpdatedAtUtc { get; set; }

        public bool HasPublishedData =>
            !string.IsNullOrWhiteSpace(ProviderName)
            || !string.IsNullOrWhiteSpace(CommercialRegistration)
            || !string.IsNullOrWhiteSpace(VatNumber)
            || !string.IsNullOrWhiteSpace(RegisteredAddress)
            || !string.IsNullOrWhiteSpace(City)
            || !string.IsNullOrWhiteSpace(Country)
            || !string.IsNullOrWhiteSpace(OfficialEmail)
            || !string.IsNullOrWhiteSpace(SupportPhone)
            || !string.IsNullOrWhiteSpace(WhatsAppNumber)
            || !string.IsNullOrWhiteSpace(WorkingHours)
            || !string.IsNullOrWhiteSpace(DataHostingLocation)
            || !string.IsNullOrWhiteSpace(BackupPolicy);
    }
}
