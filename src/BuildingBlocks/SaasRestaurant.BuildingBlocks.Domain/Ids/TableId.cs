namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct TableId
{
    private TableId(Guid value) => Value = value;

    public Guid Value { get; }

    public static TableId New() => new(Guid.CreateVersion7());

    public static TableId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new TableId(value);
    }

    public override string ToString() => Value.ToString();
}
