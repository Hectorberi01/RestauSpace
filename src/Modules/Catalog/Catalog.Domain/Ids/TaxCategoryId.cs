namespace Catalog.Domain;

public readonly record struct TaxCategoryId
{
    private TaxCategoryId(Guid value) => Value = value;

    public Guid Value { get; }

    public static TaxCategoryId New() => new(Guid.CreateVersion7());

    public static TaxCategoryId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new TaxCategoryId(value);
    }

    public override string ToString() => Value.ToString();
}
