namespace Notifications.Domain;

public readonly record struct NotificationId
{
    private NotificationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static NotificationId New() => new(Guid.CreateVersion7());

    public static NotificationId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new NotificationId(value);
    }

    public override string ToString() => Value.ToString();
}
