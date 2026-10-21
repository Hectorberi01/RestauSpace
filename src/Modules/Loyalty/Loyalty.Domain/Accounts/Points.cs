namespace Loyalty.Domain;

/// <summary>
/// Une quantité de points de fidélité : jamais négative. Le fait qu'elle soit ajoutée ou retirée est porté par la
/// transaction (type / sens), pas par un signe.
/// </summary>
public sealed class Points : ValueObject
{
    private Points(int value) => Value = value;

    public int Value { get; }

    public bool IsZero => Value == 0;

    public static Points Zero { get; } = new(0);

    public static Points Of(int value)
    {
        DomainException.ThrowIf(value < 0, LoyaltyAccountErrors.PointsNegative);
        return new Points(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
