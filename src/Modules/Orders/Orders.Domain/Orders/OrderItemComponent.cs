namespace Orders.Domain;

/// <summary>
/// Un produit choisi dans une formule, figé à l'ajout de la ligne : l'étape (« Plat »), le produit et sa variante, le
/// supplément unitaire (jamais négatif) et ses propres options. Son <see cref="Id"/> distingue ce composant pour la
/// cuisine et le stock, qui le voient comme une ligne à préparer. Construit par l'Application depuis le Catalog.
/// </summary>
public sealed class OrderItemComponent : ValueObject
{
    public const int MaxStepNameLength = 120;
    public const int MaxModifiers = 20;

    private readonly List<OrderItemModifier> _modifiers;

    private OrderItemComponent(
        Guid id,
        string stepName,
        ProductId productId,
        string productName,
        VariantId? variantId,
        string? variantName,
        Money supplement,
        List<OrderItemModifier> modifiers)
    {
        Id = id;
        StepName = stepName;
        ProductId = productId;
        ProductName = productName;
        VariantId = variantId;
        VariantName = variantName;
        Supplement = supplement;
        _modifiers = modifiers;
    }

    public Guid Id { get; }

    public string StepName { get; }

    public ProductId ProductId { get; }

    public string ProductName { get; }

    public VariantId? VariantId { get; }

    public string? VariantName { get; }

    /// <summary>Unitaire, ajouté au prix de la formule.</summary>
    public Money Supplement { get; }

    public IReadOnlyCollection<OrderItemModifier> Modifiers => _modifiers.AsReadOnly();

    /// <summary>Supplément et options du composant, pour une formule.</summary>
    public Money PriceDelta => _modifiers.Aggregate(Supplement, (price, modifier) => price + modifier.PriceDelta);

    /// <param name="id">Null : un nouveau composant ; sinon l'id relu du stockage.</param>
    public static OrderItemComponent Create(
        Guid? id,
        string? stepName,
        ProductId productId,
        string? productName,
        VariantId? variantId,
        string? variantName,
        Money supplement,
        IEnumerable<OrderItemModifier>? modifiers = null)
    {
        ArgumentNullException.ThrowIfNull(supplement);
        Guard.NotEmpty(productId);
        var step = stepName?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(step) || step.Length > MaxStepNameLength, OrderErrors.ComponentInvalid);
        var name = productName?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(name) || name.Length > ProductSnapshot.MaxNameLength, OrderErrors.ProductNameInvalid);
        var variant = string.IsNullOrWhiteSpace(variantName) ? null : variantName.Trim();
        DomainException.ThrowIf((variantId is null) != (variant is null) || variant?.Length > ProductSnapshot.MaxVariantNameLength, OrderErrors.ComponentInvalid);
        DomainException.ThrowIf(supplement.IsNegative || supplement.Amount > ProductSnapshot.MaxUnitPriceAmount, OrderErrors.ComponentInvalid);
        var list = modifiers?.ToList() ?? [];
        DomainException.ThrowIf(list.Count > MaxModifiers, OrderErrors.ItemTooManyModifiers);
        DomainException.ThrowIf(list.Any(modifier => modifier.PriceDelta.Currency != supplement.Currency), OrderErrors.CurrencyMismatch);
        DomainException.ThrowIf(list.Select(modifier => modifier.ModifierId).Distinct().Count() != list.Count, OrderErrors.ItemModifierDuplicate);
        return new OrderItemComponent(id is { } known && known != Guid.Empty ? known : Guid.CreateVersion7(), step!, productId, name!, variantId, variant, supplement, list);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Id;
        yield return StepName;
        yield return ProductId;
        yield return ProductName;
        yield return VariantId;
        yield return VariantName;
        yield return Supplement;
        foreach (var modifier in _modifiers)
        {
            yield return modifier;
        }
    }
}
