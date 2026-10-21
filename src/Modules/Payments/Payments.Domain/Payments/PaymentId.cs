namespace Payments.Domain;

public readonly record struct PaymentId
{
    private PaymentId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PaymentId New() => new(Guid.CreateVersion7());

    public static PaymentId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new PaymentId(value);
    }

    public override string ToString() => Value.ToString();
}
