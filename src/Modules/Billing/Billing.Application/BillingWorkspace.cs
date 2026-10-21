using Billing.Application.Abstractions;
using Billing.Application.Queries;
using Billing.Domain;

namespace Billing.Application;

/// <summary>
/// Étapes communes aux cas d'usage. Côté restaurant : le tenant vient du contexte (jamais de la requête), une écriture
/// exige un tenant qui les accepte. Côté plateforme : le tenant est désigné par l'administrateur et l'abonnement lu sans
/// filtre de contexte (<see cref="IPlatformSubscriptions"/>). Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class BillingWorkspace(
    ITenantContext tenantContext,
    IBillingTenants tenants,
    IPlanRepository plans,
    ISubscriptionRepository subscriptions,
    IPlatformSubscriptions platform,
    IUsageCounterRepository counters,
    ISubscriptionPaymentRepository payments,
    ITenantAddOnRepository addOns,
    ILiveSubscriptionChecker liveSubscriptions,
    IUsageCounterUniquenessChecker counterUniqueness,
    BillingSettings settings,
    IBillingUnitOfWork unitOfWork)
{
    public const int MaxTake = 100;

    public IPlanRepository Plans => plans;

    public ISubscriptionRepository Subscriptions => subscriptions;

    public IPlatformSubscriptions Platform => platform;

    public IUsageCounterRepository Counters => counters;

    public ISubscriptionPaymentRepository Payments => payments;

    public ITenantAddOnRepository AddOns => addOns;

    public ILiveSubscriptionChecker LiveSubscriptions => liveSubscriptions;

    public IUsageCounterUniquenessChecker CounterUniqueness => counterUniqueness;

    public IBillingTenants Tenants => tenants;

    public BillingSettings Settings => settings;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(BillingApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<Plan> LoadPlanAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await plans.FindAsync(PlanId.From(id), cancellationToken))
        ?? throw new NotFoundException(BillingApplicationErrors.PlanNotFound);

    /// <summary>Un plan n'est proposé qu'aux restaurants de sa devise.</summary>
    public async Task EnsurePlanCurrencyAsync(TenantId tenantId, Plan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var currency = await tenants.DefaultCurrencyAsync(tenantId, cancellationToken);
        DomainException.ThrowIf(currency is not null && currency != plan.Price.Currency, BillingApplicationErrors.PlanCurrencyMismatch);
    }

    /// <summary>L'abonnement vivant du restaurant du contexte (404 s'il n'en a pas).</summary>
    public async Task<SaasSubscription> LoadLiveSubscriptionAsync(CancellationToken cancellationToken) =>
        await subscriptions.FindLiveAsync(CurrentTenant, cancellationToken)
        ?? throw new NotFoundException(BillingApplicationErrors.SubscriptionNotFound);

    /// <summary>Plateforme : un abonnement de n'importe quel tenant.</summary>
    public async Task<SaasSubscription> LoadAnySubscriptionAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await platform.FindAsync(SubscriptionId.From(id), cancellationToken))
        ?? throw new NotFoundException(BillingApplicationErrors.SubscriptionNotFound);

    /// <summary>Le droit du tenant pour une fonctionnalité : celui du plan de son abonnement vivant, sinon désactivé.</summary>
    public async Task<Entitlement> EntitlementAsync(TenantId tenantId, FeatureCode feature, CancellationToken cancellationToken)
    {
        var subscription = await subscriptions.FindLiveAsync(tenantId, cancellationToken);
        if (subscription is null)
        {
            return Entitlement.Disabled(feature);
        }

        var plan = await plans.FindAsync(subscription.PlanId, cancellationToken);
        var fromPlan = plan?.EntitlementFor(feature) ?? Entitlement.Disabled(feature);
        var fromAddOns = (await ActiveAddOnPlansAsync(tenantId, cancellationToken))
            .Select(item => (item.Plan.EntitlementFor(feature), item.AddOn.Quantity));
        return TenantAddOn.Combine(fromPlan, fromAddOns);
    }

    /// <summary>Ce que coûte une période du plan et des options actives du tenant.</summary>
    public async Task<Money> ChargeAsync(Plan plan, TenantId tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return SubscriptionCharge.Total(plan, SubscriptionCharge.Lines(plan, await ActiveAddOnPlansAsync(tenantId, cancellationToken)));
    }

    /// <summary>Les options actives du tenant, avec leur plan d'option.</summary>
    public async Task<IReadOnlyList<(TenantAddOn AddOn, Plan Plan)>> ActiveAddOnPlansAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var result = new List<(TenantAddOn, Plan)>();
        foreach (var addOn in await addOns.ListAsync(tenantId, activeOnly: true, cancellationToken))
        {
            if (await plans.FindAsync(addOn.PlanId, cancellationToken) is { } plan)
            {
                result.Add((addOn, plan));
            }
        }

        return result;
    }

    /// <summary>Ce que coûtera la prochaine période payée de <paramref name="subscription"/> (plan et options actives, prix actuels).</summary>
    public async Task<SubscriptionChargeDto> ChargeAsync(SaasSubscription subscription, Plan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(plan);
        var addOns = await ActiveAddOnPlansAsync(subscription.TenantId, cancellationToken);
        var lines = SubscriptionCharge.Lines(plan, addOns);
        return new SubscriptionChargeDto(
            subscription.Id.Value,
            subscription.TenantId.Value,
            plan.Price.Currency.Code,
            plan.BillingInterval,
            lines.Select(ChargeLineDto.From).ToList(),
            SubscriptionCharge.Total(plan, lines).Amount,
            SubscriptionCharge.Monthly(plan, addOns).Amount);
    }

    /// <summary>L'abonnement est résilié : ses options le sont aussi (elles ne survivent pas à un nouvel abonnement).</summary>
    public async Task CancelAddOnsAsync(TenantId tenantId, IClock clock, CancellationToken cancellationToken)
    {
        foreach (var listed in await addOns.ListAsync(tenantId, activeOnly: true, cancellationToken))
        {
            if (await addOns.FindAsync(tenantId, listed.Id, cancellationToken) is { } tracked)
            {
                tracked.Cancel(clock);
            }
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);

    public static void EnsurePage(int skip, int take) =>
        DomainException.ThrowIf(skip < 0 || take is < 1 or > MaxTake, BillingApplicationErrors.PageInvalid);
}
