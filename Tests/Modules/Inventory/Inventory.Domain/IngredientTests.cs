namespace Inventory.Domain;

public class IngredientTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private static Ingredient NewIngredient(string name = "Flour", UnitOfMeasure baseUnit = UnitOfMeasure.Gram) =>
        Ingredient.Create(Tenant, name, baseUnit, isNameUnique: true);

    [Fact]
    public void Create_builds_an_active_ingredient_with_its_base_unit()
    {
        var ingredient = NewIngredient("  Flour T55 ", UnitOfMeasure.Kilogram);

        Assert.Equal(Tenant, ingredient.TenantId);
        Assert.Equal("Flour T55", ingredient.Name);
        Assert.Equal(UnitOfMeasure.Kilogram, ingredient.BaseUnit);
        Assert.True(ingredient.IsActive);
        Assert.NotEqual(Guid.Empty, ingredient.Id.Value);
        DomainAssert.NoEvents(ingredient);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name) =>
        DomainAssert.Throws(InventoryErrors.IngredientNameRequired, () => NewIngredient(name));

    [Fact]
    public void Create_accepts_a_name_at_the_limit_and_rejects_a_longer_one()
    {
        Assert.Equal(Ingredient.NameMaxLength, NewIngredient(new string('a', Ingredient.NameMaxLength)).Name.Length);
        DomainAssert.Throws(
            InventoryErrors.IngredientNameTooLong,
            () => NewIngredient(new string('a', Ingredient.NameMaxLength + 1)));
    }

    [Fact]
    public void Create_rejects_a_name_already_used_in_the_tenant() =>
        DomainAssert.Throws(
            InventoryErrors.IngredientNameNotUnique,
            () => Ingredient.Create(Tenant, "Flour", UnitOfMeasure.Gram, isNameUnique: false));

    [Fact]
    public void Create_rejects_an_unknown_base_unit() =>
        DomainAssert.Throws(InventoryErrors.UnitUnknown, () => NewIngredient(baseUnit: (UnitOfMeasure)0));

    [Fact]
    public void Rename_changes_the_name()
    {
        var ingredient = NewIngredient();

        ingredient.Rename(" Wheat flour ", isNameUnique: true);

        Assert.Equal("Wheat flour", ingredient.Name);
    }

    [Fact]
    public void Rename_rejects_a_name_already_used_in_the_tenant()
    {
        var ingredient = NewIngredient();

        DomainAssert.Throws(InventoryErrors.IngredientNameNotUnique, () => ingredient.Rename("Sugar", isNameUnique: false));
        Assert.Equal("Flour", ingredient.Name);
    }

    [Fact]
    public void Rename_to_the_current_name_is_a_silent_no_op()
    {
        var ingredient = NewIngredient();

        // La réponse d'unicité est sans objet quand rien ne change.
        ingredient.Rename(" Flour ", isNameUnique: false);

        Assert.Equal("Flour", ingredient.Name);
        DomainAssert.NoEvents(ingredient);
    }

    [Fact]
    public void Rename_rejects_a_blank_or_too_long_name()
    {
        var ingredient = NewIngredient();

        DomainAssert.Throws(InventoryErrors.IngredientNameRequired, () => ingredient.Rename(" ", isNameUnique: true));
        DomainAssert.Throws(
            InventoryErrors.IngredientNameTooLong,
            () => ingredient.Rename(new string('b', Ingredient.NameMaxLength + 1), isNameUnique: true));
    }

    [Fact]
    public void Rename_rejects_an_inactive_ingredient()
    {
        var ingredient = NewIngredient();
        ingredient.Deactivate();

        DomainAssert.Throws(InventoryErrors.IngredientInactive, () => ingredient.Rename("Sugar", isNameUnique: true));
    }

    [Fact]
    public void Deactivate_makes_the_ingredient_inactive()
    {
        var ingredient = NewIngredient();

        ingredient.Deactivate();

        Assert.False(ingredient.IsActive);
    }

    [Fact]
    public void Deactivate_rejects_an_already_inactive_ingredient()
    {
        var ingredient = NewIngredient();
        ingredient.Deactivate();

        DomainAssert.Throws(InventoryErrors.IngredientAlreadyInactive, ingredient.Deactivate);
    }

    [Fact]
    public void Base_unit_cannot_be_changed_after_creation()
    {
        var setter = typeof(Ingredient).GetProperty(nameof(Ingredient.BaseUnit))!.SetMethod;
        Assert.True(setter is null || setter.IsPrivate);

        var ingredient = NewIngredient(baseUnit: UnitOfMeasure.Liter);
        ingredient.Rename("Olive oil", isNameUnique: true);
        ingredient.Deactivate();

        Assert.Equal(UnitOfMeasure.Liter, ingredient.BaseUnit);
    }

    [Fact]
    public void ToReference_snapshots_identity_tenant_unit_and_activity()
    {
        var ingredient = NewIngredient(baseUnit: UnitOfMeasure.Milliliter);

        var active = ingredient.ToReference();
        ingredient.Deactivate();
        var inactive = ingredient.ToReference();

        Assert.Equal(
            (ingredient.Id, Tenant, UnitOfMeasure.Milliliter, true),
            (active.IngredientId, active.TenantId, active.BaseUnit, active.IsActive));
        Assert.False(inactive.IsActive);
        Assert.NotEqual(active, inactive);
    }

    [Fact]
    public void Reference_cannot_be_built_outside_the_module()
    {
        Assert.DoesNotContain(typeof(IngredientReference).GetConstructors(), constructor => constructor.IsPublic);
        Assert.All(typeof(IngredientReference).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void Create_rejects_an_empty_tenant() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => Ingredient.Create(default, "Flour", UnitOfMeasure.Gram, isNameUnique: true));
}
