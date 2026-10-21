namespace Payments.Domain;

public readonly record struct PaymentProviderId
{
    private PaymentProviderId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PaymentProviderId New() => new(Guid.CreateVersion7());

    public static PaymentProviderId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new PaymentProviderId(value);
    }

    public override string ToString() => Value.ToString();
}
