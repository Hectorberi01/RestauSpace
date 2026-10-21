namespace Promotions.Domain;

public readonly record struct CouponId
{
    private CouponId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CouponId New() => new(Guid.CreateVersion7());

    public static CouponId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new CouponId(value);
    }

    public override string ToString() => Value.ToString();
}
