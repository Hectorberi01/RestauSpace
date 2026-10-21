namespace Locations.Domain.Locations;

public readonly record struct ExceptionalScheduleId
{
    private ExceptionalScheduleId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ExceptionalScheduleId New() => new(Guid.CreateVersion7());

    public static ExceptionalScheduleId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ExceptionalScheduleId(value);
    }

    public override string ToString() => Value.ToString();
}
