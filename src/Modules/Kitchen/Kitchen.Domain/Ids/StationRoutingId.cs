namespace Kitchen.Domain;

public readonly record struct StationRoutingId
{
    private StationRoutingId(Guid value) => Value = value;

    public Guid Value { get; }

    public static StationRoutingId New() => new(Guid.CreateVersion7());

    public static StationRoutingId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new StationRoutingId(value);
    }

    public override string ToString() => Value.ToString();
}
