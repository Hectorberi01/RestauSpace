namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct LocationId
{
    private LocationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static LocationId New() => new(Guid.CreateVersion7());

    public static LocationId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new LocationId(value);
    }

    public override string ToString() => Value.ToString();
}
