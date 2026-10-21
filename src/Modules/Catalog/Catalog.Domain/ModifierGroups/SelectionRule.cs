namespace Catalog.Domain;

/// <summary>Nombre de modificateurs d'un groupe qu'un client doit (min) et peut (max) choisir : 0 &lt;= min &lt;= max,
/// 1 &lt;= max &lt;= 50.</summary>
public sealed class SelectionRule : ValueObject
{
    /// <summary>Borne haute de <see cref="MaxSelections"/> : au-delà, un groupe est une erreur de saisie, pas une option
    /// de menu.</summary>
    public const int MaxSelectionsLimit = 50;

    private SelectionRule(int minSelections, int maxSelections)
    {
        MinSelections = minSelections;
        MaxSelections = maxSelections;
    }

    public int MinSelections { get; }

    public int MaxSelections { get; }

    public bool IsRequired => MinSelections > 0;

    /// <summary>Choix libre d'au plus un modificateur.</summary>
    public static SelectionRule OptionalSingle => new(0, 1);

    /// <summary>Exactement un modificateur doit être choisi.</summary>
    public static SelectionRule RequiredSingle => new(1, 1);

    public static SelectionRule Create(int minSelections, int maxSelections)
    {
        DomainException.ThrowIf(minSelections < 0, ModifierGroupErrors.SelectionMinNegative);
        DomainException.ThrowIf(maxSelections < minSelections, ModifierGroupErrors.SelectionMaxBelowMin);
        DomainException.ThrowIf(maxSelections is < 1 or > MaxSelectionsLimit, ModifierGroupErrors.SelectionMaxOutOfRange);

        return new SelectionRule(minSelections, maxSelections);
    }

    public bool Allows(int selectionCount) => selectionCount >= MinSelections && selectionCount <= MaxSelections;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MinSelections;
        yield return MaxSelections;
    }

    public override string ToString() => $"{MinSelections}..{MaxSelections}";
}
