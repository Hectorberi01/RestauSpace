namespace Promotions.Domain;

/// <summary>Nombre maximal de fois qu'une promotion peut être appliquée.</summary>
public sealed class UsageLimit : ValueObject
{
    private UsageLimit(int value) => Value = value;

    public int Value { get; }

    public static UsageLimit Of(int value)
    {
        DomainException.ThrowIf(value <= 0, PromotionErrors.UsageLimitNotPositive);
        return new UsageLimit(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
