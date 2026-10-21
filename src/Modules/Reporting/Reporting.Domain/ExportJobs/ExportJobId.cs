namespace Reporting.Domain;

public readonly record struct ExportJobId
{
    private ExportJobId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ExportJobId New() => new(Guid.CreateVersion7());

    public static ExportJobId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ExportJobId(value);
    }

    public override string ToString() => Value.ToString();
}
