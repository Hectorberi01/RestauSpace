namespace Storefront.Domain;

public readonly record struct SalesAppId
{
    private SalesAppId(Guid value) => Value = value;

    public Guid Value { get; }

    public static SalesAppId New() => new(Guid.CreateVersion7());

    public static SalesAppId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new SalesAppId(value);
    }

    public override string ToString() => Value.ToString();
}
