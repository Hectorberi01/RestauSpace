using Billing.Application;
using Billing.Application.Abstractions;
using Billing.Domain;

namespace Billing.Infrastructure.Persistence;

// Abonnements et compteurs : chaque requête filtre sur le tenant reçu, en plus du query filter du contexte. Les seules
// lectures sans filtre de tenant (IgnoreQueryFilters) sont celles de la plateforme, ci-dessous, et chacune le déclare.

internal sealed class PlanRepository(BillingDbContext context) : IPlanRepository
{
    public Task<Plan?> FindAsync(PlanId id, CancellationToken cancellationToken = default) =>
        context.Plans.SingleOrDefaultAsync(plan => plan.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Plan>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default)
    {
        var query = context.Plans.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(plan => plan.Status == PlanStatus.Active);
        }

        return await query.OrderBy(plan => plan.Name).ThenBy(plan => plan.Id).ToListAsync(cancellationToken);
    }

    public void Add(Plan plan) => context.Plans.Add(plan);
}

internal sealed class SubscriptionRepository(BillingDbContext context, ITenantContext tenantContext) : ISubscriptionRepository, ILiveSubscriptionChecker
{
    // Restaurant : filtre de son contexte (un autre restaurant ne voit rien). Plateforme (sans tenant) : le tenant donné.
    public Task<SaasSubscription?> FindLiveAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        (tenantContext.CurrentTenantId is null ? context.Subscriptions.IgnoreQueryFilters() : context.Subscriptions).SingleOrDefaultAsync(
            subscription => subscription.TenantId == tenantId && subscription.Status != SubscriptionStatus.Cancelled, cancellationToken);

    public Task<SaasSubscription?> FindAsync(TenantId tenantId, SubscriptionId id, CancellationToken cancellationToken = default) =>
        context.Subscriptions.SingleOrDefaultAsync(subscription => subscription.TenantId == tenantId && subscription.Id == id, cancellationToken);

    public void Add(SaasSubscription subscription) => context.Subscriptions.Add(subscription);

    /// <summary>Tenant explicite (plateforme comprise) : requête sans filtre de contexte.</summary>
    public async Task<bool> HasOtherAsync(TenantId tenantId, SubscriptionId? except, CancellationToken cancellationToken = default)
    {
        if (context.Subscriptions.Local.Any(subscription => subscription.TenantId == tenantId && subscription.Id != except))
        {
            return true;
        }

        var query = context.Subscriptions.IgnoreQueryFilters().Where(subscription => subscription.TenantId == tenantId);
        if (except is { } id)
        {
            query = query.Where(subscription => subscription.Id != id);
        }

        return await query.AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Plateforme comprise (un administrateur ouvre l'abonnement d'un restaurant, sans tenant dans son jeton) : le tenant
    /// est donné explicitement, d'où la requête sans filtre de contexte.
    /// </summary>
    public async Task<bool> HasNoLiveSubscriptionAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        !context.Subscriptions.Local.Any(subscription => subscription.TenantId == tenantId && subscription.Status != SubscriptionStatus.Cancelled)
        && !await context.Subscriptions
            .IgnoreQueryFilters()
            .AnyAsync(subscription => subscription.TenantId == tenantId && subscription.Status != SubscriptionStatus.Cancelled, cancellationToken);
}

/// <summary>Back-office de la plateforme : tous tenants, déclaré (IgnoreQueryFilters).</summary>
internal sealed class PlatformSubscriptions(BillingDbContext context, BillingSettings settings) : IPlatformSubscriptions
{
    public Task<SaasSubscription?> FindAsync(SubscriptionId id, CancellationToken cancellationToken = default) =>
        context.Subscriptions.IgnoreQueryFilters().SingleOrDefaultAsync(subscription => subscription.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SaasSubscription>> ListAsync(
        TenantId? tenantId, SubscriptionStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Subscriptions.IgnoreQueryFilters().AsNoTracking();
        if (tenantId is { } tenant)
        {
            query = query.Where(subscription => subscription.TenantId == tenant);
        }

        if (status is { } wanted)
        {
            query = query.Where(subscription => subscription.Status == wanted);
        }

        return await query
            .OrderByDescending(subscription => subscription.StartedAtUtc)
            .ThenBy(subscription => subscription.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DueSubscription>> ListDueAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default)
    {
        var graceEnd = nowUtc - settings.PastDueGracePeriod;
        return await context.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(subscription => subscription.Status != SubscriptionStatus.Cancelled && subscription.CurrentPeriod.EndUtc <= nowUtc)
            .Where(subscription =>
                subscription.CancelAtPeriodEnd
                || subscription.Status == SubscriptionStatus.Trialing
                || subscription.Status == SubscriptionStatus.Active
                || (subscription.Status == SubscriptionStatus.PastDue && subscription.CurrentPeriod.EndUtc <= graceEnd))
            .OrderBy(subscription => subscription.CurrentPeriod.EndUtc)
            .ThenBy(subscription => subscription.Id)
            .Select(subscription => new DueSubscription(subscription.TenantId, subscription.Id))
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DueSubscription>> ListEndingAsync(DateTimeOffset nowUtc, TimeSpan window, int take, CancellationToken cancellationToken = default)
    {
        var until = nowUtc + window;
        var unlimited = SaasSubscription.UnlimitedEndUtc;
        return await context.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(subscription => !subscription.CancelAtPeriodEnd
                && (subscription.Status == SubscriptionStatus.Trialing
                    || (subscription.Status == SubscriptionStatus.Active && subscription.IsComplimentary && subscription.CurrentPeriod.EndUtc < unlimited))
                && subscription.CurrentPeriod.EndUtc > nowUtc
                && subscription.CurrentPeriod.EndUtc <= until
                && (subscription.EndingNoticeSentForUtc == null || subscription.EndingNoticeSentForUtc != subscription.CurrentPeriod.EndUtc))
            .OrderBy(subscription => subscription.CurrentPeriod.EndUtc)
            .ThenBy(subscription => subscription.Id)
            .Select(subscription => new DueSubscription(subscription.TenantId, subscription.Id))
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}

internal sealed class OnlinePaymentRouteRepository(BillingDbContext context) : IOnlinePaymentRouteRepository
{
    public Task<OnlinePaymentRoute?> FindAsync(string currencyCode, CancellationToken cancellationToken = default) =>
        context.OnlinePaymentRoutes.SingleOrDefaultAsync(route => route.CurrencyCode == currencyCode, cancellationToken);

    public async Task<IReadOnlyList<OnlinePaymentRoute>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.OnlinePaymentRoutes.AsNoTracking().OrderBy(route => route.CurrencyCode).ToListAsync(cancellationToken);

    public void Add(OnlinePaymentRoute route) => context.OnlinePaymentRoutes.Add(route);

    public void Remove(OnlinePaymentRoute route) => context.OnlinePaymentRoutes.Remove(route);
}

/// <summary>Indicateurs du back-office : lectures tous tenants, déclarées (IgnoreQueryFilters), sans suivi.</summary>
internal sealed class BillingOverviewReader(BillingDbContext context) : IBillingOverviewReader
{
    public async Task<IReadOnlyList<SaasSubscription>> ListLiveAsync(CancellationToken cancellationToken = default) =>
        await context.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(subscription => subscription.Status != SubscriptionStatus.Cancelled)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaasSubscription>> ListStartedSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        await context.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(subscription => subscription.StartedAtUtc >= sinceUtc)
            .ToListAsync(cancellationToken);

    public Task<int> CountCancelledSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        context.Subscriptions.IgnoreQueryFilters()
            .CountAsync(subscription => subscription.Status == SubscriptionStatus.Cancelled && subscription.CancelledAtUtc >= sinceUtc, cancellationToken);

    public async Task<IReadOnlyList<OverviewPayment>> ListPaymentsSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        (await context.SubscriptionPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(payment => payment.RecordedAtUtc >= sinceUtc)
            .ToListAsync(cancellationToken))
        .Select(payment => new OverviewPayment(payment.TenantId.Value, payment.SubscriptionId.Value, payment.Amount.Amount, payment.Amount.Currency.Code, payment.RecordedAtUtc))
        .ToList();

    public async Task<IReadOnlyList<OverviewFailedCheckout>> ListFailedCheckoutsSinceAsync(DateTimeOffset sinceUtc, int take, CancellationToken cancellationToken = default) =>
        (await context.SubscriptionCheckouts.IgnoreQueryFilters().AsNoTracking()
            .Where(checkout => (checkout.Status == SubscriptionCheckoutStatus.Failed || checkout.Status == SubscriptionCheckoutStatus.Expired)
                && checkout.CreatedAtUtc >= sinceUtc)
            .OrderByDescending(checkout => checkout.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(checkout => new OverviewFailedCheckout(
            checkout.TenantId.Value, checkout.Amount.Amount, checkout.Amount.Currency.Code, checkout.Provider, checkout.Status, checkout.CreatedAtUtc))
        .ToList();

    public async Task<IReadOnlyList<(TenantAddOn AddOn, Plan Plan)>> ListActiveAddOnsAsync(CancellationToken cancellationToken = default)
    {
        var addOns = await context.TenantAddOns.IgnoreQueryFilters().AsNoTracking()
            .Where(addOn => addOn.Status == TenantAddOnStatus.Active)
            .ToListAsync(cancellationToken);
        var ids = addOns.Select(addOn => addOn.PlanId).Distinct().ToList();
        var plans = await context.Plans.AsNoTracking().Where(plan => ids.Contains(plan.Id)).ToDictionaryAsync(plan => plan.Id, cancellationToken);
        return addOns.Where(addOn => plans.ContainsKey(addOn.PlanId)).Select(addOn => (addOn, plans[addOn.PlanId])).ToList();
    }
}

internal sealed class UsageCounterRepository(BillingDbContext context) : IUsageCounterRepository, IUsageCounterUniquenessChecker
{
    public Task<UsageCounter?> FindAsync(TenantId tenantId, FeatureCode featureCode, CancellationToken cancellationToken = default) =>
        context.UsageCounters.SingleOrDefaultAsync(counter => counter.TenantId == tenantId && counter.FeatureCode == featureCode, cancellationToken);

    public async Task<IReadOnlyList<UsageCounter>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.UsageCounters.AsNoTracking().Where(counter => counter.TenantId == tenantId).ToListAsync(cancellationToken);

    public void Add(UsageCounter counter) => context.UsageCounters.Add(counter);

    public async Task<bool> IsUniqueAsync(TenantId tenantId, FeatureCode featureCode, CancellationToken cancellationToken = default) =>
        !context.UsageCounters.Local.Any(counter => counter.TenantId == tenantId && counter.FeatureCode == featureCode)
        && !await context.UsageCounters.AnyAsync(counter => counter.TenantId == tenantId && counter.FeatureCode == featureCode, cancellationToken);
}

internal sealed class SubscriptionPaymentRepository(BillingDbContext context) : ISubscriptionPaymentRepository
{
    public void Add(SubscriptionPayment payment) => context.SubscriptionPayments.Add(payment);

    public Task<SubscriptionPayment?> FindAsync(TenantId tenantId, SubscriptionPaymentId id, CancellationToken cancellationToken = default) =>
        context.SubscriptionPayments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(payment => payment.TenantId == tenantId && payment.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SubscriptionPayment>> ListAsync(TenantId tenantId, int skip, int take, CancellationToken cancellationToken = default) =>
        await context.SubscriptionPayments
            .AsNoTracking()
            .Where(payment => payment.TenantId == tenantId)
            .OrderByDescending(payment => payment.RecordedAtUtc)
            .ThenBy(payment => payment.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <summary>Back-office de la plateforme : tous tenants, déclaré (IgnoreQueryFilters).</summary>
    public async Task<IReadOnlyList<SubscriptionPayment>> ListAllAsync(
        TenantId? tenantId, SubscriptionId? subscriptionId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int skip, int take,
        CancellationToken cancellationToken = default)
    {
        var query = context.SubscriptionPayments.IgnoreQueryFilters().AsNoTracking();
        if (tenantId is { } tenant)
        {
            query = query.Where(payment => payment.TenantId == tenant);
        }

        if (subscriptionId is { } subscription)
        {
            query = query.Where(payment => payment.SubscriptionId == subscription);
        }

        if (fromUtc is { } from)
        {
            query = query.Where(payment => payment.RecordedAtUtc >= from);
        }

        if (toUtc is { } to)
        {
            query = query.Where(payment => payment.RecordedAtUtc < to);
        }

        return await query
            .OrderByDescending(payment => payment.RecordedAtUtc)
            .ThenBy(payment => payment.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Les options souscrites. Le tenant est toujours explicite (contexte côté restaurant, désigné par l'administrateur côté
/// plateforme), d'où la lecture sans filtre de contexte, déclarée ici (IgnoreQueryFilters) et toujours restreinte au tenant.
/// </summary>
internal sealed class TenantAddOnRepository(BillingDbContext context) : ITenantAddOnRepository
{
    public Task<TenantAddOn?> FindAsync(TenantId tenantId, TenantAddOnId id, CancellationToken cancellationToken = default) =>
        context.TenantAddOns.IgnoreQueryFilters().SingleOrDefaultAsync(addOn => addOn.TenantId == tenantId && addOn.Id == id, cancellationToken);

    public async Task<TenantAddOn?> FindActiveAsync(TenantId tenantId, PlanId planId, CancellationToken cancellationToken = default) =>
        context.TenantAddOns.Local.FirstOrDefault(addOn => addOn.TenantId == tenantId && addOn.PlanId == planId && addOn.Status == TenantAddOnStatus.Active)
        ?? await context.TenantAddOns.IgnoreQueryFilters()
            .SingleOrDefaultAsync(addOn => addOn.TenantId == tenantId && addOn.PlanId == planId && addOn.Status == TenantAddOnStatus.Active, cancellationToken);

    public async Task<IReadOnlyList<TenantAddOn>> ListAsync(TenantId tenantId, bool activeOnly, CancellationToken cancellationToken = default) =>
        await context.TenantAddOns.IgnoreQueryFilters().AsNoTracking()
            .Where(addOn => addOn.TenantId == tenantId && (!activeOnly || addOn.Status == TenantAddOnStatus.Active))
            .OrderBy(addOn => addOn.ActivatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TenantAddOn>> ListActiveForPlatformAsync(int skip, int take, CancellationToken cancellationToken = default) =>
        await context.TenantAddOns.IgnoreQueryFilters().AsNoTracking()
            .Where(addOn => addOn.Status == TenantAddOnStatus.Active)
            .OrderByDescending(addOn => addOn.ActivatedAtUtc)
            .ThenBy(addOn => addOn.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(TenantId TenantId, TenantAddOnId Id)>> ListComplimentaryEndedAsync(
        DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default)
    {
        var rows = await context.TenantAddOns.IgnoreQueryFilters().AsNoTracking()
            .Where(addOn => addOn.Status == TenantAddOnStatus.Active && addOn.ComplimentaryUntilUtc != null && addOn.ComplimentaryUntilUtc <= nowUtc)
            .OrderBy(addOn => addOn.ComplimentaryUntilUtc)
            .Take(take)
            .Select(addOn => new { addOn.TenantId, addOn.Id })
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.TenantId, row.Id)).ToList();
    }

    public void Add(TenantAddOn addOn) => context.TenantAddOns.Add(addOn);
}

/// <summary>
/// Les règlements en ligne. Tenant toujours explicite (contexte de la requête, ou désigné par le job), d'où la lecture
/// sans filtre de contexte, déclarée ici et toujours restreinte au tenant ; la liste du job seule parcourt tous les tenants.
/// </summary>
internal sealed class SubscriptionCheckoutRepository(BillingDbContext context) : ISubscriptionCheckoutRepository
{
    private IQueryable<SubscriptionCheckout> Checkouts => context.SubscriptionCheckouts.IgnoreQueryFilters();

    public Task<SubscriptionCheckout?> FindAsync(TenantId tenantId, SubscriptionCheckoutId id, CancellationToken cancellationToken = default) =>
        Checkouts.SingleOrDefaultAsync(checkout => checkout.TenantId == tenantId && checkout.Id == id, cancellationToken);

    public Task<SubscriptionCheckout?> FindPendingAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Checkouts
            .Where(checkout => checkout.TenantId == tenantId && checkout.Status == SubscriptionCheckoutStatus.Pending)
            .OrderByDescending(checkout => checkout.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<(TenantId TenantId, SubscriptionCheckoutId Id)>> ListPendingAsync(int take, CancellationToken cancellationToken = default)
    {
        var rows = await Checkouts.AsNoTracking()
            .Where(checkout => checkout.Status == SubscriptionCheckoutStatus.Pending)
            .OrderBy(checkout => checkout.CreatedAtUtc)
            .Take(take)
            .Select(checkout => new { checkout.TenantId, checkout.Id })
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.TenantId, row.Id)).ToList();
    }

    public async Task<(TenantId TenantId, SubscriptionCheckoutId Id)?> FindPendingByReferenceAsync(
        string provider, string providerReference, CancellationToken cancellationToken = default)
    {
        var row = await Checkouts.AsNoTracking()
            .Where(checkout => checkout.Status == SubscriptionCheckoutStatus.Pending
                && checkout.Provider == provider && checkout.ProviderReference == providerReference)
            .Select(checkout => new { checkout.TenantId, checkout.Id })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.TenantId, row.Id);
    }

    public void Add(SubscriptionCheckout checkout) => context.SubscriptionCheckouts.Add(checkout);
}

/// <summary>Sans prestataire branché par l'hôte : le paiement en ligne des abonnements n'est pas proposé.</summary>
internal sealed class NoSubscriptionCollector : ISubscriptionCollector
{
    public string? DefaultProvider => null;

    public Task<bool> IsReadyAsync(string provider, Currency currency, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<CollectionStart> StartAsync(
        string provider, TenantId tenantId, SubscriptionCheckoutId checkoutId, Money amount, string label, string? payerPhone,
        CancellationToken cancellationToken = default) =>
        throw new DomainException(BillingApplicationErrors.OnlinePaymentUnavailable);

    public Task<CollectionStatus> StatusAsync(string provider, string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CollectionStatus(CollectionState.Pending, null));
}

internal sealed class InvoiceIssuerFromOptions(BillingOptions options) : IInvoiceIssuer
{
    public InvoiceIssuer Current { get; } = new(
        options.InvoiceIssuer.Name, options.InvoiceIssuer.Address, options.InvoiceIssuer.TaxId, options.InvoiceIssuer.RegistrationNumber,
        options.InvoiceIssuer.Email, options.InvoiceIssuer.Footer);
}
