namespace Reporting.Domain;

/// <summary>Ce qui est exporté, en code contrôlé (ex. <c>orders.csv</c>). Liste ouverte, tenue par les workers.</summary>
public sealed class ExportType : ValueObject
{
    private ExportType(string value) => Value = value;

    public string Value { get; }

    public static ExportType Create(string? value) => new(ReportCode.Normalize(value, ExportErrors.TypeInvalid));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
