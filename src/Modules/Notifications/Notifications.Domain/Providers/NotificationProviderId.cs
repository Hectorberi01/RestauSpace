namespace Notifications.Domain;

public readonly record struct NotificationProviderId
{
    private NotificationProviderId(Guid value) => Value = value;

    public Guid Value { get; }

    public static NotificationProviderId New() => new(Guid.CreateVersion7());

    public static NotificationProviderId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new NotificationProviderId(value);
    }

    public override string ToString() => Value.ToString();
}
