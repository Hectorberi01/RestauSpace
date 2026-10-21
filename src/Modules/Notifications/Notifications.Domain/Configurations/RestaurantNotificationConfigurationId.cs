namespace Notifications.Domain;

public readonly record struct RestaurantNotificationConfigurationId
{
    private RestaurantNotificationConfigurationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static RestaurantNotificationConfigurationId New() => new(Guid.CreateVersion7());

    public static RestaurantNotificationConfigurationId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new RestaurantNotificationConfigurationId(value);
    }

    public override string ToString() => Value.ToString();
}
