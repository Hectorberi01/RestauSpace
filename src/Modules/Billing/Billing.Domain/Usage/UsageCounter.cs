namespace Billing.Domain;

/// <summary>
/// Consommation d'une fonctionnalité par un tenant pendant la période en cours. Clé métier : (TenantId, FeatureCode) ;
/// le même compteur passe à la période suivante via <see cref="ResetForNewPeriod"/>.
/// </summary>
public sealed class UsageCounter : AggregateRoot<UsageCounterId>, ITenantOwned
{
    private UsageCounter(UsageCounterId id, TenantId tenantId, FeatureCode featureCode, Period period) : base(id)
    {
        TenantId = tenantId;
        FeatureCode = featureCode;
        Period = period;
    }

    // Réservé à l'ORM.
    private UsageCounter()
    {
        FeatureCode = null!;
        Period = null!;
    }

    public TenantId TenantId { get; }

    public FeatureCode FeatureCode { get; private set; }

    public Period Period { get; private set; }

    public long Used { get; private set; }

    public static UsageCounter Open(TenantId tenantId, FeatureCode featureCode, Period period, bool isFirstCounterForFeature)
    {
        ArgumentNullException.ThrowIfNull(featureCode);
        ArgumentNullException.ThrowIfNull(period);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(!isFirstCounterForFeature, UsageCounterErrors.AlreadyExists);

        return new UsageCounter(UsageCounterId.New(), tenantId, featureCode, period);
    }

    /// <summary>
    /// Enregistre <paramref name="amount"/> unités. Un incrément bloquant applique le droit : il est rejeté,
    /// et rien ne change, quand la fonctionnalité est désactivée ou que la limite serait dépassée. Un incrément
    /// non bloquant ne fait que mesurer (quota souple) et peut dépasser la limite.
    /// </summary>
    public void Increment(long amount, Entitlement entitlement, bool blocking, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(entitlement);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(amount <= 0, UsageCounterErrors.AmountNotPositive);
        DomainException.ThrowIf(entitlement.FeatureCode != FeatureCode, UsageCounterErrors.FeatureMismatch);
        var now = clock.UtcNow;
        DomainException.ThrowIf(!Period.Contains(now), UsageCounterErrors.OutsidePeriod);

        DomainException.ThrowIf(amount > long.MaxValue - Used, UsageCounterErrors.UsageOverflow);

        var newUsed = Used + amount;
        if (blocking)
        {
            DomainException.ThrowIf(!entitlement.Enabled, UsageCounterErrors.FeatureDisabled);
            DomainException.ThrowIf(!entitlement.Allows(newUsed), UsageCounterErrors.QuotaExceeded);
        }

        var previousUsed = Used;
        Used = newUsed;

        if (entitlement.Limit is { } limit && previousUsed < limit && newUsed >= limit)
        {
            Raise(new QuotaReached(TenantId, Id, FeatureCode, Period, limit, Used, now));
        }
    }

    /// <summary>Passe à la période suivante, une fois l'actuelle terminée : l'usage d'une période en cours n'est jamais effacé.</summary>
    public void ResetForNewPeriod(Period newPeriod, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newPeriod);
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        DomainException.ThrowIf(newPeriod.StartUtc < Period.EndUtc, UsageCounterErrors.PeriodNotAfterCurrent);
        DomainException.ThrowIf(!Period.HasEndedAt(now), UsageCounterErrors.PeriodNotEnded);

        var previousPeriod = Period;
        var previousUsed = Used;
        Period = newPeriod;
        Used = 0;
        Raise(new UsageCounterReset(TenantId, Id, FeatureCode, previousPeriod, previousUsed, newPeriod, now));
    }
}
