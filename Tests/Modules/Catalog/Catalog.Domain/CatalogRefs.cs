namespace Catalog.Domain;

/// <summary>Les références d'un même module sont infalsifiables : les tests les tirent de vrais agrégats.</summary>
internal static class CatalogRefs
{
    private static readonly FakeClock Clock = new();

    public static TaxCategory TaxCategory(TenantId tenant) =>
        Catalog.Domain.TaxCategory.Create(
            tenant, TaxCategoryCode.Create("VAT_STD"), TaxCategoryName.Create("Standard VAT"), TaxRate.Create(0.10m), true, Clock);

    public static TaxCategoryReference Tax(TenantId tenant, bool isArchived = false)
    {
        var category = TaxCategory(tenant);
        if (isArchived)
        {
            category.Archive(Clock);
        }

        return category.ToReference();
    }

    public static Product Product(TenantId tenant, string name = "Burger", decimal basePrice = 10m) =>
        Catalog.Domain.Product.Create(tenant, CatalogName.Create(name), null, Money.Of(basePrice, Currency.EUR), Tax(tenant), Clock);

    public static ProductReference ProductRef(TenantId tenant, bool isArchived = false)
    {
        var product = Product(tenant);
        if (isArchived)
        {
            product.Archive(Clock);
        }

        return product.ToReference();
    }

    public static ModifierGroupReference Group(TenantId tenant, Currency? currency = null) =>
        ModifierGroup.Create(tenant, CatalogName.Create("Extras"), currency ?? Currency.EUR, SelectionRule.OptionalSingle).ToReference();

    public static LocationReference Location(TenantId tenant, LocationId? locationId = null) =>
        new(tenant, locationId ?? LocationId.New(), IsOperational: true);
}
