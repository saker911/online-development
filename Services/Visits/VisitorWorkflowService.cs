using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;

namespace VehiclePermitSystemWeb.Services.Visits
{
    public interface IVisitorWorkflowService
    {
        VisitorWorkflowSettingsViewModel GetSettings();
        void Update(VisitorWorkflowSettingsViewModel model);
    }

    public sealed class VisitorWorkflowService : IVisitorWorkflowService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public VisitorWorkflowService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public VisitorWorkflowSettingsViewModel GetSettings()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var settings = db.VisitorWorkflowSettings.AsNoTracking().SingleOrDefault();
            return settings == null ? VisitorWorkflowSettingsViewModel.CreateDefault() : Map(settings);
        }

        public void Update(VisitorWorkflowSettingsViewModel model)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var settings = db.VisitorWorkflowSettings.SingleOrDefault();
            if (settings == null)
            {
                settings = new VisitorWorkflowSettings();
                db.VisitorWorkflowSettings.Add(settings);
            }

            settings.TemplateKey = VisitorWorkflowTemplates.Normalize(model.TemplateKey);
            settings.IsEnabled = model.IsEnabled;
            settings.ShowNationalId = model.ShowNationalId;
            settings.RequireNationalId = model.ShowNationalId && model.RequireNationalId;
            settings.ShowHostName = model.ShowHostName;
            settings.RequireHostName = model.ShowHostName && model.RequireHostName;
            settings.ShowVisitLocation = model.ShowVisitLocation;
            settings.RequireVisitLocation = model.ShowVisitLocation && model.RequireVisitLocation;
            settings.ShowPurpose = model.ShowPurpose;
            settings.RequirePurpose = model.ShowPurpose && model.RequirePurpose;
            settings.MinimumLeadMinutes = Math.Clamp(model.MinimumLeadMinutes, 0, 1440);
            settings.MaximumAdvanceDays = Math.Clamp(model.MaximumAdvanceDays, 1, 90);
            settings.WelcomeMessage = Normalize(model.WelcomeMessage);
            settings.UpdatedAtUtc = DateTime.UtcNow;
            db.SaveChanges();
        }

        private static VisitorWorkflowSettingsViewModel Map(VisitorWorkflowSettings settings) =>
            new()
            {
                IsEnabled = settings.IsEnabled,
                TemplateKey = VisitorWorkflowTemplates.Normalize(settings.TemplateKey),
                ShowNationalId = settings.ShowNationalId,
                RequireNationalId = settings.RequireNationalId,
                ShowHostName = settings.ShowHostName,
                RequireHostName = settings.RequireHostName,
                ShowVisitLocation = settings.ShowVisitLocation,
                RequireVisitLocation = settings.RequireVisitLocation,
                ShowPurpose = settings.ShowPurpose,
                RequirePurpose = settings.RequirePurpose,
                MinimumLeadMinutes = settings.MinimumLeadMinutes,
                MaximumAdvanceDays = settings.MaximumAdvanceDays,
                WelcomeMessage = Normalize(settings.WelcomeMessage),
            };

        private static string Normalize(string? value) => (value ?? string.Empty).Trim();
    }
}
