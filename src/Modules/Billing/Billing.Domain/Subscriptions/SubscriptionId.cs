namespace Billing.Domain;

public readonly record struct SubscriptionId
{
    private SubscriptionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static SubscriptionId New() => new(Guid.CreateVersion7());

    public static SubscriptionId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new SubscriptionId(value);
    }

    public override string ToString() => Value.ToString();
}
