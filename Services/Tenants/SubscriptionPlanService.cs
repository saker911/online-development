using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Platform;
using VehiclePermitSystemWeb.Models.ViewModels.Tenants;

namespace VehiclePermitSystemWeb.Services.Tenants
{
    public interface ISubscriptionPlanService
    {
        IReadOnlyList<TenantPlanViewModel> GetPublicPlans();
        TenantPlanViewModel? Find(string? code);
        SubscriptionPricingViewModel GetEditor();
        void Update(SubscriptionPricingViewModel model);
    }

    public sealed class SubscriptionPlanService : ISubscriptionPlanService
    {
        private static readonly object DefaultPlanSeedLock = new();
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public SubscriptionPlanService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory
        )
        {
            _dbContextFactory = dbContextFactory;
        }

        public IReadOnlyList<TenantPlanViewModel> GetPublicPlans()
        {
            using var db = _dbContextFactory.CreateDbContext();
            EnsureDefaults(db);
            var now = DateTime.UtcNow;
            return db
                .SubscriptionPlans.AsNoTracking()
                .Where(plan => plan.IsActive)
                .OrderBy(plan => plan.SortOrder)
                .ThenBy(plan => plan.DurationMonths)
                .AsEnumerable()
                .Select(plan => MapPublicPlan(plan, now))
                .ToList();
        }

        public TenantPlanViewModel? Find(string? code)
        {
            var normalizedCode = TenantPlanCatalog.NormalizeCode(code);
            using var db = _dbContextFactory.CreateDbContext();
            EnsureDefaults(db);
            var plan = db
                .SubscriptionPlans.AsNoTracking()
                .FirstOrDefault(item => item.Code == normalizedCode && item.IsActive);
            return plan == null ? null : MapPublicPlan(plan, DateTime.UtcNow);
        }

        public SubscriptionPricingViewModel GetEditor()
        {
            using var db = _dbContextFactory.CreateDbContext();
            EnsureDefaults(db);
            var plans = db
                .SubscriptionPlans.AsNoTracking()
                .OrderBy(plan => plan.SortOrder)
                .ThenBy(plan => plan.DurationMonths)
                .ToList();
            return new SubscriptionPricingViewModel
            {
                FeaturedPlanCode = plans.FirstOrDefault(plan => plan.IsFeatured)?.Code
                    ?? string.Empty,
                Plans = plans.Select(MapEditorPlan).ToList(),
            };
        }

        public void Update(SubscriptionPricingViewModel model)
        {
            using var db = _dbContextFactory.CreateDbContext();
            EnsureDefaults(db);
            var postedPlans = model
                .Plans.Where(plan => !string.IsNullOrWhiteSpace(plan.Code))
                .GroupBy(
                    plan => TenantPlanCatalog.NormalizeCode(plan.Code),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;

            foreach (var plan in db.SubscriptionPlans)
            {
                if (!postedPlans.TryGetValue(plan.Code, out var posted))
                {
                    continue;
                }

                plan.Name = Normalize(posted.Name);
                plan.Summary = Normalize(posted.Summary);
                plan.DurationMonths = posted.DurationMonths;
                plan.Price = posted.Price;
                plan.OriginalPrice = posted.OriginalPrice;
                plan.OfferLabel = Normalize(posted.OfferLabel);
                plan.OfferStartsAtUtc = ToUtc(posted.OfferStartsAtUtc);
                plan.OfferEndsAtUtc = ToUtc(posted.OfferEndsAtUtc);
                plan.IsFeatured = string.Equals(
                    plan.Code,
                    TenantPlanCatalog.NormalizeCode(model.FeaturedPlanCode),
                    StringComparison.OrdinalIgnoreCase
                );
                plan.IsActive = posted.IsActive;
                plan.FeaturesJson = JsonSerializer.Serialize(ParseFeatures(posted.FeaturesText));
                plan.UpdatedAtUtc = now;
            }

            db.SaveChanges();
        }

        private static TenantPlanViewModel MapPublicPlan(SubscriptionPlan plan, DateTime now)
        {
            var offerIsActive =
                (!plan.OfferStartsAtUtc.HasValue || plan.OfferStartsAtUtc.Value <= now)
                && (!plan.OfferEndsAtUtc.HasValue || plan.OfferEndsAtUtc.Value >= now);
            return new TenantPlanViewModel
            {
                Code = plan.Code,
                Name = plan.Name,
                Summary = plan.Summary,
                DurationMonths = plan.DurationMonths,
                TotalPrice = plan.Price,
                OriginalPrice = offerIsActive ? plan.OriginalPrice : null,
                OfferLabel = offerIsActive ? plan.OfferLabel : string.Empty,
                IsFeatured = plan.IsFeatured,
                Features = ParseFeatures(plan.FeaturesJson, true),
            };
        }

        private static SubscriptionPlanEditorViewModel MapEditorPlan(SubscriptionPlan plan) =>
            new()
            {
                Code = plan.Code,
                Name = plan.Name,
                Summary = plan.Summary,
                DurationMonths = plan.DurationMonths,
                Price = plan.Price,
                OriginalPrice = plan.OriginalPrice,
                OfferLabel = plan.OfferLabel,
                OfferStartsAtUtc = ToLocal(plan.OfferStartsAtUtc),
                OfferEndsAtUtc = ToLocal(plan.OfferEndsAtUtc),
                IsActive = plan.IsActive,
                FeaturesText = string.Join(Environment.NewLine, ParseFeatures(plan.FeaturesJson, true)),
            };

        private static IReadOnlyList<string> ParseFeatures(string? value, bool isJson = false)
        {
            if (isJson)
            {
                try
                {
                    return JsonSerializer.Deserialize<List<string>>(value ?? "[]")
                            ?.Select(Normalize)
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(12)
                            .ToList()
                        ?? [];
                }
                catch (JsonException)
                {
                    return [];
                }
            }

            return (value ?? string.Empty)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        }

        private static void EnsureDefaults(ApplicationDbContext db)
        {
            if (db.SubscriptionPlans.Any())
            {
                return;
            }

            lock (DefaultPlanSeedLock)
            {
                if (db.SubscriptionPlans.Any())
                {
                    return;
                }

                var now = DateTime.UtcNow;
                var sortOrder = 0;
                foreach (var defaultPlan in TenantPlanCatalog.GetPlans())
                {
                    db.SubscriptionPlans.Add(
                        new SubscriptionPlan
                        {
                            Code = defaultPlan.Code,
                            Name = defaultPlan.Name,
                            Summary = defaultPlan.Summary,
                            DurationMonths = defaultPlan.DurationMonths,
                            Price = defaultPlan.TotalPrice,
                            IsFeatured = defaultPlan.IsFeatured,
                            IsActive = true,
                            SortOrder = sortOrder++,
                            FeaturesJson = JsonSerializer.Serialize(defaultPlan.Features),
                            UpdatedAtUtc = now,
                        }
                    );
                }

                db.SaveChanges();
            }
        }

        private static DateTime? ToUtc(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            return value.Value.Kind switch
            {
                DateTimeKind.Utc => value.Value,
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                _ => TimeZoneInfo.ConvertTimeToUtc(value.Value, TimeZoneInfo.Local),
            };
        }

        private static DateTime? ToLocal(DateTime? value) =>
            value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToLocalTime() : null;

        private static string Normalize(string? value) => (value ?? string.Empty).Trim();
    }
}
