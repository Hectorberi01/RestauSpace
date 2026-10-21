namespace Floor.Domain;

/// <summary>
/// Numéro ou nom d'une table ("12", "Terrasse 3"), stocké sous sa forme canonique (NFKC, sans caractère de contrôle ni
/// invisible, espaces simples, sans espaces autour). Deux libellés ne différant que par la casse sont identiques.
/// </summary>
public sealed class TableLabel : ValueObject
{
    public const int MaxLength = 30;

    private TableLabel(string value) => Value = value;

    public string Value { get; }

    public static TableLabel Create(string? value)
    {
        var normalized = DisplayText.Normalize(value);
        DomainException.ThrowIf(normalized.Length == 0, DiningAreaErrors.TableLabelRequired);
        DomainException.ThrowIf(normalized.Length > MaxLength, DiningAreaErrors.TableLabelTooLong);

        return new TableLabel(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value.ToUpperInvariant();
    }

    public override string ToString() => Value;
}
