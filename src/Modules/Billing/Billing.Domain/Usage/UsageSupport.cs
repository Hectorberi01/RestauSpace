namespace Billing.Domain;

/// <summary>Port qui répond à « ce tenant n'a-t-il pas encore de compteur pour cette fonctionnalité ? » ;
/// l'index unique (tenant, feature) reste le garde-fou final.</summary>
public interface IUsageCounterUniquenessChecker
{
    Task<bool> IsUniqueAsync(TenantId tenantId, FeatureCode featureCode, CancellationToken cancellationToken = default);
}

public static class UsageCounterErrors
{
    public static readonly DomainError AlreadyExists = new("billing.usage_counter.already_exists", "The tenant already has a counter for this feature.");
    public static readonly DomainError AmountNotPositive = new("billing.usage_counter.amount_not_positive", "A usage increment must be strictly positive.");
    public static readonly DomainError FeatureMismatch = new("billing.usage_counter.feature_mismatch", "The entitlement is for another feature.");
    public static readonly DomainError OutsidePeriod = new("billing.usage_counter.outside_period", "Usage can only be recorded during the counter period.");
    public static readonly DomainError FeatureDisabled = new("billing.usage_counter.feature_disabled", "The feature is not included in the plan.");
    public static readonly DomainError QuotaExceeded = new("billing.usage_counter.quota_exceeded", "The quota of the period would be exceeded.");
    public static readonly DomainError UsageOverflow = new("billing.usage_counter.overflow", "The usage counter cannot hold such an amount.");
    public static readonly DomainError PeriodNotEnded = new("billing.usage_counter.period_not_ended", "The counter can only be reset once its current period has ended.");
    public static readonly DomainError PeriodNotAfterCurrent = new("billing.usage_counter.period_not_after_current", "The new period must start at or after the end of the current one.");
}

public sealed record QuotaReached(
    TenantId TenantId,
    UsageCounterId UsageCounterId,
    FeatureCode FeatureCode,
    Period Period,
    long Limit,
    long Used,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record UsageCounterReset(
    TenantId TenantId,
    UsageCounterId UsageCounterId,
    FeatureCode FeatureCode,
    Period PreviousPeriod,
    long PreviousUsed,
    Period Period,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
