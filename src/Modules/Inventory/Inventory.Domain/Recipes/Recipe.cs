namespace Inventory.Domain;

/// <summary>
/// Composition, en ingrédients, d'une unité vendue d'un produit — ou de l'une de ses variantes quand
/// <see cref="VariantId"/> est renseigné (une grande pizza n'utilise pas la pâte d'une petite).
/// </summary>
public sealed class Recipe : AggregateRoot<RecipeId>, ITenantOwned
{
    public const int MaxIngredients = 100;

    private readonly List<RecipeComponent> _ingredients = [];

    private Recipe(RecipeId id, TenantId tenantId, ProductId productId, VariantId? variantId) : base(id)
    {
        TenantId = tenantId;
        ProductId = productId;
        VariantId = variantId;
    }

    // Réservé à l'ORM.
    private Recipe()
    {
    }

    public TenantId TenantId { get; private set; }

    public ProductId ProductId { get; private set; }

    /// <summary>Null pour la recette au niveau produit ; renseigné quand la recette est propre à une variante
    /// du produit.</summary>
    public VariantId? VariantId { get; private set; }

    public IReadOnlyCollection<RecipeComponent> Ingredients => _ingredients.AsReadOnly();

    /// <summary>
    /// <paramref name="isUniqueForProduct"/> provient de <see cref="IRecipeUniquenessChecker"/> et couvre le
    /// couple (produit, variante) : une recette au niveau produit, et au plus une par variante.
    /// </summary>
    public static Recipe Create(TenantId tenantId, ProductReference product, bool isUniqueForProduct, VariantId? variantId = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        Guard.NotEmpty(tenantId);
        if (variantId is not null)
        {
            Guard.NotEmpty(variantId.Value);
        }

        var recipe = new Recipe(RecipeId.New(), tenantId, product.ProductId, variantId);
        recipe.EnsureSameTenant(product.TenantId);
        DomainException.ThrowIf(!isUniqueForProduct, InventoryErrors.RecipeNotUniqueForProduct);
        return recipe;
    }

    public void AddIngredient(IngredientReference ingredient, Quantity quantity, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(ingredient);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(clock);

        this.EnsureSameTenant(ingredient.TenantId);
        DomainException.ThrowIf(!ingredient.IsActive, InventoryErrors.IngredientInactive);
        DomainException.ThrowIf(!quantity.IsCompatibleWith(ingredient.BaseUnit), InventoryErrors.UnitIncompatible);
        DomainException.ThrowIf(!quantity.IsPositive, InventoryErrors.RecipeQuantityNotPositive);
        DomainException.ThrowIf(
            _ingredients.Any(component => component.IngredientId == ingredient.IngredientId),
            InventoryErrors.RecipeIngredientDuplicate);
        DomainException.ThrowIf(_ingredients.Count >= MaxIngredients, InventoryErrors.RecipeTooManyIngredients);

        _ingredients.Add(new RecipeComponent(ingredient.IngredientId, quantity));
        RaiseChanged(RecipeChangeType.IngredientAdded, ingredient.IngredientId, quantity, clock);
    }

    public void ChangeQuantity(IngredientId ingredientId, Quantity quantity, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(clock);

        var index = IndexOf(ingredientId);
        var current = _ingredients[index];
        // Le composant existant a déjà validé son unité face à l'ingrédient : rester dans sa dimension suffit.
        DomainException.ThrowIf(!quantity.IsCompatibleWith(current.Quantity.Unit), InventoryErrors.UnitIncompatible);
        DomainException.ThrowIf(!quantity.IsPositive, InventoryErrors.RecipeQuantityNotPositive);
        if (quantity == current.Quantity)
        {
            return;
        }

        _ingredients[index] = new RecipeComponent(ingredientId, quantity);
        RaiseChanged(RecipeChangeType.QuantityChanged, ingredientId, quantity, clock);
    }

    public void RemoveIngredient(IngredientId ingredientId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _ingredients.RemoveAt(IndexOf(ingredientId));
        RaiseChanged(RecipeChangeType.IngredientRemoved, ingredientId, null, clock);
    }

    /// <summary>Pure : ce que consomme la vente de <paramref name="unitsSold"/> unités du produit,
    /// par ingrédient.</summary>
    public IReadOnlyCollection<RecipeComponent> ComputeConsumption(int unitsSold)
    {
        DomainException.ThrowIf(unitsSold <= 0, InventoryErrors.RecipeUnitsSoldNotPositive);

        return _ingredients
            .Select(component => new RecipeComponent(component.IngredientId, component.Quantity.Multiply(unitsSold)))
            .ToList()
            .AsReadOnly();
    }

    private int IndexOf(IngredientId ingredientId)
    {
        var index = _ingredients.FindIndex(component => component.IngredientId == ingredientId);
        DomainException.ThrowIf(index < 0, InventoryErrors.RecipeIngredientNotFound);
        return index;
    }

    private void RaiseChanged(RecipeChangeType changeType, IngredientId ingredientId, Quantity? quantity, IClock clock) =>
        Raise(new RecipeChanged(TenantId, Id, ProductId, VariantId, changeType, ingredientId, quantity, clock.UtcNow));
}
