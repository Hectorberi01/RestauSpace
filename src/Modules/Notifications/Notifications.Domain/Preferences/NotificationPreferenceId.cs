namespace Notifications.Domain;

public readonly record struct NotificationPreferenceId
{
    private NotificationPreferenceId(Guid value) => Value = value;

    public Guid Value { get; }

    public static NotificationPreferenceId New() => new(Guid.CreateVersion7());

    public static NotificationPreferenceId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new NotificationPreferenceId(value);
    }

    public override string ToString() => Value.ToString();
}
