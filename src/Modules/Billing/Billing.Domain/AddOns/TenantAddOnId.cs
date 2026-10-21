namespace Billing.Domain;

public readonly record struct TenantAddOnId
{
    private TenantAddOnId(Guid value) => Value = value;

    public Guid Value { get; }

    public static TenantAddOnId New() => new(Guid.CreateVersion7());

    public static TenantAddOnId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new TenantAddOnId(value);
    }

    public override string ToString() => Value.ToString();
}
