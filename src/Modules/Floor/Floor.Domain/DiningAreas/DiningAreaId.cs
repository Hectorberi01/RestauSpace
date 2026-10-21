namespace Floor.Domain;

public readonly record struct DiningAreaId
{
    private DiningAreaId(Guid value) => Value = value;

    public Guid Value { get; }

    public static DiningAreaId New() => new(Guid.CreateVersion7());

    public static DiningAreaId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new DiningAreaId(value);
    }

    public override string ToString() => Value.ToString();
}
