namespace Storefront.Domain;

public readonly record struct OnlineOrderId
{
    private OnlineOrderId(Guid value) => Value = value;

    public Guid Value { get; }

    public static OnlineOrderId New() => new(Guid.CreateVersion7());

    public static OnlineOrderId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new OnlineOrderId(value);
    }

    public override string ToString() => Value.ToString();
}
