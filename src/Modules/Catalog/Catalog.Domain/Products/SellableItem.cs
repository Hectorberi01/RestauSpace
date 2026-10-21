namespace Catalog.Domain;

/// <summary>
/// Tout ce dont une ligne de commande a besoin sur un produit (et sa variante choisie) au moment de la vente. L'Application
/// copie ces valeurs dans le snapshot Orders ; obtenable uniquement via <see cref="Product.DescribeForSale"/>.
/// </summary>
public sealed record SellableItem
{
    internal SellableItem(
        TenantId tenantId,
        ProductId productId,
        string productName,
        VariantId? variantId,
        string? variantName,
        Money unitPrice,
        TaxCategoryId taxCategoryId)
    {
        TenantId = tenantId;
        ProductId = productId;
        ProductName = productName;
        VariantId = variantId;
        VariantName = variantName;
        UnitPrice = unitPrice;
        TaxCategoryId = taxCategoryId;
    }

    public TenantId TenantId { get; }

    public ProductId ProductId { get; }

    public string ProductName { get; }

    public VariantId? VariantId { get; }

    public string? VariantName { get; }

    /// <summary>Prix de base + delta de variante, tel que saisi au catalogue (mode de taxe : réglage du tenant).</summary>
    public Money UnitPrice { get; }

    public TaxCategoryId TaxCategoryId { get; }
}
