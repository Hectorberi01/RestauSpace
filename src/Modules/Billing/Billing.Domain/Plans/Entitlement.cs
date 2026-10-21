namespace Billing.Domain;

/// <summary>
/// Ce qu'un plan accorde pour une fonctionnalité. Activé avec une limite nulle signifie illimité ; un droit
/// désactivé ne porte jamais de limite.
/// </summary>
public sealed class Entitlement : ValueObject
{
    private Entitlement(FeatureCode featureCode, bool enabled, long? limit)
    {
        FeatureCode = featureCode;
        Enabled = enabled;
        Limit = limit;
    }

    public FeatureCode FeatureCode { get; }

    public bool Enabled { get; }

    public long? Limit { get; }

    public bool IsUnlimited => Enabled && Limit is null;

    public static Entitlement Create(FeatureCode featureCode, bool enabled, long? limit = null)
    {
        ArgumentNullException.ThrowIfNull(featureCode);
        DomainException.ThrowIf(limit < 0, PlanErrors.EntitlementLimitNegative);
        DomainException.ThrowIf(!enabled && limit is not null, PlanErrors.EntitlementDisabledWithLimit);
        return new Entitlement(featureCode, enabled, limit);
    }

    public static Entitlement Unlimited(FeatureCode featureCode) => Create(featureCode, true);

    public static Entitlement Limited(FeatureCode featureCode, long limit) => Create(featureCode, true, limit);

    public static Entitlement Disabled(FeatureCode featureCode) => Create(featureCode, false);

    /// <summary>Vrai quand une consommation totale de <paramref name="used"/> reste dans ce qui est accordé.</summary>
    public bool Allows(long used) => Enabled && (Limit is null || used <= Limit);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FeatureCode;
        yield return Enabled;
        yield return Limit;
    }
}
