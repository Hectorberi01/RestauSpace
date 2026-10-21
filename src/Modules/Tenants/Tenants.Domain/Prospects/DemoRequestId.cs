namespace Tenants.Domain.Prospects;

public readonly record struct DemoRequestId
{
    private DemoRequestId(Guid value) => Value = value;

    public Guid Value { get; }

    public static DemoRequestId New() => new(Guid.CreateVersion7());

    public static DemoRequestId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new DemoRequestId(value);
    }

    public override string ToString() => Value.ToString();
}
