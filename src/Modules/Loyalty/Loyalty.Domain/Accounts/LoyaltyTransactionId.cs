namespace Loyalty.Domain;

public readonly record struct LoyaltyTransactionId
{
    private LoyaltyTransactionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static LoyaltyTransactionId New() => new(Guid.CreateVersion7());

    public static LoyaltyTransactionId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new LoyaltyTransactionId(value);
    }

    public override string ToString() => Value.ToString();
}
