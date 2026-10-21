namespace Inventory.Domain;

/// <summary>
/// Instantané d'un ingrédient transmis aux agrégats qui le référencent par id (articles de stock, recettes).
/// Obtenable uniquement à partir d'un vrai ingrédient via <see cref="Ingredient.ToReference"/> : un appelant ne peut
/// pas forger un tenant, une unité de base ni un indicateur « actif ».
/// </summary>
public sealed class IngredientReference : ValueObject
{
    internal IngredientReference(IngredientId ingredientId, TenantId tenantId, UnitOfMeasure baseUnit, bool isActive)
    {
        IngredientId = ingredientId;
        TenantId = tenantId;
        BaseUnit = baseUnit;
        IsActive = isActive;
    }

    public IngredientId IngredientId { get; }

    public TenantId TenantId { get; }

    public UnitOfMeasure BaseUnit { get; }

    public bool IsActive { get; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return IngredientId;
        yield return TenantId;
        yield return BaseUnit;
        yield return IsActive;
    }
}
