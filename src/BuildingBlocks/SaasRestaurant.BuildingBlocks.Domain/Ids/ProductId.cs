namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct ProductId
{
    private ProductId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ProductId New() => new(Guid.CreateVersion7());

    public static ProductId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ProductId(value);
    }

    public override string ToString() => Value.ToString();
}
