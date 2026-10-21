namespace SaasRestaurant.BuildingBlocks.Domain;

public readonly record struct TenantId
{
    private TenantId(Guid value) => Value = value;

    public Guid Value { get; }

    public static TenantId New() => new(Guid.CreateVersion7());

    public static TenantId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new TenantId(value);
    }

    public override string ToString() => Value.ToString();
}
