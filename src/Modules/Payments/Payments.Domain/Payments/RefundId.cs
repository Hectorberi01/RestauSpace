namespace Payments.Domain;

public readonly record struct RefundId
{
    private RefundId(Guid value) => Value = value;

    public Guid Value { get; }

    public static RefundId New() => new(Guid.CreateVersion7());

    public static RefundId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new RefundId(value);
    }

    public override string ToString() => Value.ToString();
}
