namespace Billing.Domain;

public readonly record struct SubscriptionPaymentId
{
    private SubscriptionPaymentId(Guid value) => Value = value;

    public Guid Value { get; }

    public static SubscriptionPaymentId New() => new(Guid.CreateVersion7());

    public static SubscriptionPaymentId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new SubscriptionPaymentId(value);
    }

    public override string ToString() => Value.ToString();
}
