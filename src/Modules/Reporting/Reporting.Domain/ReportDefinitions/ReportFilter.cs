namespace Reporting.Domain;

public enum FilterOperator
{
    EqualTo = 1,
    NotEqualTo = 2,
    GreaterThan = 3,
    GreaterThanOrEqualTo = 4,
    LessThan = 5,
    LessThanOrEqualTo = 6,
    In = 7,
}

/// <summary>
/// Un critère (champ, opérateur, valeur). Bien formé à lui seul ; l'autorisation du champ est décidée
/// au regard d'un <see cref="ReportCatalog"/> quand il entre dans un ensemble <see cref="ReportFilters"/>.
/// La valeur est un texte opaque, toujours transmis au read model comme paramètre de requête.
/// </summary>
public sealed class ReportFilter : ValueObject
{
    public const int MaxValueLength = 200;

    private ReportFilter(string field, FilterOperator @operator, string value)
    {
        Field = field;
        Operator = @operator;
        Value = value;
    }

    public string Field { get; }

    public FilterOperator Operator { get; }

    public string Value { get; }

    public static ReportFilter Create(string? field, FilterOperator @operator, string? value)
    {
        var normalizedField = ReportCode.Normalize(field);
        DomainException.ThrowIf(!Enum.IsDefined(@operator), ReportErrors.FilterOperatorInvalid);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), ReportErrors.FilterValueInvalid);

        var trimmedValue = value!.Trim();
        DomainException.ThrowIf(trimmedValue.Length > MaxValueLength, ReportErrors.FilterValueInvalid);

        return new ReportFilter(normalizedField, @operator, trimmedValue);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Field;
        yield return Operator;
        yield return Value;
    }

    public override string ToString() => $"{Field} {Operator} {Value}";
}
