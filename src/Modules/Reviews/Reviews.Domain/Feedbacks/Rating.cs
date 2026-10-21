namespace Reviews.Domain;

/// <summary>Note en étoiles entières de 1 (la pire) à 5 (la meilleure).</summary>
public sealed class Rating : ValueObject
{
    public const int Min = 1;
    public const int Max = 5;

    private Rating(int value) => Value = value;

    public int Value { get; }

    public static Rating Create(int value)
    {
        DomainException.ThrowIf(value is < Min or > Max, FeedbackErrors.RatingOutOfRange);
        return new Rating(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value}/{Max}";
}
