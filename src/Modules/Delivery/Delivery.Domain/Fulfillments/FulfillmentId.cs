namespace Delivery.Domain;

public readonly record struct FulfillmentId
{
    private FulfillmentId(Guid value) => Value = value;

    public Guid Value { get; }

    public static FulfillmentId New() => new(Guid.CreateVersion7());

    public static FulfillmentId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new FulfillmentId(value);
    }

    public override string ToString() => Value.ToString();
}
