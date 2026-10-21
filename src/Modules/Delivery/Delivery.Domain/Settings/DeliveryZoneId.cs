namespace Delivery.Domain;

public readonly record struct DeliveryZoneId
{
    private DeliveryZoneId(Guid value) => Value = value;

    public Guid Value { get; }

    public static DeliveryZoneId New() => new(Guid.CreateVersion7());

    public static DeliveryZoneId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new DeliveryZoneId(value);
    }

    public override string ToString() => Value.ToString();
}
