namespace Audit.Domain;

public readonly record struct AuditId
{
    private AuditId(Guid value) => Value = value;

    public Guid Value { get; }

    public static AuditId New() => new(Guid.CreateVersion7());

    public static AuditId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new AuditId(value);
    }

    public override string ToString() => Value.ToString();
}
