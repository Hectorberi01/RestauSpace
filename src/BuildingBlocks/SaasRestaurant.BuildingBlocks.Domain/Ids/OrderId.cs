namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct OrderId
{
    private OrderId(Guid value) => Value = value;

    public Guid Value { get; }

    public static OrderId New() => new(Guid.CreateVersion7());

    public static OrderId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new OrderId(value);
    }

    public override string ToString() => Value.ToString();
}
