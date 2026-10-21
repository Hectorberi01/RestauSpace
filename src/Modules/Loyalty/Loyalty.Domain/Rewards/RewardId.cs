namespace Loyalty.Domain;

public readonly record struct RewardId
{
    private RewardId(Guid value) => Value = value;

    public Guid Value { get; }

    public static RewardId New() => new(Guid.CreateVersion7());

    public static RewardId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new RewardId(value);
    }

    public override string ToString() => Value.ToString();
}
