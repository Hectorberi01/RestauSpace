namespace Billing.Domain;

public enum SubscriptionStatus
{
    Trialing = 1,
    Active = 2,
    PastDue = 3,
    Suspended = 4,
    Cancelled = 5,
}

public enum CancellationReason
{
    /// <summary>Résiliation explicite et immédiate.</summary>
    Requested = 1,

    /// <summary>Résiliation programmée, appliquée quand la période arrive à échéance de renouvellement.</summary>
    AtPeriodEnd = 2,
}

/// <summary>Motif de suspension d'un abonnement. Porté par <see cref="SubscriptionSuspended"/> pour que le côté Tenants puisse suspendre le tenant pour le même motif.</summary>
public enum SuspensionReason
{
    /// <summary>Essai terminé sans conversion. Uniquement pour un abonnement en essai dont l'essai est échu.</summary>
    TrialExpired = 1,
    PaymentOverdue = 2,
    PolicyViolation = 3,
    Other = 4,
}

/// <summary>Port : « ce tenant n'a-t-il aucun abonnement actif (non résilié) ? » ; un index unique reste le garde-fou final.</summary>
public interface ILiveSubscriptionChecker
{
    Task<bool> HasNoLiveSubscriptionAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

public static class SubscriptionErrors
{
    public static readonly DomainError AlreadySubscribed = new("billing.subscription.already_subscribed", "The tenant already has a live subscription.", DomainErrorKind.Conflict);
    public static readonly DomainError PlanArchived = new("billing.subscription.plan_archived", "An archived plan cannot be subscribed to.");
    public static readonly DomainError PlanIsAddOn = new("billing.subscription.plan_is_add_on", "An add-on is added to a subscription; it cannot be its plan.");
    public static readonly DomainError PlanChangeNotAllowed = new("billing.subscription.plan_change_not_allowed", "The plan can only be changed while trialing, active or after an expired trial.");
    public static readonly DomainError Cancelled = new("billing.subscription.cancelled", "A cancelled subscription is final; start a new subscription instead.");
    public static readonly DomainError CannotActivate = new("billing.subscription.cannot_activate", "Only a trialing, past due or suspended subscription can be activated.");
    public static readonly DomainError NotActive = new("billing.subscription.not_active", "The subscription is not active.");
    public static readonly DomainError CannotSuspend = new("billing.subscription.cannot_suspend", "Only a trialing, active or past due subscription can be suspended.");
    public static readonly DomainError SuspensionReasonInvalid = new("billing.subscription.suspension_reason_invalid", "Unknown suspension reason.");
    public static readonly DomainError TrialNotExpired = new("billing.subscription.trial_not_expired", "Only a trialing subscription whose trial period is over can be suspended for trial expiry.");
    public static readonly DomainError PeriodEnded = new("billing.subscription.period_ended", "The period has already ended.");
    public static readonly DomainError PeriodNotConsistent = new("billing.subscription.period_not_consistent", "The paid period must contain the current instant, or start exactly when the still running current period ends, and cannot start before the current period.");
    public static readonly DomainError PeriodNotEnded = new("billing.subscription.period_not_ended", "The current period has not ended yet.");
    public static readonly DomainError CancellationScheduled = new("billing.subscription.cancellation_scheduled", "A cancellation is scheduled at the end of the current period: unschedule it or cancel before extending the subscription.");
    public static readonly DomainError PeriodNotContiguous = new("billing.subscription.period_not_contiguous", "A renewed period must start exactly when the current one ends.");
    public static readonly DomainError CancellationAlreadyScheduled = new("billing.subscription.cancellation_already_scheduled", "Cancellation at period end is already scheduled.");
    public static readonly DomainError CancellationNotScheduled = new("billing.subscription.cancellation_not_scheduled", "No cancellation is scheduled.");
    public static readonly DomainError Complimentary = new("billing.subscription.complimentary", "The plan is offered by the platform: only the platform can change it.");
    public static readonly DomainError NotComplimentary = new("billing.subscription.not_complimentary", "The subscription is not offered by the platform.");
    public static readonly DomainError ComplimentaryEndInvalid = new("billing.subscription.complimentary_end_invalid", "An offer must end in the future.");
}

public sealed record SubscriptionTrialStarted(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    PlanId PlanId,
    Period TrialPeriod,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionActivated(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    PlanId PlanId,
    SubscriptionStatus PreviousStatus,
    Period CurrentPeriod,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>L'abonnement est passé à une autre formule (nom dans la spec : "PlanChanged" de SaasSubscription).</summary>
public sealed record SubscriptionPlanChanged(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    PlanId PreviousPlanId,
    PlanId NewPlanId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionPastDue(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    Period CurrentPeriod,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionSuspended(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    SubscriptionStatus PreviousStatus,
    SuspensionReason Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionCancellationScheduled(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    DateTimeOffset EffectiveAtUtc,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionCancellationUnscheduled(TenantId TenantId, SubscriptionId SubscriptionId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionPeriodRenewed(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    PlanId PlanId,
    Period CurrentPeriod,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <param name="UntilUtc">Fin de l'offre ; null = sans limite de durée.</param>
public sealed record SubscriptionComplimentaryGranted(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    PlanId PlanId,
    DateTimeOffset? UntilUtc,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionComplimentaryEnded(TenantId TenantId, SubscriptionId SubscriptionId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>L'essai, ou l'offre limitée, se termine bientôt : le restaurant est prévenu.</summary>
public sealed record SubscriptionEndingSoon(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    PlanId PlanId,
    DateTimeOffset EndsAtUtc,
    bool Complimentary,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionCancelled(
    TenantId TenantId,
    SubscriptionId SubscriptionId,
    PlanId PlanId,
    CancellationReason Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
