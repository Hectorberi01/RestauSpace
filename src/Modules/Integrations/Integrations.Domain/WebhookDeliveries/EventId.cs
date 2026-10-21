namespace Integrations.Domain;

public readonly record struct EventId
{
    private EventId(Guid value) => Value = value;

    public Guid Value { get; }

    public static EventId New() => new(Guid.CreateVersion7());

    public static EventId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new EventId(value);
    }

    public override string ToString() => Value.ToString();
}
