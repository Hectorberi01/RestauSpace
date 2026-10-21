namespace Notifications.Domain;

public readonly record struct DeviceRegistrationId
{
    private DeviceRegistrationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static DeviceRegistrationId New() => new(Guid.CreateVersion7());

    public static DeviceRegistrationId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new DeviceRegistrationId(value);
    }

    public override string ToString() => Value.ToString();
}
