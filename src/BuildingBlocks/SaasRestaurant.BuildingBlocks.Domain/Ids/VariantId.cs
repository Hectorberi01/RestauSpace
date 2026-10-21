namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct VariantId
{
    private VariantId(Guid value) => Value = value;

    public Guid Value { get; }

    public static VariantId New() => new(Guid.CreateVersion7());

    public static VariantId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new VariantId(value);
    }

    public override string ToString() => Value.ToString();
}
