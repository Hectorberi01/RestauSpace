namespace Notifications.Domain;

public readonly record struct NotificationChannelAssignmentId
{
    private NotificationChannelAssignmentId(Guid value) => Value = value;

    public Guid Value { get; }

    public static NotificationChannelAssignmentId New() => new(Guid.CreateVersion7());

    public static NotificationChannelAssignmentId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new NotificationChannelAssignmentId(value);
    }

    public override string ToString() => Value.ToString();
}
