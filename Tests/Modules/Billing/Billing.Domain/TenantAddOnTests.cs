namespace Billing.Domain;

public sealed class TenantAddOnTests
{
    private static readonly FeatureCode Kiosk = FeatureCode.Create("storefront.kiosk");

    [Fact]
    public void Entitlements_combine_the_plan_and_the_add_ons()
    {
        var disabled = Entitlement.Disabled(Kiosk);

        Assert.Equal(disabled, TenantAddOn.Combine(disabled, []));
        Assert.Equal(Entitlement.Limited(Kiosk, 6), TenantAddOn.Combine(disabled, [(Entitlement.Limited(Kiosk, 2), 3)]));
        Assert.Equal(Entitlement.Limited(Kiosk, 5), TenantAddOn.Combine(Entitlement.Limited(Kiosk, 1), [(Entitlement.Limited(Kiosk, 2), 2)]));
        Assert.Equal(Entitlement.Unlimited(Kiosk), TenantAddOn.Combine(Entitlement.Limited(Kiosk, 1), [(Entitlement.Unlimited(Kiosk), 1)]));
        Assert.Equal(Entitlement.Limited(Kiosk, 1), TenantAddOn.Combine(Entitlement.Limited(Kiosk, 1), [(Entitlement.Disabled(Kiosk), 4)]));
        Assert.Equal(Entitlement.Limited(Kiosk, long.MaxValue), TenantAddOn.Combine(disabled, [(Entitlement.Limited(Kiosk, long.MaxValue), 2)]));
    }

    [Fact]
    public void An_add_on_is_activated_resized_then_cancelled_for_good()
    {
        var clock = new FakeClock();
        var plan = Plan.Create("Borne", Money.Of(10m, Currency.EUR), BillingInterval.Monthly, [Entitlement.Limited(Kiosk, 1)], clock, PlanKind.AddOn);
        var addOn = TenantAddOn.Activate(TenantId.New(), plan.ToReference(), 2, hasNoActiveSameAddOn: true, clock);

        addOn.ChangeQuantity(4, clock);
        var tooMany = Assert.Throws<DomainException>(() => addOn.ChangeQuantity(TenantAddOn.MaxQuantity + 1, clock));
        addOn.Cancel(clock);
        var afterCancel = Assert.Throws<DomainException>(() => addOn.ChangeQuantity(1, clock));

        Assert.Equal(4, addOn.Quantity);
        Assert.Equal(TenantAddOnErrors.QuantityInvalid, tooMany.Error);
        Assert.Equal(TenantAddOnErrors.Cancelled, afterCancel.Error);
        Assert.False(addOn.IsActive);
        Assert.Equal(3, addOn.DomainEvents.Count);
    }

    [Fact]
    public void Only_an_active_add_on_plan_can_be_added_once()
    {
        var clock = new FakeClock();
        var basePlan = Plan.Create("Pro", Money.Of(59m, Currency.EUR), BillingInterval.Monthly, [], clock);
        var addOnPlan = Plan.Create("Web", Money.Of(10m, Currency.EUR), BillingInterval.Monthly, [], clock, PlanKind.AddOn);
        var tenant = TenantId.New();

        var notAddOn = Assert.Throws<DomainException>(() => TenantAddOn.Activate(tenant, basePlan.ToReference(), 1, true, clock));
        var twice = Assert.Throws<DomainException>(() => TenantAddOn.Activate(tenant, addOnPlan.ToReference(), 1, false, clock));
        var zero = Assert.Throws<DomainException>(() => TenantAddOn.Activate(tenant, addOnPlan.ToReference(), 0, true, clock));
        addOnPlan.Archive(clock);
        var archived = Assert.Throws<DomainException>(() => TenantAddOn.Activate(tenant, addOnPlan.ToReference(), 1, true, clock));

        Assert.Equal(TenantAddOnErrors.NotAnAddOn, notAddOn.Error);
        Assert.Equal(TenantAddOnErrors.AlreadyActive, twice.Error);
        Assert.Equal(TenantAddOnErrors.QuantityInvalid, zero.Error);
        Assert.Equal(TenantAddOnErrors.AddOnArchived, archived.Error);
    }
}
