namespace Billing.Domain;

public enum PlanStatus
{
    Active = 1,
    Archived = 2,
}

/// <summary>Plan de base (un par abonnement) ou option qui s'ajoute au plan (vente en ligne, borne…).</summary>
public enum PlanKind
{
    Base = 1,
    AddOn = 2,
}

public enum BillingInterval
{
    Monthly = 1,
    Yearly = 2,
}

/// <summary>Ce qui a changé dans l'offre commerciale.</summary>
public enum PlanDefinitionChange
{
    PriceChanged = 1,
    EntitlementSet = 2,
}

/// <summary>
/// Ce qu'un abonnement doit savoir d'un plan. Seul <see cref="Plan.ToReference"/> peut la construire : la
/// réponse « est actif » vient donc toujours d'un vrai plan, jamais d'un appelant.
/// </summary>
public sealed record PlanReference
{
    internal PlanReference(PlanId planId, bool isActive, bool isAddOn = false)
    {
        PlanId = planId;
        IsActive = isActive;
        IsAddOn = isAddOn;
    }

    public PlanId PlanId { get; }

    public bool IsActive { get; }

    /// <summary>Vrai pour une option : elle s'ajoute à un abonnement, elle n'en est jamais le plan.</summary>
    public bool IsAddOn { get; }
}

public static class PlanErrors
{
    public static readonly DomainError FeatureCodeRequired = new("billing.feature_code.required", "Feature code is required.");
    public static readonly DomainError FeatureCodeInvalid = new("billing.feature_code.invalid", "Feature code must be 2 to 64 characters (a-z, 0-9, '.', '_', '-') and start with a letter.");
    public static readonly DomainError EntitlementLimitNegative = new("billing.entitlement.limit_negative", "An entitlement limit cannot be negative.");
    public static readonly DomainError EntitlementDisabledWithLimit = new("billing.entitlement.disabled_with_limit", "A disabled entitlement cannot carry a limit.");

    public static readonly DomainError NameRequired = new("billing.plan.name_required", "Plan name is required.");
    public static readonly DomainError NameTooLong = new("billing.plan.name_too_long", "Plan name is too long.");
    public static readonly DomainError PriceNegative = new("billing.plan.price_negative", "A plan price cannot be negative.");
    public static readonly DomainError PriceCurrencyMismatch = new("billing.plan.price_currency_mismatch", "A plan keeps the currency it was created with.");
    public static readonly DomainError BillingIntervalInvalid = new("billing.plan.billing_interval_invalid", "Unknown billing interval.");
    public static readonly DomainError DuplicateEntitlement = new("billing.plan.duplicate_entitlement", "A plan has at most one entitlement per feature.");
    public static readonly DomainError Archived = new("billing.plan.archived", "An archived plan cannot change anymore.");
    public static readonly DomainError KindInvalid = new("billing.plan.kind_invalid", "Unknown plan kind.");
}

public sealed record PlanCreated(PlanId PlanId, string Name, Money Price, BillingInterval BillingInterval, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>L'offre elle-même a changé (spec : « PlanChanged » de Plan). Ne pas confondre avec <c>SubscriptionPlanChanged</c>.</summary>
public sealed record PlanDefinitionChanged(
    PlanId PlanId,
    PlanDefinitionChange Change,
    Money Price,
    Entitlement? Entitlement,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PlanArchived(PlanId PlanId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
