namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct TableSessionId
{
    private TableSessionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static TableSessionId New() => new(Guid.CreateVersion7());

    public static TableSessionId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new TableSessionId(value);
    }

    public override string ToString() => Value.ToString();
}
