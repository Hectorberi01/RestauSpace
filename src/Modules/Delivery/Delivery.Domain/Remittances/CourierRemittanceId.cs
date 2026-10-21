namespace Delivery.Domain;

public readonly record struct CourierRemittanceId
{
    private CourierRemittanceId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CourierRemittanceId New() => new(Guid.CreateVersion7());

    public static CourierRemittanceId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new CourierRemittanceId(value);
    }

    public override string ToString() => Value.ToString();
}
