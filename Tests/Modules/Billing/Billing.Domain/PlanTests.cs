using Billing.Domain;

namespace Billing.Domain;

public class PlanTests
{
    private static readonly FeatureCode Orders = FeatureCode.Create("orders.monthly");
    private static readonly FeatureCode Kds = FeatureCode.Create("kds");

    private readonly FakeClock _clock = new();

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.EUR);

    private Plan NewPlan(params Entitlement[] entitlements)
    {
        var plan = Plan.Create("Pro", Eur(49m), BillingInterval.Monthly, entitlements, _clock);
        plan.ClearDomainEvents();
        return plan;
    }

    [Fact]
    public void Create_starts_active_with_its_entitlements()
    {
        var plan = Plan.Create("  Pro ", Eur(49m), BillingInterval.Yearly, [Entitlement.Limited(Orders, 500), Entitlement.Unlimited(Kds)], _clock);

        Assert.Equal("Pro", plan.Name);
        Assert.Equal(Eur(49m), plan.Price);
        Assert.Equal(BillingInterval.Yearly, plan.BillingInterval);
        Assert.Equal(PlanStatus.Active, plan.Status);
        Assert.Equal(2, plan.Entitlements.Count);
        Assert.Equal(Entitlement.Limited(Orders, 500), plan.EntitlementFor(Orders));

        var created = DomainAssert.Raised<PlanCreated>(plan);
        Assert.Equal(
            (plan.Id, "Pro", Eur(49m), BillingInterval.Yearly, FakeClock.DefaultNow),
            (created.PlanId, created.Name, created.Price, created.BillingInterval, created.OccurredAtUtc));
    }

    [Fact]
    public void Plan_is_platform_global_and_not_tenant_owned()
    {
        Assert.False(typeof(ITenantOwned).IsAssignableFrom(typeof(Plan)));
        Assert.Null(typeof(Plan).GetProperty("TenantId"));
    }

    [Fact]
    public void Create_accepts_a_free_plan_without_entitlement()
    {
        var plan = Plan.Create("Free", Eur(0m), BillingInterval.Monthly, [], _clock);

        Assert.True(plan.Price.IsZero);
        Assert.Empty(plan.Entitlements);
        Assert.Equal(Entitlement.Disabled(Orders), plan.EntitlementFor(Orders));
    }

    [Fact]
    public void Create_rejects_a_negative_price() =>
        DomainAssert.Throws(PlanErrors.PriceNegative, () => Plan.Create("Pro", Eur(-0.01m), BillingInterval.Monthly, [], _clock));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_name(string? name) =>
        DomainAssert.Throws(PlanErrors.NameRequired, () => Plan.Create(name!, Eur(49m), BillingInterval.Monthly, [], _clock));

    [Fact]
    public void Create_rejects_a_too_long_name() =>
        DomainAssert.Throws(
            PlanErrors.NameTooLong,
            () => Plan.Create(new string('p', Plan.MaxNameLength + 1), Eur(49m), BillingInterval.Monthly, [], _clock));

    [Fact]
    public void Create_rejects_an_unknown_billing_interval() =>
        DomainAssert.Throws(PlanErrors.BillingIntervalInvalid, () => Plan.Create("Pro", Eur(49m), (BillingInterval)7, [], _clock));

    [Fact]
    public void Create_rejects_two_entitlements_for_the_same_feature() =>
        DomainAssert.Throws(
            PlanErrors.DuplicateEntitlement,
            () => Plan.Create("Pro", Eur(49m), BillingInterval.Monthly, [Entitlement.Limited(Orders, 500), Entitlement.Unlimited(Orders)], _clock));

    [Fact]
    public void ChangePriceForFuture_updates_the_catalogue_price()
    {
        var plan = NewPlan();

        plan.ChangePriceForFuture(Eur(59m), _clock);

        Assert.Equal(Eur(59m), plan.Price);
        var changed = DomainAssert.Raised<PlanDefinitionChanged>(plan);
        Assert.Equal((plan.Id, PlanDefinitionChange.PriceChanged, Eur(59m), (Entitlement?)null), (changed.PlanId, changed.Change, changed.Price, changed.Entitlement));
    }

    [Fact]
    public void ChangePriceForFuture_changes_nothing_but_the_catalogue_price()
    {
        var plan = NewPlan(Entitlement.Limited(Orders, 500));

        plan.ChangePriceForFuture(Eur(99m), _clock);

        // Le plan ne porte aucun état d'abonnement : il peut seulement prouver que seul son propre prix a changé.
        Assert.Equal(Eur(99m), plan.Price);
        Assert.Equal(BillingInterval.Monthly, plan.BillingInterval);
        Assert.Equal(PlanStatus.Active, plan.Status);
        Assert.Equal(Entitlement.Limited(Orders, 500), Assert.Single(plan.Entitlements));
        var changed = Assert.IsType<PlanDefinitionChanged>(Assert.Single(plan.DomainEvents));
        Assert.Equal(PlanDefinitionChange.PriceChanged, changed.Change);
        Assert.Equal(_clock.UtcNow, changed.OccurredAtUtc);
    }

    [Fact]
    public void Create_rejects_a_null_entitlement_as_a_programming_error() =>
        Assert.Throws<ArgumentNullException>(
            () => Plan.Create("Pro", Eur(49m), BillingInterval.Monthly, [Entitlement.Unlimited(Kds), null!], _clock));

    [Fact]
    public void ChangePriceForFuture_accepts_zero_and_rejects_a_negative_price()
    {
        var plan = NewPlan();

        DomainAssert.Throws(PlanErrors.PriceNegative, () => plan.ChangePriceForFuture(Eur(-1m), _clock));
        Assert.Equal(Eur(49m), plan.Price);
        DomainAssert.NoEvents(plan);

        plan.ChangePriceForFuture(Eur(0m), _clock);
        Assert.True(plan.Price.IsZero);
    }

    [Fact]
    public void ChangePriceForFuture_rejects_another_currency()
    {
        var plan = NewPlan();

        DomainAssert.Throws(PlanErrors.PriceCurrencyMismatch, () => plan.ChangePriceForFuture(Money.Of(59m, Currency.USD), _clock));

        Assert.Equal(Eur(49m), plan.Price);
        DomainAssert.NoEvents(plan);
    }

    [Fact]
    public void ChangePriceForFuture_to_the_same_price_changes_nothing()
    {
        var plan = NewPlan();

        plan.ChangePriceForFuture(Eur(49m), _clock);

        Assert.Equal(Eur(49m), plan.Price);
        DomainAssert.NoEvents(plan);
    }

    [Fact]
    public void SetEntitlement_adds_a_new_feature()
    {
        var plan = NewPlan(Entitlement.Limited(Orders, 500));

        plan.SetEntitlement(Entitlement.Unlimited(Kds), _clock);

        Assert.Equal(2, plan.Entitlements.Count);
        Assert.Equal(Entitlement.Unlimited(Kds), plan.EntitlementFor(Kds));
        var changed = DomainAssert.Raised<PlanDefinitionChanged>(plan);
        Assert.Equal((plan.Id, PlanDefinitionChange.EntitlementSet, Entitlement.Unlimited(Kds), Eur(49m)), (changed.PlanId, changed.Change, changed.Entitlement, changed.Price));
    }

    [Fact]
    public void SetEntitlement_replaces_the_quota_of_an_existing_feature()
    {
        var plan = NewPlan(Entitlement.Limited(Orders, 500), Entitlement.Unlimited(Kds));

        plan.SetEntitlement(Entitlement.Limited(Orders, 2000), _clock);

        Assert.Equal(2, plan.Entitlements.Count);
        Assert.Equal(2000, plan.EntitlementFor(Orders).Limit);
        Assert.Single(plan.Entitlements, entitlement => entitlement.FeatureCode == Orders);
    }

    [Fact]
    public void SetEntitlement_keeps_one_entitlement_per_feature_whatever_the_sequence()
    {
        var plan = NewPlan();

        plan.SetEntitlement(Entitlement.Limited(Orders, 10), _clock);
        plan.SetEntitlement(Entitlement.Disabled(Orders), _clock);
        plan.SetEntitlement(Entitlement.Unlimited(FeatureCode.Create("ORDERS.MONTHLY")), _clock);
        plan.SetEntitlement(Entitlement.Limited(Kds, 2), _clock);

        Assert.Equal(2, plan.Entitlements.Count);
        Assert.Equal(2, plan.Entitlements.Select(entitlement => entitlement.FeatureCode).Distinct().Count());
        Assert.True(plan.EntitlementFor(Orders).IsUnlimited);
    }

    [Fact]
    public void SetEntitlement_with_an_identical_entitlement_changes_nothing()
    {
        var plan = NewPlan(Entitlement.Limited(Orders, 500));

        plan.SetEntitlement(Entitlement.Limited(Orders, 500), _clock);

        Assert.Equal(Entitlement.Limited(Orders, 500), Assert.Single(plan.Entitlements));
        DomainAssert.NoEvents(plan);
    }

    [Fact]
    public void EntitlementFor_returns_the_stored_entitlement_or_a_disabled_one_when_the_feature_is_missing()
    {
        var plan = NewPlan(Entitlement.Limited(Orders, 500));

        Assert.Equal(Entitlement.Limited(Orders, 500), plan.EntitlementFor(Orders));

        var missing = plan.EntitlementFor(Kds);
        Assert.Equal(Kds, missing.FeatureCode);
        Assert.False(missing.Enabled);
        Assert.False(missing.Allows(0));
        Assert.Single(plan.Entitlements);
    }

    [Fact]
    public void A_feature_missing_from_the_plan_blocks_usage()
    {
        var plan = NewPlan();
        var period = Period.Create(FakeClock.DefaultNow.AddDays(-1), FakeClock.DefaultNow.AddDays(29));
        var counter = UsageCounter.Open(TenantId.New(), Orders, period, isFirstCounterForFeature: true);

        DomainAssert.Throws(
            UsageCounterErrors.FeatureDisabled,
            () => counter.Increment(1, plan.EntitlementFor(Orders), blocking: true, _clock));
    }

    [Fact]
    public void Entitlements_cannot_be_modified_from_outside()
    {
        var plan = NewPlan(Entitlement.Limited(Orders, 500));

        Assert.Throws<NotSupportedException>(() => ((ICollection<Entitlement>)plan.Entitlements).Clear());
    }

    [Fact]
    public void Archive_withdraws_the_plan()
    {
        var plan = NewPlan();
        Assert.Equal((plan.Id, true), (plan.ToReference().PlanId, plan.ToReference().IsActive));

        plan.Archive(_clock);

        Assert.Equal(PlanStatus.Archived, plan.Status);
        Assert.Equal(plan.Id, DomainAssert.Raised<PlanArchived>(plan).PlanId);
        Assert.Equal((plan.Id, false), (plan.ToReference().PlanId, plan.ToReference().IsActive));
    }

    [Fact]
    public void Archived_plan_rejects_every_change()
    {
        var plan = NewPlan(Entitlement.Limited(Orders, 500));
        plan.Archive(_clock);
        plan.ClearDomainEvents();

        DomainAssert.Throws(PlanErrors.Archived, () => plan.ChangePriceForFuture(Eur(59m), _clock));
        DomainAssert.Throws(PlanErrors.Archived, () => plan.SetEntitlement(Entitlement.Unlimited(Kds), _clock));
        DomainAssert.Throws(PlanErrors.Archived, () => plan.Archive(_clock));

        Assert.Equal(Eur(49m), plan.Price);
        Assert.Single(plan.Entitlements);
        DomainAssert.NoEvents(plan);
    }
}
