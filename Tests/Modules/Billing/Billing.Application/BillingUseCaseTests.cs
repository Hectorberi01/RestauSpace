using Billing.Application.Commands;
using Billing.Application.Queries;
using Billing.Domain;

namespace Billing.Application;

public sealed class BillingUseCaseTests
{
    private readonly BillingFixture _fixture = new();

    private FakeClock Clock => _fixture.Clock;

    /// <summary>Le restaurant courant choisit un plan : essai de 14 jours.</summary>
    private async Task<(Guid Plan, Guid Subscription)> TrialAsync()
    {
        var plan = await _fixture.PlanAsync();
        return (plan, await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan)));
    }

    /// <summary>Essai converti (paiement enregistré par la plateforme) : abonnement actif jusqu'à fin d'essai + 1 mois.</summary>
    private async Task<(Guid Plan, Guid Subscription)> ActiveAsync()
    {
        var (plan, subscription) = await TrialAsync();
        _ = await _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(subscription));
        return (plan, subscription);
    }

    // --- Options (vente en ligne, bornes)

    private Task<Guid> AddOnAsync(string name, params EntitlementInput[] entitlements) =>
        _fixture.CreatePlan.HandleAsync(new CreatePlanCommand(name, 15m, "EUR", BillingInterval.Monthly, entitlements, PlanKind.AddOn));

    [Fact]
    public async Task An_add_on_adds_its_entitlements_to_the_plan_multiplied_by_its_quantity()
    {
        await ActiveAsync();
        var online = await AddOnAsync("Vente en ligne", new EntitlementInput(BillingFeatures.StorefrontMobileApp, true));
        var kiosk = await AddOnAsync("Borne", new EntitlementInput(BillingFeatures.StorefrontKiosk, true, 1));
        var before = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.StorefrontMobileApp));

        await _fixture.ActivateAddOn.HandleAsync(new ActivateTenantAddOnCommand(_fixture.TenantA.Value, online));
        var kioskId = await _fixture.ActivateAddOn.HandleAsync(new ActivateTenantAddOnCommand(_fixture.TenantA.Value, kiosk, 2));
        var twice = await Assert.ThrowsAsync<DomainException>(() => _fixture.ActivateAddOn.HandleAsync(new ActivateTenantAddOnCommand(_fixture.TenantA.Value, online)));
        await _fixture.ChangeAddOnQuantity.HandleAsync(new ChangeTenantAddOnQuantityCommand(_fixture.TenantA.Value, kioskId, 3));
        var web = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.StorefrontMobileApp));
        var kiosks = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.StorefrontKiosk));
        var api = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.StorefrontApi));
        var mine = await _fixture.ListMyAddOns.HandleAsync(new ListMyAddOnsQuery());
        var plans = await _fixture.ListMyPlans.HandleAsync(new ListMyPlansQuery());
        var usage = (await _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery())).Usage;

        Assert.False(before.Enabled);
        Assert.Equal(TenantAddOnErrors.AlreadyActive, twice.Error);
        Assert.Equal((true, (long?)null), (web.Enabled, web.Limit));
        Assert.Equal((true, (long?)3), (kiosks.Enabled, kiosks.Limit));
        Assert.False(api.Enabled);
        Assert.Equal(2, mine.Offered.Count);
        Assert.Equal(2, mine.Active.Count);
        Assert.DoesNotContain(plans, plan => plan.Kind == PlanKind.AddOn);
        Assert.Contains(usage, feature => feature.FeatureCode == BillingFeatures.StorefrontKiosk && feature.Limit == 3);
    }

    [Fact]
    public async Task An_add_on_is_never_a_subscription_plan_needs_a_subscription_and_stops_when_cancelled()
    {
        var online = await AddOnAsync("Vente en ligne", new EntitlementInput(BillingFeatures.StorefrontMobileApp, true));
        var asPlan = await Assert.ThrowsAsync<DomainException>(() => _fixture.Start.HandleAsync(new StartSubscriptionCommand(online)));
        var noSubscription = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.ActivateAddOn.HandleAsync(new ActivateTenantAddOnCommand(_fixture.TenantA.Value, online)));
        var (plan, _) = await ActiveAsync();
        var notAnAddOn = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.ActivateAddOn.HandleAsync(new ActivateTenantAddOnCommand(_fixture.TenantA.Value, plan)));
        var unknownTenant = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.ActivateAddOn.HandleAsync(new ActivateTenantAddOnCommand(Guid.NewGuid(), online)));
        var id = await _fixture.ActivateAddOn.HandleAsync(new ActivateTenantAddOnCommand(_fixture.TenantA.Value, online));
        var otherTenant = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.CancelAddOn.HandleAsync(new CancelTenantAddOnCommand(_fixture.TenantB.Value, id)));
        await _fixture.CancelAddOn.HandleAsync(new CancelTenantAddOnCommand(_fixture.TenantA.Value, id));
        var after = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.StorefrontMobileApp));
        var history = await _fixture.ListTenantAddOns.HandleAsync(new ListTenantAddOnsQuery(_fixture.TenantA.Value));

        Assert.Equal(SubscriptionErrors.PlanIsAddOn, asPlan.Error);
        Assert.Equal(BillingApplicationErrors.AddOnNeedsSubscription, noSubscription.Error);
        Assert.Equal(TenantAddOnErrors.NotAnAddOn, notAnAddOn.Error);
        Assert.Equal(BillingApplicationErrors.TenantNotFound, unknownTenant.Error);
        Assert.Equal(BillingApplicationErrors.AddOnNotFound, otherTenant.Error);
        Assert.False(after.Enabled);
        Assert.Equal(TenantAddOnStatus.Cancelled, Assert.Single(history).Status);
    }

    // --- Catalogue (plateforme)

    [Fact]
    public async Task A_plan_is_created_priced_given_entitlements_and_archived()
    {
        var plan = await _fixture.PlanAsync("Pro", 59m);

        await _fixture.ChangePrice.HandleAsync(new ChangePlanPriceCommand(plan, 69m));
        await _fixture.SetEntitlement.HandleAsync(new SetPlanEntitlementCommand(plan, new EntitlementInput(BillingFeatures.Locations, true, 3)));
        var read = await _fixture.GetPlan.HandleAsync(new GetPlanQuery(plan));
        await _fixture.ArchivePlan.HandleAsync(new ArchivePlanCommand(plan));

        Assert.Equal((69m, "EUR", PlanStatus.Active), (read.Price, read.CurrencyCode, read.Status));
        Assert.Contains(read.Entitlements, entitlement => entitlement == new EntitlementDto(BillingFeatures.Locations, true, 3));
        Assert.Empty(await _fixture.ListPlans.HandleAsync(new ListPlansQuery(ActiveOnly: true)));
        Assert.Single(await _fixture.ListPlans.HandleAsync(new ListPlansQuery(ActiveOnly: false)));
    }

    [Fact]
    public async Task Invalid_plans_are_refused_with_the_domain_error()
    {
        var negative = await Assert.ThrowsAsync<DomainException>(() => _fixture.PlanAsync(price: -1m));
        var badFeature = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.PlanAsync(entitlements: new[] { new EntitlementInput("Pas un code !", true) }));
        var disabledWithLimit = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.PlanAsync(entitlements: new[] { new EntitlementInput(BillingFeatures.OrdersMonthly, false, 10) }));

        Assert.Equal(PlanErrors.PriceNegative, negative.Error);
        Assert.Equal(PlanErrors.FeatureCodeInvalid, badFeature.Error);
        Assert.Equal(PlanErrors.EntitlementDisabledWithLimit, disabledWithLimit.Error);
        Assert.Empty(_fixture.Plans);
    }

    // --- Abonnement (restaurant)

    [Fact]
    public async Task Choosing_a_plan_starts_a_trial_once()
    {
        var (plan, id) = await TrialAsync();

        var mine = await _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery());
        var second = await Assert.ThrowsAsync<DomainException>(() => _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan)));

        Assert.Equal((id, plan, SubscriptionStatus.Trialing), (mine.Subscription.Id, mine.Plan.Id, mine.Subscription.Status));
        Assert.Equal(Clock.UtcNow + _fixture.Settings.TrialLength, mine.Subscription.CurrentPeriodEndUtc);
        Assert.Equal(SubscriptionErrors.AlreadySubscribed, second.Error);
        Assert.Equal(_fixture.TenantA, Assert.Single(_fixture.Subscriptions).TenantId);
    }

    [Fact]
    public async Task An_archived_or_unknown_plan_cannot_be_chosen()
    {
        var plan = await _fixture.PlanAsync();
        await _fixture.ArchivePlan.HandleAsync(new ArchivePlanCommand(plan));

        var archived = await Assert.ThrowsAsync<DomainException>(() => _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Start.HandleAsync(new StartSubscriptionCommand(Guid.NewGuid())));

        Assert.Equal(SubscriptionErrors.PlanArchived, archived.Error);
    }

    [Fact]
    public async Task The_plan_changes_during_the_trial()
    {
        var (_, id) = await TrialAsync();
        var pro = await _fixture.PlanAsync("Pro", 59m);

        await _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(pro));

        Assert.Equal(pro, _fixture.SubscriptionById(id).PlanId.Value);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_but_cannot_choose_nor_change_its_plan_yet_can_still_leave()
    {
        var (plan, _) = await TrialAsync();
        _fixture.Writable[_fixture.TenantA] = false;

        var change = await Assert.ThrowsAsync<DomainException>(() => _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(plan)));
        await _fixture.ScheduleCancellation.HandleAsync(new ScheduleSubscriptionCancellationCommand());
        var mine = await _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery());

        Assert.Equal(BillingApplicationErrors.TenantNotWritable, change.Error);
        Assert.True(mine.Subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task A_scheduled_cancellation_can_be_withdrawn_before_the_period_ends()
    {
        var (_, id) = await ActiveAsync();

        await _fixture.ScheduleCancellation.HandleAsync(new ScheduleSubscriptionCancellationCommand());
        var twice = await Assert.ThrowsAsync<DomainException>(() => _fixture.ScheduleCancellation.HandleAsync(new ScheduleSubscriptionCancellationCommand()));
        await _fixture.UnscheduleCancellation.HandleAsync(new UnscheduleSubscriptionCancellationCommand());

        Assert.Equal(SubscriptionErrors.CancellationAlreadyScheduled, twice.Error);
        Assert.False(_fixture.SubscriptionById(id).CancelAtPeriodEnd);
    }

    [Fact]
    public async Task A_restaurant_sees_and_subscribes_only_to_plans_of_its_currency()
    {
        var euro = await _fixture.PlanAsync("Essentiel");
        var cfa = await _fixture.CreatePlan.HandleAsync(new CreatePlanCommand("Essentiel CFA", 9900m, "XOF", BillingInterval.Monthly));
        _fixture.Currencies[_fixture.TenantB] = Currency.XOF;

        var forA = await _fixture.ListMyPlans.HandleAsync(new ListMyPlansQuery());
        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Start.HandleAsync(new StartSubscriptionCommand(cfa)));
        _fixture.CurrentTenantId = _fixture.TenantB;
        var forB = await _fixture.ListMyPlans.HandleAsync(new ListMyPlansQuery());
        var started = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(cfa));
        var change = await Assert.ThrowsAsync<DomainException>(() => _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(euro)));
        _fixture.CurrentTenantId = null;
        var platform = await _fixture.ListPlans.HandleAsync(new ListPlansQuery(ActiveOnly: false, CurrencyCode: "xof"));

        Assert.Equal([euro], forA.Select(plan => plan.Id));
        Assert.Equal([cfa], forB.Select(plan => plan.Id));
        Assert.Equal(BillingApplicationErrors.PlanCurrencyMismatch, refused.Error);
        Assert.Equal(BillingApplicationErrors.PlanCurrencyMismatch, change.Error);
        Assert.NotEqual(Guid.Empty, started);
        Assert.Equal([cfa], platform.Select(plan => plan.Id));
    }

    [Fact]
    public async Task Without_a_live_subscription_the_restaurant_gets_404_and_no_entitlement()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery()));
        var limit = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.NotificationsSmsMonthly));

        Assert.Equal(new FeatureLimitDto(BillingFeatures.NotificationsSmsMonthly, false, null, false), limit);
    }

    // --- Paiements et échéances (plateforme et job)

    [Fact]
    public async Task A_payment_during_the_trial_activates_the_period_that_follows_it()
    {
        var (_, id) = await TrialAsync();
        var trialEnd = _fixture.SubscriptionById(id).CurrentPeriod.EndUtc;
        _fixture.CurrentTenantId = null;

        var paid = await _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(id));
        var again = await Assert.ThrowsAsync<DomainException>(() => _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(id)));

        Assert.Equal((SubscriptionStatus.Active, trialEnd, trialEnd.AddMonths(1)), (paid.Status, paid.PaidFromUtc, paid.PaidUntilUtc));
        Assert.Equal(BillingApplicationErrors.NothingToPay, again.Error);
    }

    [Fact]
    public async Task An_ended_paid_period_is_renewed_contiguously_by_the_next_payment()
    {
        var (_, id) = await ActiveAsync();
        var end = _fixture.SubscriptionById(id).CurrentPeriod.EndUtc;
        Clock.Set(end.AddHours(1));

        var paid = await _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(id));

        Assert.Equal((SubscriptionStatus.Active, end, end.AddMonths(1)), (paid.Status, paid.PaidFromUtc, paid.PaidUntilUtc));
    }

    [Fact]
    public async Task Each_payment_is_kept_in_the_history_at_the_price_of_the_moment()
    {
        var (plan, id) = await ActiveAsync();
        var firstEnd = _fixture.SubscriptionById(id).CurrentPeriod.EndUtc;
        await _fixture.ChangePrice.HandleAsync(new ChangePlanPriceCommand(plan, 39m));
        Clock.Set(firstEnd.AddHours(1));
        _ = await _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(id));
        _fixture.CurrentTenantId = null;

        var all = await _fixture.ListPayments.HandleAsync(new ListSubscriptionPaymentsQuery(SubscriptionId: id));

        Assert.Equal([39m, 29m], all.Select(payment => payment.Amount));
        Assert.All(all, payment => Assert.Equal(("Essentiel", "EUR", _fixture.TenantA.Value), (payment.PlanName, payment.CurrencyCode, payment.TenantId)));
        Assert.Equal((firstEnd, firstEnd.AddMonths(1)), (all[0].PaidFromUtc, all[0].PaidUntilUtc));
    }

    [Fact]
    public async Task A_refused_payment_leaves_no_trace_in_the_history()
    {
        var (_, id) = await ActiveAsync();

        _ = await Assert.ThrowsAsync<DomainException>(() => _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(id)));

        Assert.Single(_fixture.Payments);
    }

    [Fact]
    public async Task A_restaurant_sees_only_its_own_payments()
    {
        _ = await ActiveAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;
        _ = await ActiveAsync();
        _fixture.CurrentTenantId = _fixture.TenantA;

        var mine = await _fixture.ListMyPayments.HandleAsync(new ListMySubscriptionPaymentsQuery());

        Assert.Equal(_fixture.TenantA.Value, Assert.Single(mine).TenantId);
        Assert.Equal(2, _fixture.Payments.Count);
    }

    [Fact]
    public async Task The_payment_list_rejects_an_inverted_period()
    {
        var now = Clock.UtcNow;

        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.ListPayments.HandleAsync(new ListSubscriptionPaymentsQuery(From: now, To: now)));

        Assert.Equal(BillingApplicationErrors.PeriodInvalid, refused.Error);
    }

    [Fact]
    public async Task An_unconverted_trial_is_suspended_by_the_due_job()
    {
        var (_, id) = await TrialAsync();
        var early = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));
        Clock.Advance(_fixture.Settings.TrialLength);

        var outcome = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));
        var replay = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));
        var subscription = _fixture.SubscriptionById(id);

        Assert.Equal((SubscriptionDueOutcome.Nothing, SubscriptionDueOutcome.TrialExpired, SubscriptionDueOutcome.Nothing), (early, outcome, replay));
        Assert.Equal((SubscriptionStatus.Suspended, SuspensionReason.TrialExpired), (subscription.Status, subscription.SuspensionReason));
        Assert.Contains(subscription.DomainEvents, e => e is SubscriptionSuspended { Reason: SuspensionReason.TrialExpired });
    }

    [Fact]
    public async Task An_unpaid_period_becomes_past_due_then_suspended_after_the_grace_period_and_a_payment_restores_it()
    {
        var (_, id) = await ActiveAsync();
        var end = _fixture.SubscriptionById(id).CurrentPeriod.EndUtc;

        Clock.Set(end);
        var pastDue = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));
        Clock.Set(end + _fixture.Settings.PastDueGracePeriod - TimeSpan.FromMinutes(1));
        var inGrace = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));
        Clock.Set(end + _fixture.Settings.PastDueGracePeriod);
        var suspended = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));
        var paid = await _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(id));

        Assert.Equal(
            (SubscriptionDueOutcome.PastDue, SubscriptionDueOutcome.Nothing, SubscriptionDueOutcome.SuspendedForNonPayment),
            (pastDue, inGrace, suspended));
        // Pas de trou rétroactif : la période payée part de la fin de la précédente, qui contient maintenant.
        Assert.Equal((SubscriptionStatus.Active, end), (paid.Status, paid.PaidFromUtc));
        Assert.Null(_fixture.SubscriptionById(id).SuspensionReason);
    }

    [Fact]
    public async Task A_scheduled_cancellation_takes_effect_when_the_period_ends()
    {
        var (_, id) = await ActiveAsync();
        await _fixture.ScheduleCancellation.HandleAsync(new ScheduleSubscriptionCancellationCommand());
        Clock.Set(_fixture.SubscriptionById(id).CurrentPeriod.EndUtc);

        var outcome = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));

        Assert.Equal(SubscriptionDueOutcome.Cancelled, outcome);
        Assert.Contains(_fixture.SubscriptionById(id).DomainEvents, e => e is SubscriptionCancelled { Reason: CancellationReason.AtPeriodEnd });
        // Résilié : le restaurant peut repartir sur un nouvel abonnement.
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery()));
    }

    [Fact]
    public async Task The_due_job_only_acts_on_subscriptions_of_its_tenant()
    {
        var (_, id) = await TrialAsync();
        Clock.Advance(_fixture.Settings.TrialLength);
        _fixture.CurrentTenantId = _fixture.TenantB;

        var outcome = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));

        Assert.Equal(SubscriptionDueOutcome.Nothing, outcome);
        Assert.Equal(SubscriptionStatus.Trialing, _fixture.SubscriptionById(id).Status);
    }

    [Fact]
    public async Task The_platform_opens_a_subscription_for_a_known_restaurant_only()
    {
        var plan = await _fixture.PlanAsync();
        _fixture.CurrentTenantId = null;

        var id = await _fixture.AdminStart.HandleAsync(new AdminStartSubscriptionCommand(_fixture.TenantB.Value, plan, TrialDays: 30));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.AdminStart.HandleAsync(new AdminStartSubscriptionCommand(Guid.NewGuid(), plan)));
        var tooLong = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.AdminStart.HandleAsync(new AdminStartSubscriptionCommand(_fixture.TenantA.Value, plan, TrialDays: 91)));

        var subscription = _fixture.SubscriptionById(id);
        Assert.Equal((_fixture.TenantB, Clock.UtcNow.AddDays(30)), (subscription.TenantId, subscription.CurrentPeriod.EndUtc));
        Assert.Equal(BillingApplicationErrors.TrialLengthInvalid, tooLong.Error);
    }

    [Fact]
    public async Task The_platform_suspends_and_cancels_any_subscription_and_lists_them_all()
    {
        var (_, a) = await ActiveAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;
        var (_, b) = await TrialAsync();
        _fixture.CurrentTenantId = null;

        await _fixture.AdminSuspend.HandleAsync(new AdminSuspendSubscriptionCommand(a, SuspensionReason.PolicyViolation));
        await _fixture.AdminCancel.HandleAsync(new AdminCancelSubscriptionCommand(b));
        var all = await _fixture.ListSubscriptions.HandleAsync(new ListSubscriptionsQuery());
        var ofB = await _fixture.ListSubscriptions.HandleAsync(new ListSubscriptionsQuery(_fixture.TenantB.Value));
        var badPage = await Assert.ThrowsAsync<DomainException>(() => _fixture.ListSubscriptions.HandleAsync(new ListSubscriptionsQuery(Take: 500)));

        Assert.Equal(2, all.Count);
        Assert.Equal(SubscriptionStatus.Cancelled, Assert.Single(ofB).Status);
        Assert.Equal(SuspensionReason.PolicyViolation, (await _fixture.GetSubscription.HandleAsync(new GetSubscriptionQuery(a))).SuspensionReason);
        Assert.Equal(BillingApplicationErrors.PageInvalid, badPage.Error);
    }

    // --- Droits et consommation

    [Fact]
    public async Task Consumption_is_counted_per_month_and_blocked_beyond_the_plan_limit()
    {
        var plan = await _fixture.PlanAsync(entitlements: new[] { new EntitlementInput(BillingFeatures.OrdersMonthly, true, 2) });
        _ = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));

        var first = await _fixture.Consume.HandleAsync(new ConsumeFeatureCommand(BillingFeatures.OrdersMonthly));
        var second = await _fixture.Consume.HandleAsync(new ConsumeFeatureCommand(BillingFeatures.OrdersMonthly));
        var exceeded = await Assert.ThrowsAsync<DomainException>(() => _fixture.Consume.HandleAsync(new ConsumeFeatureCommand(BillingFeatures.OrdersMonthly)));
        var soft = await _fixture.Consume.HandleAsync(new ConsumeFeatureCommand(BillingFeatures.OrdersMonthly, Blocking: false));
        var usage = (await _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery())).Usage;

        Assert.Equal((1L, 2L, 3L), (first, second, soft));
        Assert.Equal(UsageCounterErrors.QuotaExceeded, exceeded.Error);
        Assert.Equal(new FeatureUsageDto(BillingFeatures.OrdersMonthly, true, 2, 3), Assert.Single(usage));
    }

    [Fact]
    public async Task The_counter_starts_again_at_the_next_month()
    {
        var plan = await _fixture.PlanAsync(entitlements: new[] { new EntitlementInput(BillingFeatures.OrdersMonthly, true, 1) });
        _ = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));
        _ = await _fixture.Consume.HandleAsync(new ConsumeFeatureCommand(BillingFeatures.OrdersMonthly));

        Clock.Set(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var nextMonth = await _fixture.Consume.HandleAsync(new ConsumeFeatureCommand(BillingFeatures.OrdersMonthly));

        Assert.Equal(1L, nextMonth);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), Assert.Single(_fixture.Counters).Period.StartUtc);
    }

    [Fact]
    public async Task A_feature_absent_from_the_plan_is_disabled_and_cannot_be_consumed()
    {
        _ = await TrialAsync();

        var whatsapp = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.NotificationsMonthly("WhatsApp")));
        var email = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.NotificationsMonthly("Email")));
        var sms = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.NotificationsSmsMonthly));
        var disabled = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Consume.HandleAsync(new ConsumeFeatureCommand(BillingFeatures.NotificationsWhatsAppMonthly)));

        Assert.False(whatsapp.Enabled);
        Assert.Equal((true, (long?)null), (email.Enabled, email.Limit));
        Assert.Equal((true, (long?)100, true), (sms.Enabled, sms.Limit, sms.HasLiveSubscription));
        Assert.Equal(UsageCounterErrors.FeatureDisabled, disabled.Error);
    }

    [Fact]
    public async Task A_restaurant_never_sees_the_subscription_nor_the_usage_of_another()
    {
        var (_, id) = await TrialAsync();
        _ = await _fixture.Consume.HandleAsync(new ConsumeFeatureCommand(BillingFeatures.NotificationsSmsMonthly));
        _fixture.CurrentTenantId = _fixture.TenantB;

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery()));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ScheduleCancellation.HandleAsync(new ScheduleSubscriptionCancellationCommand()));
        var limit = await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.NotificationsSmsMonthly));

        Assert.False(limit.Enabled);
        Assert.False(_fixture.SubscriptionById(id).CancelAtPeriodEnd);
        Assert.All(_fixture.Counters, counter => Assert.Equal(_fixture.TenantA, counter.TenantId));
    }

    [Fact]
    public async Task Without_a_restaurant_in_context_restaurant_use_cases_are_refused()
    {
        var plan = await _fixture.PlanAsync();
        _fixture.CurrentTenantId = null;

        await Assert.ThrowsAnyAsync<Exception>(() => _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan)));
        Assert.Empty(_fixture.Subscriptions);
    }
}
