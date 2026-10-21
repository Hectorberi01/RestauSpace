using Billing.Application.Commands;
using Billing.Application.Queries;
using Billing.Domain;

namespace Billing.Application;

public sealed class AddOnSubscriptionTests
{
    private readonly BillingFixture _fixture = new();

    private Task<Guid> AddOnAsync(string name, decimal price, BillingInterval interval, params EntitlementInput[] entitlements) =>
        _fixture.CreatePlan.HandleAsync(new CreatePlanCommand(name, price, "EUR", interval, entitlements, PlanKind.AddOn));

    private Task<Guid> SiteAsync() => AddOnAsync("Site web", 5m, BillingInterval.Monthly, new EntitlementInput(BillingFeatures.StorefrontWeb, true));

    private Task<Guid> KioskAsync() => AddOnAsync("Borne", 12m, BillingInterval.Monthly, new EntitlementInput(BillingFeatures.StorefrontKiosk, true, 1));

    private async Task<Guid> TrialAsync(decimal price = 29m, BillingInterval interval = BillingInterval.Monthly)
    {
        var plan = await _fixture.PlanAsync(price: price, interval: interval);
        return await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));
    }

    [Fact]
    public async Task A_restaurant_subscribes_an_add_on_and_gets_its_rights_at_once()
    {
        await TrialAsync();
        var site = await SiteAsync();
        var kiosk = await KioskAsync();

        await _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(site));
        var kiosks = await _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(kiosk, 2));
        await _fixture.ChangeMyAddOnQuantity.HandleAsync(new ChangeMyAddOnQuantityCommand(kiosks, 3));

        Assert.True((await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.StorefrontWeb))).Enabled);
        Assert.Equal(3, (await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.StorefrontKiosk))).Limit);
        var mine = await _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery());
        Assert.Equal(["Essentiel", "Borne", "Site web"], mine.NextCharge!.Lines.Select(line => line.Label));
        Assert.Equal((29m + 36m + 5m, 70m), (mine.NextCharge.Total, mine.NextCharge.MonthlyTotal));
        Assert.Equal((3, 12m, 36m), (mine.NextCharge.Lines[1].Quantity, mine.NextCharge.Lines[1].UnitAmount, mine.NextCharge.Lines[1].Amount));
    }

    [Fact]
    public async Task The_recorded_payment_bills_the_plan_and_the_active_add_ons_line_by_line()
    {
        var subscription = await TrialAsync();
        var kiosk = await KioskAsync();
        await _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(kiosk, 2));

        await _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(subscription));

        var payment = Assert.Single(_fixture.Payments);
        Assert.Equal(Money.Of(53m, Currency.EUR), payment.Amount);
        Assert.Equal([(PlanKind.Base, 1, 29m), (PlanKind.AddOn, 2, 24m)], payment.Lines.Select(line => (line.Kind, line.Quantity, line.Amount)));
        var listed = Assert.Single(await _fixture.ListPayments.HandleAsync(new ListSubscriptionPaymentsQuery()));
        Assert.Equal(2, listed.Lines!.Count);
    }

    [Fact]
    public async Task A_monthly_add_on_on_a_yearly_plan_is_billed_for_twelve_months()
    {
        var subscription = await TrialAsync(290m, BillingInterval.Yearly);
        await _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(await SiteAsync()));

        var charge = await _fixture.GetCharge.HandleAsync(new GetSubscriptionChargeQuery(subscription));

        Assert.Equal((60m, 350m), (charge.Lines[1].Amount, charge.Total));
        Assert.Equal(Math.Round(290m / 12m + 5m, 2), charge.MonthlyTotal);
    }

    [Fact]
    public async Task Add_ons_are_changed_only_during_the_trial_or_a_paid_period()
    {
        var site = await SiteAsync();
        var none = await Assert.ThrowsAsync<DomainException>(() => _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(site)));
        var subscription = await TrialAsync();
        _fixture.SubscriptionById(subscription).Suspend(SuspensionReason.PolicyViolation, _fixture.Clock);

        var suspended = await Assert.ThrowsAsync<DomainException>(() => _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(site)));

        Assert.Equal(BillingApplicationErrors.AddOnNeedsSubscription, none.Error);
        Assert.Equal(BillingApplicationErrors.AddOnNeedsOpenSubscription, suspended.Error);
    }

    [Fact]
    public async Task A_restaurant_cancels_its_own_add_on_only()
    {
        await TrialAsync();
        var mine = await _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(await SiteAsync()));
        _fixture.CurrentTenantId = _fixture.TenantB;

        var other = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.CancelMyAddOn.HandleAsync(new CancelMyAddOnCommand(mine)));
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.CancelMyAddOn.HandleAsync(new CancelMyAddOnCommand(mine));

        Assert.Equal(BillingApplicationErrors.AddOnNotFound, other.Error);
        Assert.False((await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.StorefrontWeb))).Enabled);
        Assert.Empty((await _fixture.ListMyAddOns.HandleAsync(new ListMyAddOnsQuery())).Active);
    }

    [Fact]
    public async Task A_suspended_restaurant_cannot_subscribe()
    {
        await TrialAsync();
        var site = await SiteAsync();
        _fixture.Writable[_fixture.TenantA] = false;

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(site)));

        Assert.Equal(BillingApplicationErrors.TenantNotWritable, refused.Error);
    }

    [Fact]
    public async Task Cancelling_the_subscription_cancels_its_add_ons()
    {
        var subscription = await TrialAsync();
        await _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(await SiteAsync()));

        await _fixture.AdminCancel.HandleAsync(new AdminCancelSubscriptionCommand(subscription));

        Assert.All(_fixture.AddOns, addOn => Assert.Equal(TenantAddOnStatus.Cancelled, addOn.Status));
    }

    [Fact]
    public async Task A_scheduled_cancellation_cancels_the_add_ons_at_the_end_of_the_period()
    {
        var subscription = await TrialAsync();
        await _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(await SiteAsync()));
        await _fixture.ScheduleCancellation.HandleAsync(new ScheduleSubscriptionCancellationCommand());
        _fixture.Clock.Advance(TimeSpan.FromDays(30));

        var outcome = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(subscription));

        Assert.Equal(SubscriptionDueOutcome.Cancelled, outcome);
        Assert.All(_fixture.AddOns, addOn => Assert.Equal(TenantAddOnStatus.Cancelled, addOn.Status));
    }

    [Fact]
    public async Task The_platform_lists_the_active_add_ons_of_every_restaurant()
    {
        await TrialAsync();
        var site = await SiteAsync();
        var kiosk = await KioskAsync();
        await _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(site));
        var cancelled = await _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(kiosk, 2));
        await _fixture.CancelMyAddOn.HandleAsync(new CancelMyAddOnCommand(cancelled));
        _fixture.CurrentTenantId = _fixture.TenantB;
        await TrialAsync();
        await _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(kiosk, 3));
        _fixture.CurrentTenantId = null;

        var all = await _fixture.ListActiveAddOns.HandleAsync(new ListActiveAddOnsQuery());

        Assert.Equal(
            [(_fixture.TenantA.Value, "Site web", 1), (_fixture.TenantB.Value, "Borne", 3)],
            all.Select(addOn => (addOn.TenantId, addOn.AddOn.Name, addOn.Quantity)).OrderBy(item => item.Item2 == "Borne"));
    }
}
