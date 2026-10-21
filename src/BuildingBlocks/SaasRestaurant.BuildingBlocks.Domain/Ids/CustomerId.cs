namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct CustomerId
{
    private CustomerId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CustomerId New() => new(Guid.CreateVersion7());

    public static CustomerId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new CustomerId(value);
    }

    public override string ToString() => Value.ToString();
}
