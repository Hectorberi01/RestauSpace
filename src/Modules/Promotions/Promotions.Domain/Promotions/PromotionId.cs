namespace Promotions.Domain;

public readonly record struct PromotionId
{
    private PromotionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PromotionId New() => new(Guid.CreateVersion7());

    public static PromotionId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new PromotionId(value);
    }

    public override string ToString() => Value.ToString();
}
