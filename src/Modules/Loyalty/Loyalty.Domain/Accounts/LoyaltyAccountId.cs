namespace Loyalty.Domain;

public readonly record struct LoyaltyAccountId
{
    private LoyaltyAccountId(Guid value) => Value = value;

    public Guid Value { get; }

    public static LoyaltyAccountId New() => new(Guid.CreateVersion7());

    public static LoyaltyAccountId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new LoyaltyAccountId(value);
    }

    public override string ToString() => Value.ToString();
}
