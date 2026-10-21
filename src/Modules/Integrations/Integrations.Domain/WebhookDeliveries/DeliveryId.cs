namespace Integrations.Domain;

public readonly record struct DeliveryId
{
    private DeliveryId(Guid value) => Value = value;

    public Guid Value { get; }

    public static DeliveryId New() => new(Guid.CreateVersion7());

    public static DeliveryId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new DeliveryId(value);
    }

    public override string ToString() => Value.ToString();
}
