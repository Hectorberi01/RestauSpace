namespace Catalog.Domain;

/// <summary>Déclinaison vendable d'un produit (taille, format...). Son prix est le prix de base du produit plus <see cref="PriceDelta"/>.</summary>
public sealed class ProductVariant : Entity<VariantId>
{
    private ProductVariant(VariantId id, CatalogName name, Money priceDelta, Sku? sku) : base(id)
    {
        Name = name;
        PriceDelta = priceDelta;
        Sku = sku;
        IsAvailable = true;
    }

    // Réservé à l'ORM.
    private ProductVariant()
    {
        Name = null!;
        PriceDelta = null!;
    }

    public CatalogName Name { get; private set; }

    /// <summary>Peut être négatif (variante moins chère) ; le produit parent garantit prix de base + delta >= 0.</summary>
    public Money PriceDelta { get; private set; }

    public Sku? Sku { get; private set; }

    public bool IsAvailable { get; private set; }

    internal static ProductVariant Create(CatalogName name, Money priceDelta, Sku? sku) =>
        new(VariantId.New(), name, priceDelta, sku);

    internal void ChangePriceDelta(Money priceDelta) => PriceDelta = priceDelta;

    internal void SetAvailability(bool isAvailable) => IsAvailable = isAvailable;
}
