using Billing.Domain;

namespace Billing.Application.Queries;

// Lectures. Côté restaurant : tenant du contexte, possibles même quand le restaurant est suspendu. Côté plateforme :
// catalogue complet et abonnements de tous les restaurants.

/// <summary>
/// Plateforme : le catalogue, archivés compris si <paramref name="ActiveOnly"/> est faux, restreint au besoin à une
/// devise (code ISO 4217).
/// </summary>
public sealed record ListPlansQuery(bool ActiveOnly = true, string? CurrencyCode = null) : IQuery<IReadOnlyList<PlanDto>>;

public sealed class ListPlansHandler(BillingWorkspace workspace) : IQueryHandler<ListPlansQuery, IReadOnlyList<PlanDto>>
{
    public async Task<IReadOnlyList<PlanDto>> HandleAsync(ListPlansQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var currency = string.IsNullOrWhiteSpace(query.CurrencyCode) ? null : Currency.FromCode(query.CurrencyCode.Trim().ToUpperInvariant());
        return (await workspace.Plans.ListAsync(query.ActiveOnly, cancellationToken))
            .Where(plan => currency is null || plan.Price.Currency == currency)
            .Select(PlanDto.From)
            .ToList();
    }
}

/// <summary>Restaurant : les plans de base actifs vendus dans sa devise (aucun pour un restaurant inconnu).</summary>
public sealed record ListMyPlansQuery : IQuery<IReadOnlyList<PlanDto>>;

public sealed class ListMyPlansHandler(BillingWorkspace workspace) : IQueryHandler<ListMyPlansQuery, IReadOnlyList<PlanDto>>
{
    public async Task<IReadOnlyList<PlanDto>> HandleAsync(ListMyPlansQuery query, CancellationToken cancellationToken = default)
    {
        var currency = await workspace.Tenants.DefaultCurrencyAsync(workspace.CurrentTenant, cancellationToken);
        if (currency is null)
        {
            return [];
        }

        return (await workspace.Plans.ListAsync(activeOnly: true, cancellationToken))
            .Where(plan => plan.Price.Currency == currency && plan.Kind == PlanKind.Base)
            .Select(PlanDto.From)
            .ToList();
    }
}

/// <summary>Restaurant : les options proposées dans sa devise et celles qu'il a souscrites (actives).</summary>
public sealed record ListMyAddOnsQuery : IQuery<MyAddOnsDto>;

public sealed class ListMyAddOnsHandler(BillingWorkspace workspace) : IQueryHandler<ListMyAddOnsQuery, MyAddOnsDto>
{
    public async Task<MyAddOnsDto> HandleAsync(ListMyAddOnsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var currency = await workspace.Tenants.DefaultCurrencyAsync(tenant, cancellationToken);
        var offered = currency is null
            ? []
            : (await workspace.Plans.ListAsync(activeOnly: true, cancellationToken))
                .Where(plan => plan.Price.Currency == currency && plan.Kind == PlanKind.AddOn)
                .Select(PlanDto.From)
                .ToList();
        var active = (await workspace.ActiveAddOnPlansAsync(tenant, cancellationToken))
            .Select(item => TenantAddOnDto.From(item.AddOn, item.Plan))
            .ToList();
        return new MyAddOnsDto(offered, active);
    }
}

/// <summary>Plateforme : les options d'un restaurant, résiliées comprises.</summary>
public sealed record ListTenantAddOnsQuery(Guid TenantId) : IQuery<IReadOnlyList<TenantAddOnDto>>;

public sealed class ListTenantAddOnsHandler(BillingWorkspace workspace) : IQueryHandler<ListTenantAddOnsQuery, IReadOnlyList<TenantAddOnDto>>
{
    public async Task<IReadOnlyList<TenantAddOnDto>> HandleAsync(ListTenantAddOnsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = await Commands.TenantAddOns.RequireTenantAsync(workspace, query.TenantId, cancellationToken);
        var result = new List<TenantAddOnDto>();
        foreach (var addOn in await workspace.AddOns.ListAsync(tenant, activeOnly: false, cancellationToken))
        {
            if (await workspace.Plans.FindAsync(addOn.PlanId, cancellationToken) is { } plan)
            {
                result.Add(TenantAddOnDto.From(addOn, plan));
            }
        }

        return result;
    }
}

/// <summary>Plateforme : ce que coûtera la prochaine période payée d'un abonnement (plan programmé compris, et options actives).</summary>
public sealed record GetSubscriptionChargeQuery(Guid SubscriptionId) : IQuery<SubscriptionChargeDto>;

public sealed class GetSubscriptionChargeHandler(BillingWorkspace workspace) : IQueryHandler<GetSubscriptionChargeQuery, SubscriptionChargeDto>
{
    public async Task<SubscriptionChargeDto> HandleAsync(GetSubscriptionChargeQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var subscription = await workspace.LoadAnySubscriptionAsync(query.SubscriptionId, cancellationToken);
        return await workspace.ChargeAsync(subscription, await workspace.LoadNextPeriodPlanAsync(subscription, cancellationToken), cancellationToken);
    }
}

/// <summary>Plateforme : les options actives de tous les restaurants (vente en ligne, bornes…), avec leur plan d'option.</summary>
public sealed record ListActiveAddOnsQuery(int Skip = 0, int Take = 50) : IQuery<IReadOnlyList<TenantAddOnDto>>;

public sealed class ListActiveAddOnsHandler(BillingWorkspace workspace) : IQueryHandler<ListActiveAddOnsQuery, IReadOnlyList<TenantAddOnDto>>
{
    public async Task<IReadOnlyList<TenantAddOnDto>> HandleAsync(ListActiveAddOnsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        BillingWorkspace.EnsurePage(query.Skip, query.Take);
        var plans = new Dictionary<PlanId, Plan?>();
        var result = new List<TenantAddOnDto>();
        foreach (var addOn in await workspace.AddOns.ListActiveForPlatformAsync(query.Skip, query.Take, cancellationToken))
        {
            if (!plans.TryGetValue(addOn.PlanId, out var plan))
            {
                plan = await workspace.Plans.FindAsync(addOn.PlanId, cancellationToken);
                plans[addOn.PlanId] = plan;
            }

            if (plan is not null)
            {
                result.Add(TenantAddOnDto.From(addOn, plan));
            }
        }

        return result;
    }
}

public sealed record GetPlanQuery(Guid PlanId) : IQuery<PlanDto>;

public sealed class GetPlanHandler(BillingWorkspace workspace) : IQueryHandler<GetPlanQuery, PlanDto>
{
    public async Task<PlanDto> HandleAsync(GetPlanQuery query, CancellationToken cancellationToken = default) =>
        PlanDto.From(await workspace.LoadPlanAsync(query.PlanId, cancellationToken));
}

/// <summary>L'abonnement vivant du restaurant, son plan, sa consommation du mois et son changement programmé (404 sans abonnement).</summary>
public sealed record GetMySubscriptionQuery : IQuery<MySubscriptionDto>;

public sealed class GetMySubscriptionHandler(BillingWorkspace workspace, IClock clock) : IQueryHandler<GetMySubscriptionQuery, MySubscriptionDto>
{
    public async Task<MySubscriptionDto> HandleAsync(GetMySubscriptionQuery query, CancellationToken cancellationToken = default)
    {
        var subscription = await workspace.LoadLiveSubscriptionAsync(cancellationToken);
        var plan = await workspace.Plans.FindAsync(subscription.PlanId, cancellationToken)
            ?? throw new NotFoundException(BillingApplicationErrors.PlanNotFound);
        var period = BillingFeatures.ConsumptionPeriod(clock.UtcNow);
        var used = (await workspace.Counters.ListAsync(subscription.TenantId, cancellationToken))
            .Where(counter => counter.Period == period)
            .ToDictionary(counter => counter.FeatureCode.Value, counter => counter.Used, StringComparer.Ordinal);

        var addOns = await workspace.ActiveAddOnPlansAsync(subscription.TenantId, cancellationToken);
        var usage = plan.Entitlements.Select(entitlement => entitlement.FeatureCode)
            .Concat(addOns.SelectMany(item => item.Plan.Entitlements.Select(entitlement => entitlement.FeatureCode)))
            .Distinct()
            .OrderBy(feature => feature.Value, StringComparer.Ordinal)
            .Select(feature => TenantAddOn.Combine(
                plan.EntitlementFor(feature), addOns.Select(item => (item.Plan.EntitlementFor(feature), item.AddOn.Quantity))))
            .Select(entitlement => new FeatureUsageDto(
                entitlement.FeatureCode.Value, entitlement.Enabled, entitlement.Limit, used.GetValueOrDefault(entitlement.FeatureCode.Value)))
            .ToList();
        var next = await workspace.LoadNextPeriodPlanAsync(subscription, cancellationToken);
        var scheduled = subscription.ScheduledPlanEffectiveAtUtc is { } effectiveAt ? new ScheduledPlanChangeDto(next.Id.Value, next.Name, effectiveAt) : null;
        return new MySubscriptionDto(
            SubscriptionDto.From(subscription), PlanDto.From(plan), usage, await workspace.ChargeAsync(subscription, next, cancellationToken), scheduled);
    }
}

/// <summary>
/// Restaurant : ses droits effectifs (plan et options) sur tous les codes connus. Sans abonnement vivant : tout désactivé.
/// Les compteurs (<see cref="MyFeaturesDto.Usage"/>) ne sont pas encore fournis : null.
/// </summary>
public sealed record GetMyFeaturesQuery : IQuery<MyFeaturesDto>;

public sealed class GetMyFeaturesHandler(BillingWorkspace workspace) : IQueryHandler<GetMyFeaturesQuery, MyFeaturesDto>
{
    public async Task<MyFeaturesDto> HandleAsync(GetMyFeaturesQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var subscription = await workspace.Subscriptions.FindLiveAsync(tenant, cancellationToken);
        if (subscription is null)
        {
            return new MyFeaturesDto(null, null, null, BillingFeatures.All.Select(code => new PlanFeatureDto(code, false, null)).ToList(), null);
        }

        var plan = await workspace.Plans.FindAsync(subscription.PlanId, cancellationToken);
        var addOns = await workspace.ActiveAddOnPlansAsync(tenant, cancellationToken);
        var features = BillingFeatures.All
            .Select(FeatureCode.Create)
            .Select(feature => TenantAddOn.Combine(
                plan?.EntitlementFor(feature) ?? Entitlement.Disabled(feature),
                addOns.Select(item => (item.Plan.EntitlementFor(feature), item.AddOn.Quantity))))
            .Select(entitlement => new PlanFeatureDto(entitlement.FeatureCode.Value, entitlement.Enabled, entitlement.Limit))
            .ToList();
        var trialEnds = subscription.Status == SubscriptionStatus.Trialing ? subscription.CurrentPeriod.EndUtc : (DateTimeOffset?)null;
        return new MyFeaturesDto(plan?.Name, subscription.Status, trialEnds, features, null);
    }
}

/// <summary>
/// Pour l'hôte (au nom d'un autre module) : le droit du restaurant du contexte pour une fonctionnalité. Sans abonnement
/// vivant ou si le plan ne la mentionne pas : désactivée.
/// </summary>
public sealed record GetFeatureLimitQuery(string FeatureCode) : IQuery<FeatureLimitDto>;

public sealed class GetFeatureLimitHandler(BillingWorkspace workspace) : IQueryHandler<GetFeatureLimitQuery, FeatureLimitDto>
{
    public async Task<FeatureLimitDto> HandleAsync(GetFeatureLimitQuery query, CancellationToken cancellationToken = default)
    {
        var feature = FeatureCode.Create(query.FeatureCode);
        var entitlement = await workspace.EntitlementAsync(workspace.CurrentTenant, feature, cancellationToken);
        var subscribed = await workspace.Subscriptions.FindLiveAsync(workspace.CurrentTenant, cancellationToken) is not null;
        return new FeatureLimitDto(feature.Value, entitlement.Enabled, entitlement.Limit, subscribed);
    }
}

/// <summary>Plateforme : les abonnements, filtrés par restaurant et statut au besoin, du plus récent au plus ancien.</summary>
public sealed record ListSubscriptionsQuery(Guid? TenantId = null, SubscriptionStatus? Status = null, int Skip = 0, int Take = 50)
    : IQuery<IReadOnlyList<SubscriptionDto>>;

public sealed class ListSubscriptionsHandler(BillingWorkspace workspace) : IQueryHandler<ListSubscriptionsQuery, IReadOnlyList<SubscriptionDto>>
{
    public async Task<IReadOnlyList<SubscriptionDto>> HandleAsync(ListSubscriptionsQuery query, CancellationToken cancellationToken = default)
    {
        BillingWorkspace.EnsurePage(query.Skip, query.Take);
        var tenant = query.TenantId is { } id && id != Guid.Empty ? TenantId.From(id) : (TenantId?)null;
        return (await workspace.Platform.ListAsync(tenant, query.Status, query.Skip, query.Take, cancellationToken))
            .Select(SubscriptionDto.From)
            .ToList();
    }
}

public sealed record GetSubscriptionQuery(Guid SubscriptionId) : IQuery<SubscriptionDto>;

public sealed class GetSubscriptionHandler(BillingWorkspace workspace) : IQueryHandler<GetSubscriptionQuery, SubscriptionDto>
{
    public async Task<SubscriptionDto> HandleAsync(GetSubscriptionQuery query, CancellationToken cancellationToken = default) =>
        SubscriptionDto.From(await workspace.LoadAnySubscriptionAsync(query.SubscriptionId, cancellationToken));
}

/// <summary>Restaurant : ses paiements d'abonnement, du plus récent au plus ancien.</summary>
public sealed record ListMySubscriptionPaymentsQuery(int Skip = 0, int Take = 50) : IQuery<IReadOnlyList<SubscriptionPaymentRecordDto>>;

public sealed class ListMySubscriptionPaymentsHandler(BillingWorkspace workspace)
    : IQueryHandler<ListMySubscriptionPaymentsQuery, IReadOnlyList<SubscriptionPaymentRecordDto>>
{
    public async Task<IReadOnlyList<SubscriptionPaymentRecordDto>> HandleAsync(
        ListMySubscriptionPaymentsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        BillingWorkspace.EnsurePage(query.Skip, query.Take);
        return (await workspace.Payments.ListAsync(workspace.CurrentTenant, query.Skip, query.Take, cancellationToken))
            .Select(SubscriptionPaymentRecordDto.From)
            .ToList();
    }
}

/// <summary>
/// Plateforme : les paiements d'abonnement de tous les restaurants, filtrés au besoin par restaurant, abonnement et
/// période d'enregistrement [From, To).
/// </summary>
public sealed record ListSubscriptionPaymentsQuery(
    Guid? TenantId = null, Guid? SubscriptionId = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int Skip = 0, int Take = 50)
    : IQuery<IReadOnlyList<SubscriptionPaymentRecordDto>>;

public sealed class ListSubscriptionPaymentsHandler(BillingWorkspace workspace)
    : IQueryHandler<ListSubscriptionPaymentsQuery, IReadOnlyList<SubscriptionPaymentRecordDto>>
{
    public async Task<IReadOnlyList<SubscriptionPaymentRecordDto>> HandleAsync(
        ListSubscriptionPaymentsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        BillingWorkspace.EnsurePage(query.Skip, query.Take);
        DomainException.ThrowIf(query.From is { } from && query.To is { } to && to <= from, BillingApplicationErrors.PeriodInvalid);
        var tenant = query.TenantId is { } tenantId && tenantId != Guid.Empty ? TenantId.From(tenantId) : (TenantId?)null;
        var subscription = query.SubscriptionId is { } subscriptionId && subscriptionId != Guid.Empty
            ? SubscriptionId.From(subscriptionId)
            : (SubscriptionId?)null;
        return (await workspace.Payments.ListAllAsync(tenant, subscription, query.From, query.To, query.Skip, query.Take, cancellationToken))
            .Select(SubscriptionPaymentRecordDto.From)
            .ToList();
    }
}
