using VehiclePermitSystemWeb.Infrastructure;
using VehiclePermitSystemWeb.Models.Entities;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Security)]
public sealed class SubscriptionAccessMiddlewareTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PendingPayment_IsRestricted()
    {
        var tenant = CreateTenant(TenantSubscriptionStatuses.PendingPayment);

        Assert.True(SubscriptionAccessMiddleware.IsSubscriptionRestricted(tenant, Now));
    }

    [Fact]
    public void ActiveSubscription_WithFutureEndDate_IsAllowed()
    {
        var tenant = CreateTenant(TenantSubscriptionStatuses.Active);
        tenant.SubscriptionEndsAtUtc = Now.AddDays(1);

        Assert.False(SubscriptionAccessMiddleware.IsSubscriptionRestricted(tenant, Now));
    }

    [Fact]
    public void ActiveSubscription_WithPastEndDate_IsRestricted()
    {
        var tenant = CreateTenant(TenantSubscriptionStatuses.Active);
        tenant.SubscriptionEndsAtUtc = Now.AddMinutes(-1);

        Assert.True(SubscriptionAccessMiddleware.IsSubscriptionRestricted(tenant, Now));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public void Trial_UsesTrialEndDate(int dayOffset, bool expectedRestriction)
    {
        var tenant = CreateTenant(TenantSubscriptionStatuses.Trial);
        tenant.TrialEndsAtUtc = Now.AddDays(dayOffset);

        Assert.Equal(
            expectedRestriction,
            SubscriptionAccessMiddleware.IsSubscriptionRestricted(tenant, Now)
        );
    }

    [Fact]
    public void InactiveTenant_IsRestricted()
    {
        var tenant = CreateTenant(TenantSubscriptionStatuses.Active);
        tenant.IsActive = false;

        Assert.True(SubscriptionAccessMiddleware.IsSubscriptionRestricted(tenant, Now));
    }

    private static Tenant CreateTenant(string status)
    {
        return new Tenant
        {
            TenantId = "subscription-test",
            Name = "Subscription Test",
            Slug = "subscription-test",
            IsActive = true,
            SubscriptionStatus = status,
        };
    }
}
