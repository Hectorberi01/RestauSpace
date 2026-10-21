namespace Payments.Domain;

public readonly record struct RestaurantPaymentConfigurationId
{
    private RestaurantPaymentConfigurationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static RestaurantPaymentConfigurationId New() => new(Guid.CreateVersion7());

    public static RestaurantPaymentConfigurationId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new RestaurantPaymentConfigurationId(value);
    }

    public override string ToString() => Value.ToString();
}
