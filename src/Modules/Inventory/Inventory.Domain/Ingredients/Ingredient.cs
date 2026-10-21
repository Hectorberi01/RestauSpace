namespace Inventory.Domain;

public sealed class Ingredient : AggregateRoot<IngredientId>, ITenantOwned
{
    public const int NameMaxLength = 100;

    private Ingredient(IngredientId id, TenantId tenantId, string name, UnitOfMeasure baseUnit) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        BaseUnit = baseUnit;
        IsActive = true;
    }

    // Réservé à l'ORM.
    private Ingredient()
    {
    }

    public TenantId TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    // Volontairement sans mutateur : articles de stock, registres et recettes sont exprimés en unités de cette dimension.
    public UnitOfMeasure BaseUnit { get; private set; }

    public bool IsActive { get; private set; }

    public static Ingredient Create(TenantId tenantId, string name, UnitOfMeasure baseUnit, bool isNameUnique)
    {
        Guard.NotEmpty(tenantId);
        var normalizedName = NormalizeName(name);
        DomainException.ThrowIf(!Enum.IsDefined(baseUnit), InventoryErrors.UnitUnknown);
        DomainException.ThrowIf(!isNameUnique, InventoryErrors.IngredientNameNotUnique);

        return new Ingredient(IngredientId.New(), tenantId, normalizedName, baseUnit);
    }

    public void Rename(string name, bool isNameUnique)
    {
        DomainException.ThrowIf(!IsActive, InventoryErrors.IngredientInactive);

        var normalizedName = NormalizeName(name);
        if (normalizedName == Name)
        {
            return;
        }

        DomainException.ThrowIf(!isNameUnique, InventoryErrors.IngredientNameNotUnique);

        Name = normalizedName;
    }

    public void Deactivate()
    {
        DomainException.ThrowIf(!IsActive, InventoryErrors.IngredientAlreadyInactive);
        IsActive = false;
    }

    public IngredientReference ToReference() => new(Id, TenantId, BaseUnit, IsActive);

    private static string NormalizeName(string name)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), InventoryErrors.IngredientNameRequired);

        var normalized = name.Trim();
        DomainException.ThrowIf(normalized.Length > NameMaxLength, InventoryErrors.IngredientNameTooLong);

        return normalized;
    }
}
