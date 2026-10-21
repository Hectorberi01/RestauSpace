namespace Locations.Domain.Locations;

public sealed class LocationName : ValueObject
{
    public const int MinLength = 2;
    public const int MaxLength = 100;

    private LocationName(string value) => Value = value;

    public string Value { get; }

    public static LocationName Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), LocationErrors.NameEmpty);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(trimmed.Length is < MinLength or > MaxLength, LocationErrors.NameLengthInvalid);

        return new LocationName(trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
