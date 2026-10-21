namespace Reporting.Domain;

public readonly record struct ReportDefinitionId
{
    private ReportDefinitionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ReportDefinitionId New() => new(Guid.CreateVersion7());

    public static ReportDefinitionId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ReportDefinitionId(value);
    }

    public override string ToString() => Value.ToString();
}
