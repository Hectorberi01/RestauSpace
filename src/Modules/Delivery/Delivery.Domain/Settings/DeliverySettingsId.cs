namespace Delivery.Domain;

public readonly record struct DeliverySettingsId
{
    private DeliverySettingsId(Guid value) => Value = value;

    public Guid Value { get; }

    public static DeliverySettingsId New() => new(Guid.CreateVersion7());

    public static DeliverySettingsId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new DeliverySettingsId(value);
    }

    public override string ToString() => Value.ToString();
}
