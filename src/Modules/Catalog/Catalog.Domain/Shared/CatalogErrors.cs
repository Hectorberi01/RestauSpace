namespace Catalog.Domain;

/// <summary>Erreurs des value objects partagés par les agrégats du catalogue.</summary>
public static class CatalogErrors
{
    public static readonly DomainError NameEmpty = new("catalog.name.empty", "A name is required.");
    public static readonly DomainError NameTooLong = new("catalog.name.too_long", "A name cannot exceed 120 characters.");
    public static readonly DomainError SkuInvalid = new("catalog.sku.invalid", "A SKU is 1 to 64 characters among letters, digits, '-', '_' and '.'.");
}
