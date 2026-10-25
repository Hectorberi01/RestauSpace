using Billing.Application.Abstractions;
using Billing.Application.Commands;
using Billing.Application.Queries;
using Billing.Domain;

namespace Billing.Application;

public sealed class ComplimentaryTests
{
    private readonly BillingFixture _fixture = new();

    private FakeClock Clock => _fixture.Clock;

    private static async Task ThrowsAsync(DomainError expected, Func<Task> action) =>
        Assert.Equal(expected.Code, (await Assert.ThrowsAsync<DomainException>(action)).Code);

    private SaasSubscription Live(TenantId tenant) =>
        _fixture.Subscriptions.Single(subscription => subscription.TenantId == tenant && subscription.Status != SubscriptionStatus.Cancelled);

    private Task<Guid> GrantAsync(Guid plan, DateTimeOffset? until, TenantId? tenant = null)
    {
        _fixture.CurrentTenantId = null;
        return _fixture.Grant.HandleAsync(new AdminGrantComplimentaryCommand((tenant ?? _fixture.TenantA).Value, plan, until));
    }

    [Fact]
    public async Task An_offer_for_a_period_costs_nothing_until_it_ends_then_the_restaurant_pays()
    {
        var basic = await _fixture.PlanAsync();
        var premium = await _fixture.PlanAsync("Premium", 79m);
        await _fixture.Start.HandleAsync(new StartSubscriptionCommand(basic));
        var until = Clock.UtcNow.AddMonths(3);

        var id = await GrantAsync(premium, until);
        _fixture.CurrentTenantId = _fixture.TenantA;
        var mine = await _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery());
        var due = await _fixture.OnlinePayment.HandleAsync(new GetOnlinePaymentQuery());

        Assert.Equal((SubscriptionStatus.Active, true, until, premium), (mine.Subscription.Status, mine.Subscription.IsComplimentary, mine.Subscription.ComplimentaryUntilUtc, mine.Plan.Id));
        Assert.False(due.Due);
        await ThrowsAsync(SubscriptionErrors.Complimentary, () => _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(basic)));

        Clock.Set(until.AddMinutes(1));
        Assert.Equal(SubscriptionDueOutcome.PastDue, await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id)));
        var after = await _fixture.OnlinePayment.HandleAsync(new GetOnlinePaymentQuery());
        Assert.False(_fixture.SubscriptionById(id).IsComplimentary);
        Assert.Equal((true, 79m), (after.Due, after.Amount));
    }

    [Fact]
    public async Task An_unlimited_offer_opens_a_subscription_never_ends_and_keeps_options_with_the_platform()
    {
        var plan = await _fixture.PlanAsync();
        var site = await _fixture.CreatePlan.HandleAsync(new CreatePlanCommand(
            "Application mobile", 5m, "EUR", BillingInterval.Monthly, [new EntitlementInput(BillingFeatures.StorefrontMobileApp, true)], PlanKind.AddOn));

        var id = await GrantAsync(plan, null);
        var subscription = _fixture.SubscriptionById(id);
        Clock.Advance(TimeSpan.FromDays(4000));
        _fixture.CurrentTenantId = _fixture.TenantA;

        Assert.True(subscription.IsComplimentaryUnlimited);
        Assert.Null(SubscriptionDto.From(subscription).ComplimentaryUntilUtc);
        Assert.Equal(SubscriptionDueOutcome.Nothing, await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id)));
        await ThrowsAsync(SubscriptionErrors.Complimentary, () => _fixture.ScheduleCancellation.HandleAsync(new ScheduleSubscriptionCancellationCommand()));
        await ThrowsAsync(BillingApplicationErrors.AddOnComplimentaryUnlimited, () => _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(site)));
    }

    [Fact]
    public async Task An_offer_restores_a_suspended_subscription_and_ending_it_brings_back_the_due_payment()
    {
        var plan = await _fixture.PlanAsync();
        var id = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));
        Clock.Advance(TimeSpan.FromDays(15));
        await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));
        Assert.Equal(SubscriptionStatus.Suspended, _fixture.SubscriptionById(id).Status);

        await GrantAsync(plan, Clock.UtcNow.AddDays(30));
        Assert.Equal((SubscriptionStatus.Active, (SuspensionReason?)null), (_fixture.SubscriptionById(id).Status, _fixture.SubscriptionById(id).SuspensionReason));

        await _fixture.EndOffer.HandleAsync(new AdminEndComplimentaryCommand(id));
        Clock.Advance(TimeSpan.FromMinutes(1));
        _fixture.CurrentTenantId = _fixture.TenantA;

        Assert.False(_fixture.SubscriptionById(id).IsComplimentary);
        Assert.Equal(SubscriptionDueOutcome.PastDue, await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id)));
        await ThrowsAsync(SubscriptionErrors.NotComplimentary, () => _fixture.EndOffer.HandleAsync(new AdminEndComplimentaryCommand(id)));
    }

    [Fact]
    public async Task An_offer_needs_a_known_restaurant_a_base_plan_of_its_currency_and_a_future_end()
    {
        var plan = await _fixture.PlanAsync();
        var xof = await _fixture.CreatePlan.HandleAsync(new CreatePlanCommand("Essentiel", 19000m, "XOF", BillingInterval.Monthly, []));

        await Assert.ThrowsAsync<NotFoundException>(() => GrantAsync(plan, null, TenantId.New()));
        await ThrowsAsync(BillingApplicationErrors.PlanCurrencyMismatch, () => GrantAsync(xof, null));
        await ThrowsAsync(SubscriptionErrors.ComplimentaryEndInvalid, () => GrantAsync(plan, Clock.UtcNow.AddDays(-1)));
    }

    [Fact]
    public async Task The_restaurant_is_told_once_before_its_trial_or_its_offer_ends()
    {
        var plan = await _fixture.PlanAsync();
        var trial = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));
        _fixture.Writable[_fixture.TenantB] = true;
        var offer = await GrantAsync(plan, Clock.UtcNow.AddDays(20), _fixture.TenantB);
        var platform = (IPlatformSubscriptions)_fixture;

        Assert.Empty(await platform.ListEndingAsync(Clock.UtcNow, SaasSubscription.EndingNoticeWindow, 10));
        Clock.Advance(TimeSpan.FromDays(8));
        var ending = await platform.ListEndingAsync(Clock.UtcNow, SaasSubscription.EndingNoticeWindow, 10);
        _fixture.CurrentTenantId = _fixture.TenantA;
        var first = await _fixture.NoticeEnding.HandleAsync(new NoticeSubscriptionEndingCommand(trial));
        var again = await _fixture.NoticeEnding.HandleAsync(new NoticeSubscriptionEndingCommand(trial));

        Assert.Equal([trial], ending.Select(entry => entry.SubscriptionId.Value));
        Assert.Equal((true, false), (first, again));
        Assert.True(DomainAssert.Raised<SubscriptionEndingSoon>(_fixture.SubscriptionById(trial)).EndsAtUtc > Clock.UtcNow);

        Clock.Advance(TimeSpan.FromDays(6));
        _fixture.CurrentTenantId = _fixture.TenantB;
        Assert.True(await _fixture.NoticeEnding.HandleAsync(new NoticeSubscriptionEndingCommand(offer)));
        Assert.True(DomainAssert.Raised<SubscriptionEndingSoon>(_fixture.SubscriptionById(offer)).Complimentary);
    }

    [Fact]
    public async Task Each_currency_is_collected_by_the_provider_the_platform_chose_when_it_is_ready()
    {
        _fixture.Currencies[_fixture.TenantA] = Currency.XOF;
        var plan = await _fixture.CreatePlan.HandleAsync(new CreatePlanCommand("Essentiel", 19000m, "XOF", BillingInterval.Monthly, []));
        await _fixture.PlanAsync();
        _fixture.NotReady.Add("stripe:XOF");

        await ThrowsAsync(BillingApplicationErrors.OnlinePaymentProviderNotReady, () => _fixture.SetRoute.HandleAsync(new SetOnlinePaymentRouteCommand("XOF", ["stripe"])));
        await _fixture.SetRoute.HandleAsync(new SetOnlinePaymentRouteCommand("xof", ["KkiaPay"]));
        var routes = await _fixture.ListRoutes.HandleAsync(new ListOnlinePaymentRoutesQuery());
        await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));
        var started = await _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand());

        Assert.Equal([("EUR", "fedapay", false, true), ("XOF", "kkiapay", true, true)], routes.Select(route => (route.CurrencyCode, route.Provider, route.Chosen, route.Ready)));
        Assert.Equal("kkiapay", started.Provider);

        await _fixture.SetRoute.HandleAsync(new SetOnlinePaymentRouteCommand("XOF", []));
        Assert.Empty(_fixture.Routes);
        await ThrowsAsync(BillingApplicationErrors.CurrencyInvalid, () => _fixture.SetRoute.HandleAsync(new SetOnlinePaymentRouteCommand("ZZZ", ["fedapay"])));
    }

    [Fact]
    public async Task The_restaurant_chooses_among_the_ready_providers_of_its_currency()
    {
        var plan = await _fixture.PlanAsync();
        await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));
        await _fixture.SetRoute.HandleAsync(new SetOnlinePaymentRouteCommand("EUR", ["Stripe", "paypal", "stripe"]));
        _fixture.CurrentTenantId = _fixture.TenantA;

        var offered = await _fixture.OnlinePayment.HandleAsync(new GetOnlinePaymentQuery());
        await ThrowsAsync(BillingApplicationErrors.OnlinePaymentProviderNotOffered, () => _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand(null, "fedapay")));
        var started = await _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand(null, "PayPal"));

        Assert.Equal(["stripe", "paypal"], Assert.Single(_fixture.Routes).Providers);
        Assert.Equal(["stripe", "paypal"], offered.Options!.Select(option => option.Provider));
        Assert.Equal("paypal", started.Provider);

        _fixture.NotReady.Add("stripe:EUR");
        var withoutStripe = await _fixture.OnlinePayment.HandleAsync(new GetOnlinePaymentQuery());
        Assert.Equal(("paypal", true), (withoutStripe.Provider, withoutStripe.Available));
        await ThrowsAsync(BillingApplicationErrors.OnlinePaymentProviderNotReady, () => _fixture.SetRoute.HandleAsync(new SetOnlinePaymentRouteCommand("EUR", ["stripe", "paypal"])));
    }

    [Fact]
    public async Task The_overview_counts_paying_subscriptions_without_offers_and_lists_what_needs_attention()
    {
        var plan = await _fixture.PlanAsync();
        var paying = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));
        _fixture.CurrentTenantId = null;
        await _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(paying));
        _fixture.Writable[_fixture.TenantB] = true;
        await GrantAsync(plan, null, _fixture.TenantB);
        var trialTenant = TenantId.New();
        _fixture.Writable[trialTenant] = true;
        _fixture.CurrentTenantId = trialTenant;
        await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));
        Clock.Advance(TimeSpan.FromDays(10));

        var overview = await _fixture.Overview.HandleAsync(new GetBillingOverviewQuery());

        Assert.Equal([new AmountDto("EUR", 29m)], overview.MonthlyRecurringRevenue);
        Assert.Equal((1, 1, 1, 1, 3, 1), (overview.Paying, overview.Trialing, overview.Complimentary, overview.ComplimentaryUnlimited, overview.TrialsStarted, overview.TrialsConverted));
        var attention = Assert.Single(overview.Attention);
        Assert.Equal((AttentionReason.TrialEnding, trialTenant.Value), (attention.Reason, attention.TenantId));
        Assert.Equal(29m, overview.CollectedThisMonth.Single().Amount);
    }
}
