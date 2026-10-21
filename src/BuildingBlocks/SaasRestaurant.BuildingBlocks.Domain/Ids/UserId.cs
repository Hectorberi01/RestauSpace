namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct UserId
{
    private UserId(Guid value) => Value = value;

    public Guid Value { get; }

    public static UserId New() => new(Guid.CreateVersion7());

    public static UserId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new UserId(value);
    }

    public override string ToString() => Value.ToString();
}
