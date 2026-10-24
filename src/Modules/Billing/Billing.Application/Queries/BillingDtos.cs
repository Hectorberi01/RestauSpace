using Billing.Domain;

namespace Billing.Application.Queries;

/// <param name="Limit">Null : illimité (si activé).</param>
public sealed record EntitlementDto(string FeatureCode, bool Enabled, long? Limit)
{
    public static EntitlementDto From(Entitlement entitlement)
    {
        ArgumentNullException.ThrowIfNull(entitlement);
        return new(entitlement.FeatureCode.Value, entitlement.Enabled, entitlement.Limit);
    }
}

public sealed record PlanDto(
    Guid Id,
    string Name,
    decimal Price,
    string CurrencyCode,
    BillingInterval BillingInterval,
    PlanStatus Status,
    IReadOnlyList<EntitlementDto> Entitlements,
    PlanKind Kind = PlanKind.Base)
{
    public static PlanDto From(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(
            plan.Id.Value, plan.Name, plan.Price.Amount, plan.Price.Currency.Code, plan.BillingInterval, plan.Status,
            plan.Entitlements.OrderBy(entitlement => entitlement.FeatureCode.Value, StringComparer.Ordinal).Select(EntitlementDto.From).ToList(),
            plan.Kind);
    }
}

/// <summary>Une option souscrite par un restaurant : le plan d'option, la quantité et son état.</summary>
public sealed record TenantAddOnDto(
    Guid Id,
    Guid TenantId,
    PlanDto AddOn,
    int Quantity,
    TenantAddOnStatus Status,
    DateTimeOffset ActivatedAtUtc,
    DateTimeOffset? CancelledAtUtc)
{
    public static TenantAddOnDto From(TenantAddOn addOn, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(addOn);
        return new(addOn.Id.Value, addOn.TenantId.Value, PlanDto.From(plan), addOn.Quantity, addOn.Status, addOn.ActivatedAtUtc, addOn.CancelledAtUtc);
    }
}

/// <summary>Ce que voit le restaurant : les options proposées dans sa devise, et celles qu'il a.</summary>
public sealed record MyAddOnsDto(IReadOnlyList<PlanDto> Offered, IReadOnlyList<TenantAddOnDto> Active);

public sealed record SubscriptionDto(
    Guid Id,
    Guid TenantId,
    Guid PlanId,
    SubscriptionStatus Status,
    DateTimeOffset CurrentPeriodStartUtc,
    DateTimeOffset CurrentPeriodEndUtc,
    bool CancelAtPeriodEnd,
    SuspensionReason? SuspensionReason,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    bool IsComplimentary = false,
    DateTimeOffset? ComplimentaryUntilUtc = null,
    Guid? ScheduledPlanId = null,
    DateTimeOffset? ScheduledPlanEffectiveAtUtc = null)
{
    public static SubscriptionDto From(SaasSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return new(
            subscription.Id.Value, subscription.TenantId.Value, subscription.PlanId.Value, subscription.Status,
            subscription.CurrentPeriod.StartUtc, subscription.CurrentPeriod.EndUtc, subscription.CancelAtPeriodEnd,
            subscription.SuspensionReason, subscription.StartedAtUtc, subscription.CancelledAtUtc,
            subscription.IsComplimentary, subscription.IsComplimentary && !subscription.IsComplimentaryUnlimited ? subscription.CurrentPeriod.EndUtc : null,
            subscription.ScheduledPlanId?.Value, subscription.ScheduledPlanEffectiveAtUtc);
    }
}

/// <summary>Un droit du plan et, pour une fonctionnalité de consommation, ce qui a été consommé ce mois-ci.</summary>
public sealed record FeatureUsageDto(string FeatureCode, bool Enabled, long? Limit, long Used);

/// <summary>Une ligne de ce que paie une période : le plan de base ou une option (prix unitaire pour la période).</summary>
public sealed record ChargeLineDto(Guid PlanId, PlanKind Kind, string Label, int Quantity, decimal UnitAmount, decimal Amount)
{
    public static ChargeLineDto From(SubscriptionPaymentLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new(line.PlanId.Value, line.Kind, line.Label, line.Quantity, line.UnitAmount, line.Amount);
    }
}

/// <summary>
/// Ce que coûtera la prochaine période payée de l'abonnement, au prix actuel du plan et des options actives
/// (<paramref name="Total"/>), et le même montant ramené au mois (<paramref name="MonthlyTotal"/>).
/// </summary>
public sealed record SubscriptionChargeDto(
    Guid SubscriptionId, Guid TenantId, string CurrencyCode, BillingInterval BillingInterval, IReadOnlyList<ChargeLineDto> Lines, decimal Total, decimal MonthlyTotal);

/// <summary>Le changement de plan programmé : le plan qui s'appliquera, et quand.</summary>
public sealed record ScheduledPlanChangeDto(Guid PlanId, string PlanName, DateTimeOffset EffectiveAtUtc);

/// <summary>
/// Ce que voit le restaurant : son abonnement, son plan, ses droits avec leur consommation, ce que coûtera sa prochaine
/// période (au plan programmé s'il y en a un) et le changement de plan programmé.
/// </summary>
public sealed record MySubscriptionDto(
    SubscriptionDto Subscription, PlanDto Plan, IReadOnlyList<FeatureUsageDto> Usage, SubscriptionChargeDto? NextCharge = null,
    ScheduledPlanChangeDto? ScheduledPlan = null);

/// <summary>
/// La réponse donnée aux autres modules : activé ou non, limite (null = illimité), et si le restaurant a un abonnement
/// vivant (sans abonnement, rien n'est activé : à l'hôte de décider ce que cela signifie pour chaque fonctionnalité).
/// </summary>
public sealed record FeatureLimitDto(string FeatureCode, bool Enabled, long? Limit, bool HasLiveSubscription);

/// <summary>Un paiement d'abonnement enregistré : plan et montant au moment du paiement, période couverte.</summary>
public sealed record SubscriptionPaymentRecordDto(
    Guid Id,
    Guid TenantId,
    Guid SubscriptionId,
    Guid PlanId,
    string PlanName,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset PaidFromUtc,
    DateTimeOffset PaidUntilUtc,
    DateTimeOffset RecordedAtUtc,
    IReadOnlyList<ChargeLineDto>? Lines = null,
    string? InvoiceNumber = null,
    string? CollectedBy = null)
{
    public static SubscriptionPaymentRecordDto From(SubscriptionPayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return new(
            payment.Id.Value, payment.TenantId.Value, payment.SubscriptionId.Value, payment.PlanId.Value, payment.PlanName,
            payment.Amount.Amount, payment.Amount.Currency.Code, payment.PaidPeriod.StartUtc, payment.PaidPeriod.EndUtc, payment.RecordedAtUtc,
            payment.Lines.Select(ChargeLineDto.From).ToList(), payment.InvoiceNumber, payment.CollectedBy);
    }
}
