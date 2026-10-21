namespace Orders.Domain;

/// <summary>
/// Tout ce que la commande doit savoir d'un produit au moment de son ajout : rien n'est recherché ensuite.
/// Construit par l'Application depuis le Catalog ; <see cref="TenantId"/> doit être le tenant du produit lui-même.
/// </summary>
public sealed class ProductSnapshot : ValueObject
{
    public const int MaxNameLength = 200;
    public const int MaxVariantNameLength = 120;

    /// <summary>
    /// Prix unitaire maximal, en unités principales de la devise. Une ligne plafonne son prix unitaire avec options à
    /// la même valeur, pour que la plus grosse commande (lignes max x quantité max x ce prix, plus autant de taxe)
    /// reste sous <see cref="Money.MaxAbsoluteAmount"/> : aucun vrai panier ne peut déclencher l'erreur de plage du noyau.
    /// </summary>
    public const decimal MaxUnitPriceAmount = 100_000_000m;

    public const int MaxComponents = 20;

    private readonly List<OrderItemModifier> _modifiers;
    private readonly List<OrderItemComponent> _components;

    private ProductSnapshot(
        TenantId tenantId,
        ProductId productId,
        string name,
        Money unitPrice,
        TaxSnapshot tax,
        List<OrderItemModifier> modifiers,
        VariantId? variantId,
        string? variantName,
        List<OrderItemComponent> components)
    {
        TenantId = tenantId;
        _components = components;
        ProductId = productId;
        Name = name;
        UnitPrice = unitPrice;
        Tax = tax;
        _modifiers = modifiers;
        VariantId = variantId;
        VariantName = variantName;
    }

    public TenantId TenantId { get; }

    public ProductId ProductId { get; }

    public string Name { get; }

    /// <summary>La variante vendue (taille, cuisson...), quand le produit a des variantes.</summary>
    public VariantId? VariantId { get; }

    /// <summary>Présent exactement quand <see cref="VariantId"/> l'est.</summary>
    public string? VariantName { get; }

    /// <summary>
    /// Prix d'une unité avant options, delta de la variante déjà inclus (calculé par le Catalog) ;
    /// taxe incluse ou non selon le <see cref="PriceTaxMode"/> de la commande.
    /// </summary>
    public Money UnitPrice { get; }

    public TaxSnapshot Tax { get; }

    public IReadOnlyCollection<OrderItemModifier> Modifiers => _modifiers.AsReadOnly();

    /// <summary>Produits choisis dans une formule (vide pour un produit ordinaire).</summary>
    public IReadOnlyCollection<OrderItemComponent> Components => _components.AsReadOnly();

    public static ProductSnapshot Create(
        TenantId tenantId,
        ProductId productId,
        string? name,
        Money unitPrice,
        TaxSnapshot tax,
        IEnumerable<OrderItemModifier>? modifiers = null,
        VariantId? variantId = null,
        string? variantName = null,
        IEnumerable<OrderItemComponent>? components = null)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);
        ArgumentNullException.ThrowIfNull(tax);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(productId);
        var trimmed = name?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength, OrderErrors.ProductNameInvalid);
        DomainException.ThrowIf(unitPrice.IsNegative, OrderErrors.ProductUnitPriceNegative);
        DomainException.ThrowIf(unitPrice.Amount > MaxUnitPriceAmount, OrderErrors.ProductUnitPriceTooLarge);

        var trimmedVariantName = string.IsNullOrWhiteSpace(variantName) ? null : variantName.Trim();
        if (variantId is { } variant)
        {
            Guard.NotEmpty(variant);
            DomainException.ThrowIf(trimmedVariantName is null, OrderErrors.ProductVariantNameRequired);
            DomainException.ThrowIf(trimmedVariantName!.Length > MaxVariantNameLength, OrderErrors.ProductVariantNameTooLong);
        }
        else
        {
            DomainException.ThrowIf(trimmedVariantName is not null, OrderErrors.ProductVariantNameWithoutVariant);
        }

        var list = modifiers?.ToList() ?? [];
        foreach (var modifier in list)
        {
            ArgumentNullException.ThrowIfNull(modifier, nameof(modifiers));
        }

        var parts = components?.ToList() ?? [];
        DomainException.ThrowIf(parts.Count > MaxComponents, OrderErrors.ItemTooManyComponents);
        foreach (var component in parts)
        {
            ArgumentNullException.ThrowIfNull(component, nameof(components));
            DomainException.ThrowIf(component.Supplement.Currency != unitPrice.Currency, OrderErrors.CurrencyMismatch);
        }

        return new ProductSnapshot(tenantId, productId, trimmed!, unitPrice, tax, list, variantId, trimmedVariantName, parts);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return TenantId;
        yield return ProductId;
        yield return Name;
        yield return UnitPrice;
        yield return Tax;
        yield return VariantId;
        yield return VariantName;
        foreach (var modifier in _modifiers)
        {
            yield return modifier;
        }

        foreach (var component in _components)
        {
            yield return component;
        }
    }
}
