using Billing.Application.Abstractions;
using Billing.Application.Commands;
using Billing.Application.Queries;
using Billing.Domain;

namespace Billing.Application;

/// <summary>Pendant une période payée, le changement de plan s'applique à l'échéance (pas de montée gratuite en cours de période).</summary>
public sealed class ScheduledPlanChangeTests
{
    private readonly BillingFixture _fixture = new();

    private FakeClock Clock => _fixture.Clock;

    private Task<Guid> ProAsync() =>
        _fixture.PlanAsync("Pro", 59m, BillingInterval.Monthly, new EntitlementInput(BillingFeatures.NotificationsSmsMonthly, true, 1000));

    /// <summary>Essentiel (29 EUR, 100 SMS) payé : période payée qui suit l'essai.</summary>
    private async Task<(Guid Plan, Guid Subscription)> PaidAsync()
    {
        var plan = await _fixture.PlanAsync();
        var id = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));
        await _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(id));
        return (plan, id);
    }

    [Fact]
    public async Task An_upgrade_during_a_paid_period_keeps_the_current_rights_until_the_period_end()
    {
        var (plan, id) = await PaidAsync();
        var pro = await ProAsync();
        var end = _fixture.SubscriptionById(id).CurrentPeriod.EndUtc;

        await _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(pro));
        var sms = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.NotificationsSmsMonthly));
        var mine = await _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery());
        var online = await _fixture.OnlinePayment.HandleAsync(new GetOnlinePaymentQuery());

        Assert.Equal(plan, _fixture.SubscriptionById(id).PlanId.Value);
        Assert.Equal(100, sms.Limit);
        Assert.Equal(plan, mine.Plan.Id);
        Assert.Equal(new ScheduledPlanChangeDto(pro, "Pro", end), mine.ScheduledPlan);
        Assert.Equal((pro, end), (mine.Subscription.ScheduledPlanId, mine.Subscription.ScheduledPlanEffectiveAtUtc));
        Assert.Equal(59m, mine.NextCharge!.Total);
        Assert.False(online.Due);
        Assert.Single(_fixture.Payments);
    }

    [Fact]
    public async Task At_the_period_end_the_new_plan_applies_and_the_next_period_is_charged_at_its_price_with_add_ons()
    {
        var (_, id) = await PaidAsync();
        var pro = await ProAsync();
        var online = await _fixture.CreatePlan.HandleAsync(new CreatePlanCommand(
            "Vente en ligne", 15m, "EUR", BillingInterval.Monthly, [new EntitlementInput(BillingFeatures.StorefrontMobileApp, true)], PlanKind.AddOn));
        await _fixture.ActivateAddOn.HandleAsync(new ActivateTenantAddOnCommand(_fixture.TenantA.Value, online));
        await _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(pro));
        var end = _fixture.SubscriptionById(id).CurrentPeriod.EndUtc;

        Clock.Set(end.AddMinutes(1));
        var outcome = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));
        var sms = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.NotificationsSmsMonthly));
        var due = await _fixture.OnlinePayment.HandleAsync(new GetOnlinePaymentQuery());
        var started = await _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand());
        _fixture.ProviderStatuses["ref-1"] = new CollectionStatus(CollectionState.Paid, 74m);
        var paid = await _fixture.RefreshCheckout.HandleAsync(new RefreshSubscriptionCheckoutCommand(started.Id));

        var subscription = _fixture.SubscriptionById(id);
        Assert.Equal(SubscriptionDueOutcome.PastDue, outcome);
        Assert.Equal(1000, sms.Limit);
        Assert.Equal((true, 74m), (due.Due, due.Amount));
        Assert.Equal(SubscriptionCheckoutStatus.Paid, paid.Status);
        Assert.Equal((SubscriptionStatus.Active, pro, (PlanId?)null), (subscription.Status, subscription.PlanId.Value, subscription.ScheduledPlanId));
        Assert.Equal((end, end.AddMonths(1)), (subscription.CurrentPeriod.StartUtc, subscription.CurrentPeriod.EndUtc));
        var payment = _fixture.Payments[^1];
        Assert.Equal((pro, "Pro", 74m), (payment.PlanId.Value, payment.PlanName, payment.Amount.Amount));
    }

    [Fact]
    public async Task A_payment_recorded_before_the_due_job_uses_the_scheduled_plan()
    {
        var (_, id) = await PaidAsync();
        var pro = await ProAsync();
        await _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(pro));
        var end = _fixture.SubscriptionById(id).CurrentPeriod.EndUtc;
        Clock.Set(end.AddMinutes(1));

        var charge = await _fixture.GetCharge.HandleAsync(new GetSubscriptionChargeQuery(id));
        await _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(id));

        var subscription = _fixture.SubscriptionById(id);
        Assert.Equal(59m, charge.Total);
        Assert.Equal((pro, (PlanId?)null), (subscription.PlanId.Value, subscription.ScheduledPlanId));
        Assert.Equal((pro, 59m), (_fixture.Payments[^1].PlanId.Value, _fixture.Payments[^1].Amount.Amount));
    }

    [Fact]
    public async Task A_scheduled_change_can_be_withdrawn_and_the_plan_stays_at_the_period_end()
    {
        var (plan, id) = await PaidAsync();
        var pro = await ProAsync();
        await _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(pro));
        var cancelling = await Assert.ThrowsAsync<DomainException>(() => _fixture.ScheduleCancellation.HandleAsync(new ScheduleSubscriptionCancellationCommand()));

        await _fixture.UnschedulePlanChange.HandleAsync(new UnscheduleSubscriptionPlanChangeCommand());
        var twice = await Assert.ThrowsAsync<DomainException>(() => _fixture.UnschedulePlanChange.HandleAsync(new UnscheduleSubscriptionPlanChangeCommand()));
        var mine = await _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery());
        Clock.Set(_fixture.SubscriptionById(id).CurrentPeriod.EndUtc.AddMinutes(1));
        await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));

        Assert.Equal(SubscriptionErrors.PlanChangeScheduled, cancelling.Error);
        Assert.Equal(SubscriptionErrors.PlanChangeNotScheduled, twice.Error);
        Assert.Null(mine.ScheduledPlan);
        Assert.Equal(29m, mine.NextCharge!.Total);
        Assert.Equal(plan, _fixture.SubscriptionById(id).PlanId.Value);
    }

    [Fact]
    public async Task A_downgrade_is_scheduled_too_and_choosing_the_current_plan_withdraws_it()
    {
        var cheap = await _fixture.PlanAsync("Mini", 9m);
        var (plan, id) = await PaidAsync();

        await _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(cheap));
        var scheduled = _fixture.SubscriptionById(id).ScheduledPlanId;
        await _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(plan));

        Assert.Equal(cheap, scheduled?.Value);
        Assert.Equal((plan, (PlanId?)null), (_fixture.SubscriptionById(id).PlanId.Value, _fixture.SubscriptionById(id).ScheduledPlanId));
    }

    [Fact]
    public async Task During_the_trial_the_change_stays_immediate_and_the_free_plan_stays_refused_once_paid()
    {
        var free = await _fixture.PlanAsync("Découverte", 0m);
        var plan = await _fixture.PlanAsync();
        var pro = await ProAsync();
        var id = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));

        await _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(pro));
        var sms = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.NotificationsSmsMonthly));
        await _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(id));
        var toFree = await Assert.ThrowsAsync<DomainException>(() => _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(free)));

        Assert.Equal(1000, sms.Limit);
        Assert.Equal((pro, (PlanId?)null), (_fixture.SubscriptionById(id).PlanId.Value, _fixture.SubscriptionById(id).ScheduledPlanId));
        Assert.Equal(59m, _fixture.Payments.Single().Amount.Amount);
        Assert.Equal(BillingApplicationErrors.FreePlanUnavailable, toFree.Error);
    }
}
