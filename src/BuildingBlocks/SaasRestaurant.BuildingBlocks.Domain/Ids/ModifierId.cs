namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct ModifierId
{
    private ModifierId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ModifierId New() => new(Guid.CreateVersion7());

    public static ModifierId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ModifierId(value);
    }

    public override string ToString() => Value.ToString();
}
