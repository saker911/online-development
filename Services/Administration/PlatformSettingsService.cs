using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Platform;

namespace VehiclePermitSystemWeb.Services.Administration
{
    public interface IPlatformSettingsService
    {
        PlatformSettingsViewModel Get();
        void Update(PlatformSettingsViewModel model);
    }

    public sealed class PlatformSettingsService : IPlatformSettingsService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;

        public PlatformSettingsService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IConfiguration configuration
        )
        {
            _dbContextFactory = dbContextFactory;
            _configuration = configuration;
        }

        public PlatformSettingsViewModel Get()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var settings = db.PlatformSettings.AsNoTracking().FirstOrDefault(item => item.Id == 1);
            return settings == null ? BuildConfigurationFallback() : Map(settings);
        }

        public void Update(PlatformSettingsViewModel model)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var settings = db.PlatformSettings.FirstOrDefault(item => item.Id == 1);
            if (settings == null)
            {
                settings = new PlatformSettings { Id = 1 };
                db.PlatformSettings.Add(settings);
            }

            settings.ProviderName = Normalize(model.ProviderName);
            settings.CommercialRegistration = Normalize(model.CommercialRegistration);
            settings.VatNumber = Normalize(model.VatNumber);
            settings.RegisteredAddress = Normalize(model.RegisteredAddress);
            settings.City = Normalize(model.City);
            settings.Country = Normalize(model.Country);
            settings.OfficialEmail = Normalize(model.OfficialEmail).ToLowerInvariant();
            settings.SupportPhone = Normalize(model.SupportPhone);
            settings.WhatsAppNumber = Normalize(model.WhatsAppNumber);
            settings.WorkingHours = Normalize(model.WorkingHours);
            settings.DataHostingLocation = Normalize(model.DataHostingLocation);
            settings.BackupPolicy = Normalize(model.BackupPolicy);
            settings.UpdatedAtUtc = DateTime.UtcNow;
            db.SaveChanges();
        }

        private PlatformSettingsViewModel BuildConfigurationFallback()
        {
            return new PlatformSettingsViewModel
            {
                ProviderName = Normalize(_configuration["Legal:ProviderName"]),
                CommercialRegistration = Normalize(
                    _configuration["Legal:CommercialRegistration"]
                ),
                VatNumber = Normalize(_configuration["Legal:VatNumber"]),
                RegisteredAddress = Normalize(_configuration["Legal:Address"]),
                OfficialEmail = Normalize(_configuration["Legal:SupportEmail"]).ToLowerInvariant(),
                SupportPhone = Normalize(_configuration["Legal:SupportPhone"]),
                WhatsAppNumber = Normalize(_configuration["Legal:WhatsAppNumber"]),
                WorkingHours = Normalize(_configuration["Legal:WorkingHours"]),
                DataHostingLocation = Normalize(_configuration["Legal:DataHostingLocation"]),
                BackupPolicy = Normalize(_configuration["Legal:BackupPolicy"]),
            };
        }

        private static PlatformSettingsViewModel Map(PlatformSettings settings)
        {
            return new PlatformSettingsViewModel
            {
                Id = settings.Id,
                ProviderName = settings.ProviderName,
                CommercialRegistration = settings.CommercialRegistration,
                VatNumber = settings.VatNumber,
                RegisteredAddress = settings.RegisteredAddress,
                City = settings.City,
                Country = settings.Country,
                OfficialEmail = settings.OfficialEmail,
                SupportPhone = settings.SupportPhone,
                WhatsAppNumber = settings.WhatsAppNumber,
                WorkingHours = settings.WorkingHours,
                DataHostingLocation = settings.DataHostingLocation,
                BackupPolicy = settings.BackupPolicy,
                UpdatedAtUtc = settings.UpdatedAtUtc,
            };
        }

        private static string Normalize(string? value) => (value ?? string.Empty).Trim();
    }
}
