namespace Notifications.Domain;

public readonly record struct NotificationDeliveryId
{
    private NotificationDeliveryId(Guid value) => Value = value;

    public Guid Value { get; }

    public static NotificationDeliveryId New() => new(Guid.CreateVersion7());

    public static NotificationDeliveryId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new NotificationDeliveryId(value);
    }

    public override string ToString() => Value.ToString();
}
