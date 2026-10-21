namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct OrderItemId
{
    private OrderItemId(Guid value) => Value = value;

    public Guid Value { get; }

    public static OrderItemId New() => new(Guid.CreateVersion7());

    public static OrderItemId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new OrderItemId(value);
    }

    public override string ToString() => Value.ToString();
}
