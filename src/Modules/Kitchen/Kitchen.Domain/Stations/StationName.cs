namespace Kitchen.Domain;

/// <summary>Nom, sans espaces superflus, d'un poste de préparation ("Grill", "Froid", "Bar"...).</summary>
public sealed class StationName : ValueObject
{
    public const int MaxLength = 60;

    private StationName(string value) => Value = value;

    public string Value { get; }

    public static StationName Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), KitchenStationErrors.NameEmpty);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(trimmed.Length > MaxLength, KitchenStationErrors.NameTooLong);

        return new StationName(trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
