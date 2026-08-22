using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Tenants;
using VehiclePermitSystemWeb.Services.Tenants;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Administration)]
public sealed class SubscriptionPricingTests
{
    [Fact]
    public void PricingServicePublishesManagedPricesAndOffers()
    {
        using var provider = BuildProvider("pricing-management");
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var service = new SubscriptionPlanService(factory);

        var editor = service.GetEditor();
        var monthly = editor.Plans.Single(plan => plan.Code == "monthly");
        monthly.Price = 39;
        monthly.OriginalPrice = 49;
        monthly.OfferLabel = "عرض الإطلاق";
        editor.FeaturedPlanCode = monthly.Code;

        service.Update(editor);

        var published = service.Find("monthly");
        Assert.NotNull(published);
        Assert.Equal(39, published.TotalPrice);
        Assert.Equal(49, published.OriginalPrice);
        Assert.Equal("عرض الإطلاق", published.BadgeText);
        Assert.True(published.IsFeatured);
    }

    [Fact]
    public void SignupKeepsItsPriceAndDurationSnapshotWhenCatalogChanges()
    {
        using var provider = BuildProvider("pricing-snapshot");
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var pricing = new SubscriptionPlanService(factory);
        var tenantService = new TenantManagementService(
            factory,
            new EphemeralDataProtectionProvider(),
            pricing
        );
        var signup = tenantService.CreateSignup(
            new TenantSignupViewModel
            {
                PlanCode = "annual",
                CompanyName = "جهة اختبار السعر",
                TenantId = "pricing-snapshot",
                OwnerFullName = "مالك اختبار السعر",
                OwnerUsername = "pricing.owner",
                OwnerPhoneNumber = "0501234567",
                OwnerEmail = "pricing@example.com",
                Password = "StrongSignup1!",
                ConfirmPassword = "StrongSignup1!",
                AcceptPolicy = true,
            }
        );
        Assert.True(signup.Succeeded, signup.Message);

        var editor = pricing.GetEditor();
        var annual = editor.Plans.Single(plan => plan.Code == "annual");
        annual.Price = 299;
        annual.DurationMonths = 10;
        pricing.Update(editor);

        var checkout = tenantService.GetCheckout(signup.TenantId, signup.CheckoutToken);
        Assert.NotNull(checkout);
        Assert.Equal(449, checkout.TotalPrice);
        Assert.Equal(12, checkout.DurationMonths);

        var activatedAt = DateTime.UtcNow;
        Assert.True(tenantService.ActivatePaidSubscription(signup.TenantId).Succeeded);
        using var db = factory.CreateDbContext();
        var tenant = db.Tenants.IgnoreQueryFilters().Single(item => item.TenantId == signup.TenantId);
        Assert.InRange(
            tenant.SubscriptionEndsAtUtc!.Value,
            activatedAt.AddMonths(12).AddMinutes(-1),
            activatedAt.AddMonths(12).AddMinutes(1)
        );
    }

    private static ServiceProvider BuildProvider(string prefix)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"{prefix}-{Guid.NewGuid():N}")
        );
        return services.BuildServiceProvider();
    }
}
