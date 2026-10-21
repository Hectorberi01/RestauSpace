namespace Catalog.Domain;

/// <summary>Partie nommée d'un menu (« Entrées », « Desserts »...) : ids des produits, dans l'ordre d'affichage.</summary>
public sealed class MenuSection : Entity<MenuSectionId>
{
    // Pas readonly : l'ORM le pose au chargement (stocké en une seule valeur jsonb). Jamais réassigné par le domaine.
    private List<ProductId> _productIds = [];

    private MenuSection(MenuSectionId id, CatalogName name) : base(id) => Name = name;

    // Réservé à l'ORM.
    private MenuSection() => Name = null!;

    public CatalogName Name { get; private set; }

    /// <summary>Produits dans l'ordre d'affichage.</summary>
    public IReadOnlyCollection<ProductId> ProductIds => _productIds.AsReadOnly();

    public bool IsEmpty => _productIds.Count == 0;

    internal static MenuSection Create(CatalogName name) => new(MenuSectionId.New(), name);

    internal void Rename(CatalogName name) => Name = name;

    internal void AddProduct(ProductId productId)
    {
        DomainException.ThrowIf(_productIds.Contains(productId), MenuErrors.ProductAlreadyInSection);
        DomainException.ThrowIf(_productIds.Count >= Menu.MaxProductsPerSection, MenuErrors.TooManyProductsInSection);
        _productIds.Add(productId);
    }

    internal void RemoveProduct(ProductId productId) =>
        DomainException.ThrowIf(!_productIds.Remove(productId), MenuErrors.ProductNotInSection);

    internal void MoveProduct(ProductId productId, int newPosition)
    {
        DomainException.ThrowIf(!_productIds.Contains(productId), MenuErrors.ProductNotInSection);
        DomainException.ThrowIf(newPosition < 0 || newPosition >= _productIds.Count, MenuErrors.PositionOutOfRange);

        _productIds.Remove(productId);
        _productIds.Insert(newPosition, productId);
    }
}
