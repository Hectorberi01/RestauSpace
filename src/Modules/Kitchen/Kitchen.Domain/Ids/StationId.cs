namespace Kitchen.Domain;

public readonly record struct StationId
{
    private StationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static StationId New() => new(Guid.CreateVersion7());

    public static StationId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new StationId(value);
    }

    public override string ToString() => Value.ToString();
}
