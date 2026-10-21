using Billing.Application;
using Billing.Application.Abstractions;
using Billing.Domain;
using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Infrastructure;

/// <summary>
/// Persistance et repositories sur un vrai PostgreSQL (Docker) : plans globaux et leurs droits, abonnements et compteurs
/// isolés par tenant, lectures déclarées de la plateforme, gardes finales de la base (index uniques, version optimiste)
/// et outbox.
/// </summary>
public sealed class BillingPersistenceTests(BillingDatabase database) : IClassFixture<BillingDatabase>
{
    private FakeClock Clock => database.Clock;

    private static FeatureCode Feature(string code) => FeatureCode.Create(code);

    private async Task<Plan> PlanAsync(params Entitlement[] entitlements)
    {
        var plan = Plan.Create("Essentiel " + Guid.NewGuid().ToString("N")[..6], Money.Of(29m, Currency.FromCode("EUR")), BillingInterval.Monthly, entitlements, Clock);
        await database.SaveAsync(null, services =>
        {
            services.GetRequiredService<IPlanRepository>().Add(plan);
            return Task.CompletedTask;
        });
        return plan;
    }

    private async Task<SaasSubscription> TrialAsync(TenantId tenant, Plan plan, TimeSpan? length = null)
    {
        SaasSubscription subscription = null!;
        await database.SaveAsync(tenant, async services =>
        {
            var isFirst = await services.GetRequiredService<ILiveSubscriptionChecker>().HasNoLiveSubscriptionAsync(tenant);
            var now = Clock.UtcNow;
            subscription = SaasSubscription.StartTrial(tenant, plan.ToReference(), Period.Create(now, now + (length ?? TimeSpan.FromDays(14))), isFirst, Clock);
            services.GetRequiredService<ISubscriptionRepository>().Add(subscription);
        });
        return subscription;
    }

    [Fact]
    public async Task A_plan_and_its_entitlements_are_stored_and_read_back_whatever_the_tenant()
    {
        var plan = await PlanAsync(Entitlement.Limited(Feature("orders.monthly"), 500), Entitlement.Unlimited(Feature("notifications.email.monthly")));

        await using var scope = database.NewScope(TenantId.New());
        var loaded = await scope.ServiceProvider.GetRequiredService<IPlanRepository>().FindAsync(plan.Id);

        Assert.NotNull(loaded);
        Assert.Equal((29m, "EUR", PlanStatus.Active), (loaded.Price.Amount, loaded.Price.Currency.Code, loaded.Status));
        Assert.Equal(500, loaded.EntitlementFor(Feature("orders.monthly")).Limit);
        Assert.True(loaded.EntitlementFor(Feature("notifications.email.monthly")).IsUnlimited);
        Assert.False(loaded.EntitlementFor(Feature("staff.count")).Enabled);
    }

    [Fact]
    public async Task An_entitlement_is_replaced_and_the_plan_version_moves_with_it()
    {
        var plan = await PlanAsync(Entitlement.Limited(Feature("orders.monthly"), 500));
        await using var stale = database.NewScope(null);
        var staleContext = stale.ServiceProvider.GetRequiredService<BillingDbContext>();
        var stalePlan = await staleContext.Plans.SingleAsync(candidate => candidate.Id == plan.Id);

        await database.SaveAsync(null, async services =>
        {
            var loaded = (await services.GetRequiredService<IPlanRepository>().FindAsync(plan.Id))!;
            loaded.SetEntitlement(Entitlement.Limited(Feature("orders.monthly"), 1000), Clock);
        });
        stalePlan.SetEntitlement(Entitlement.Disabled(Feature("orders.monthly")), Clock);
        var conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => stale.ServiceProvider.GetRequiredService<IBillingUnitOfWork>().SaveChangesAsync());

        await using var scope = database.NewScope(null);
        var reloaded = await scope.ServiceProvider.GetRequiredService<IPlanRepository>().FindAsync(plan.Id);
        Assert.Equal(1000, reloaded!.EntitlementFor(Feature("orders.monthly")).Limit);
        Assert.Single(reloaded.Entitlements);
        Assert.Equal(BillingApplicationErrors.ConcurrentUpdate, conflict.Error);
    }

    [Fact]
    public async Task A_subscription_is_stored_with_its_period_and_seen_only_by_its_restaurant()
    {
        var plan = await PlanAsync();
        var tenant = TenantId.New();
        var subscription = await TrialAsync(tenant, plan);

        await using var own = database.NewScope(tenant);
        await using var other = database.NewScope(TenantId.New());
        var loaded = await own.ServiceProvider.GetRequiredService<ISubscriptionRepository>().FindLiveAsync(tenant);

        Assert.NotNull(loaded);
        Assert.Equal((subscription.Id, SubscriptionStatus.Trialing, plan.Id), (loaded.Id, loaded.Status, loaded.PlanId));
        Assert.Equal(subscription.CurrentPeriod, loaded.CurrentPeriod);
        // Même en donnant le bon tenant, le filtre du contexte d'un autre restaurant ne laisse rien passer.
        Assert.Null(await other.ServiceProvider.GetRequiredService<ISubscriptionRepository>().FindLiveAsync(tenant));
        Assert.Null(await other.ServiceProvider.GetRequiredService<ISubscriptionRepository>().FindAsync(tenant, subscription.Id));
    }

    [Fact]
    public async Task The_database_refuses_a_second_live_subscription_but_accepts_one_after_cancellation()
    {
        var plan = await PlanAsync();
        var tenant = TenantId.New();
        var first = await TrialAsync(tenant, plan);

        // Deux décisions prises en même temps : chacune a vu « aucun abonnement vivant ».
        var duplicate = await Assert.ThrowsAsync<DomainException>(() => database.SaveAsync(tenant, services =>
        {
            var now = Clock.UtcNow;
            services.GetRequiredService<ISubscriptionRepository>().Add(
                SaasSubscription.StartTrial(tenant, plan.ToReference(), Period.Create(now, now.AddDays(14)), tenantHasNoLiveSubscription: true, Clock));
            return Task.CompletedTask;
        }));
        await database.SaveAsync(tenant, async services =>
            (await services.GetRequiredService<ISubscriptionRepository>().FindAsync(tenant, first.Id))!.Cancel(Clock));
        var second = await TrialAsync(tenant, plan);

        Assert.Equal(SubscriptionErrors.AlreadySubscribed, duplicate.Error);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task The_platform_reads_the_subscriptions_of_every_restaurant()
    {
        var plan = await PlanAsync();
        var a = TenantId.New();
        var b = TenantId.New();
        var ofA = await TrialAsync(a, plan);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var ofB = await TrialAsync(b, plan);

        await using var scope = database.NewScope(null);
        var platform = scope.ServiceProvider.GetRequiredService<IPlatformSubscriptions>();

        Assert.Equal(a, (await platform.FindAsync(ofA.Id))!.TenantId);
        Assert.Equal([ofB.Id], (await platform.ListAsync(b, null, 0, 10)).Select(subscription => subscription.Id));
        Assert.Empty(await platform.ListAsync(a, SubscriptionStatus.Active, 0, 10));
        var all = await platform.ListAsync(null, SubscriptionStatus.Trialing, 0, 100);
        Assert.True(all.Select(subscription => subscription.Id).ToList().IndexOf(ofB.Id) < all.Select(subscription => subscription.Id).ToList().IndexOf(ofA.Id));
    }

    [Fact]
    public async Task A_payment_is_stored_seen_by_its_restaurant_and_listed_by_the_platform()
    {
        var plan = await PlanAsync();
        var a = TenantId.New();
        var b = TenantId.New();
        SubscriptionPayment paymentOfA = null!;
        foreach (var tenant in new[] { a, b })
        {
            var subscription = await TrialAsync(tenant, plan);
            await database.SaveAsync(tenant, async services =>
            {
                var loaded = (await services.GetRequiredService<ISubscriptionRepository>().FindAsync(tenant, subscription.Id))!;
                loaded.Activate(Period.Create(loaded.CurrentPeriod.EndUtc, loaded.CurrentPeriod.EndUtc.AddMonths(1)), Clock);
                var payment = SubscriptionPayment.Record(loaded, plan, Clock);
                services.GetRequiredService<ISubscriptionPaymentRepository>().Add(payment);
                if (tenant == a)
                {
                    paymentOfA = payment;
                }
            });
            Clock.Advance(TimeSpan.FromMinutes(1));
        }

        await using var scope = database.NewScope(a);
        var payments = scope.ServiceProvider.GetRequiredService<ISubscriptionPaymentRepository>();
        var mine = Assert.Single(await payments.ListAsync(a, 0, 10));
        var ofA = await payments.ListAllAsync(a, null, null, null, 0, 10);
        var recent = await payments.ListAllAsync(null, null, paymentOfA.RecordedAtUtc.AddSeconds(1), null, 0, 100);

        Assert.Equal((paymentOfA.Id, 29m, "EUR", plan.Name), (mine.Id, mine.Amount.Amount, mine.Amount.Currency.Code, mine.PlanName));
        Assert.Equal(paymentOfA.PaidPeriod, mine.PaidPeriod);
        Assert.Equal([paymentOfA.Id], ofA.Select(payment => payment.Id));
        Assert.DoesNotContain(recent, payment => payment.TenantId == a);
        Assert.Contains(recent, payment => payment.TenantId == b);
    }

    [Fact]
    public async Task The_due_list_holds_ended_trials_ended_paid_periods_and_overdue_payments_only()
    {
        var plan = await PlanAsync();
        var settings = new BillingSettings();
        var endedTrial = TenantId.New();
        var runningTrial = TenantId.New();
        var pastDueInGrace = TenantId.New();
        var pastDueOverdue = TenantId.New();
        var ended = await TrialAsync(endedTrial, plan, TimeSpan.FromDays(1));
        var inGrace = await TrialAsync(pastDueInGrace, plan, TimeSpan.FromDays(1));
        var overdue = await TrialAsync(pastDueOverdue, plan, TimeSpan.FromDays(1));

        // Les deux derniers : convertis, puis échus et marqués impayés à des dates différentes.
        await ActivateAndMarkPastDueAsync(pastDueOverdue, overdue.Id);
        Clock.Advance(settings.PastDueGracePeriod);
        await ActivateAndMarkPastDueAsync(pastDueInGrace, inGrace.Id);
        var running = await TrialAsync(runningTrial, plan, TimeSpan.FromDays(30));

        await using var scope = database.NewScope(null);
        var due = (await scope.ServiceProvider.GetRequiredService<IPlatformSubscriptions>().ListDueAsync(Clock.UtcNow, 1000))
            .Select(candidate => candidate.SubscriptionId)
            .ToList();

        Assert.Contains(ended.Id, due);
        Assert.Contains(overdue.Id, due);
        Assert.DoesNotContain(running.Id, due);
        Assert.DoesNotContain(inGrace.Id, due);
    }

    /// <summary>Paiement d'un mois (à la suite de l'essai s'il court encore, sinon à partir de maintenant), puis fin de ce mois et impayé.</summary>
    private async Task ActivateAndMarkPastDueAsync(TenantId tenant, SubscriptionId id)
    {
        await database.SaveAsync(tenant, async services =>
        {
            var subscription = (await services.GetRequiredService<ISubscriptionRepository>().FindAsync(tenant, id))!;
            var start = subscription.CurrentPeriod.HasEndedAt(Clock.UtcNow) ? Clock.UtcNow : subscription.CurrentPeriod.EndUtc;
            subscription.Activate(BillingPeriods.Starting(start, BillingInterval.Monthly), Clock);
        });
        await using (var scope = database.NewScope(tenant))
        {
            var subscription = (await scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>().FindAsync(tenant, id))!;
            Clock.Set(subscription.CurrentPeriod.EndUtc);
        }

        await database.SaveAsync(tenant, async services =>
            (await services.GetRequiredService<ISubscriptionRepository>().FindAsync(tenant, id))!.MarkPastDue(Clock));
    }

    [Fact]
    public async Task Two_simultaneous_changes_of_a_subscription_conflict()
    {
        var plan = await PlanAsync();
        var tenant = TenantId.New();
        var subscription = await TrialAsync(tenant, plan);
        await using var stale = database.NewScope(tenant);
        var staleCopy = (await stale.ServiceProvider.GetRequiredService<ISubscriptionRepository>().FindAsync(tenant, subscription.Id))!;

        await database.SaveAsync(tenant, async services =>
            (await services.GetRequiredService<ISubscriptionRepository>().FindAsync(tenant, subscription.Id))!.ScheduleCancellation(Clock));
        staleCopy.Suspend(SuspensionReason.Other, Clock);

        await Assert.ThrowsAsync<ConcurrencyException>(() => stale.ServiceProvider.GetRequiredService<IBillingUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task A_counter_is_unique_per_restaurant_and_feature_and_isolated()
    {
        var tenant = TenantId.New();
        var period = BillingFeatures.ConsumptionPeriod(Clock.UtcNow);
        var entitlement = Entitlement.Limited(Feature("orders.monthly"), 10);
        await database.SaveAsync(tenant, async services =>
        {
            var unique = await services.GetRequiredService<IUsageCounterUniquenessChecker>().IsUniqueAsync(tenant, entitlement.FeatureCode);
            var counter = UsageCounter.Open(tenant, entitlement.FeatureCode, period, unique);
            counter.Increment(3, entitlement, blocking: true, Clock);
            services.GetRequiredService<IUsageCounterRepository>().Add(counter);
        });

        var race = await Assert.ThrowsAsync<ConcurrencyException>(() => database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IUsageCounterRepository>().Add(UsageCounter.Open(tenant, entitlement.FeatureCode, period, isFirstCounterForFeature: true));
            return Task.CompletedTask;
        }));

        await using var own = database.NewScope(tenant);
        await using var other = database.NewScope(TenantId.New());
        var counters = own.ServiceProvider.GetRequiredService<IUsageCounterRepository>();
        Assert.Equal(3, (await counters.FindAsync(tenant, entitlement.FeatureCode))!.Used);
        Assert.False(await own.ServiceProvider.GetRequiredService<IUsageCounterUniquenessChecker>().IsUniqueAsync(tenant, entitlement.FeatureCode));
        Assert.Single(await counters.ListAsync(tenant));
        Assert.Null(await other.ServiceProvider.GetRequiredService<IUsageCounterRepository>().FindAsync(tenant, entitlement.FeatureCode));
        Assert.Equal(BillingApplicationErrors.ConcurrentUpdate, race.Error);
    }

    [Fact]
    public async Task Subscription_events_are_written_to_the_outbox_with_the_tenant()
    {
        var plan = await PlanAsync();
        var tenant = TenantId.New();
        var subscription = await TrialAsync(tenant, plan);

        await using var scope = database.NewScope(null);
        var messages = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().OutboxMessages.AsNoTracking()
            .Where(message => message.Type == nameof(SubscriptionTrialStarted))
            .ToListAsync();

        Assert.Contains(messages, message => message.Content.Contains(subscription.Id.Value.ToString(), StringComparison.Ordinal)
            && message.Content.Contains(tenant.Value.ToString(), StringComparison.Ordinal));
    }
}
