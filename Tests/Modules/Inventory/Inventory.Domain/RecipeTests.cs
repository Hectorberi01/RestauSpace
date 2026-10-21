namespace Inventory.Domain;

public class RecipeTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly ProductId Product = ProductId.New();

    private readonly FakeClock _clock = new();

    private static Quantity G(decimal amount) => Quantity.Create(amount, UnitOfMeasure.Gram);

    private static readonly ProductReference Dish = new(Tenant, Product);

    // Une IngredientReference ne se construit pas à la main : chaque test passe par un vrai ingrédient.
    private static IngredientReference Ingredient(
        UnitOfMeasure baseUnit = UnitOfMeasure.Gram,
        TenantId? tenantId = null,
        bool isActive = true)
    {
        var ingredient = Domain.Ingredient.Create(tenantId ?? Tenant, "Flour", baseUnit, isNameUnique: true);
        if (!isActive)
        {
            ingredient.Deactivate();
        }

        return ingredient.ToReference();
    }

    private static Recipe NewRecipe() => Recipe.Create(Tenant, Dish, isUniqueForProduct: true);

    private Recipe NewRecipeWith(IngredientReference ingredient, Quantity quantity)
    {
        var recipe = NewRecipe();
        recipe.AddIngredient(ingredient, quantity, _clock);
        recipe.ClearDomainEvents();
        return recipe;
    }

    [Fact]
    public void Create_builds_an_empty_recipe_for_the_product()
    {
        var recipe = NewRecipe();

        Assert.Equal(Tenant, recipe.TenantId);
        Assert.Equal(Product, recipe.ProductId);
        Assert.Null(recipe.VariantId);
        Assert.Empty(recipe.Ingredients);
        DomainAssert.NoEvents(recipe);
    }

    [Fact]
    public void Create_can_target_one_variant_of_the_product()
    {
        var large = VariantId.New();
        var flour = Ingredient();

        var recipe = Recipe.Create(Tenant, Dish, isUniqueForProduct: true, large);
        recipe.AddIngredient(flour, G(400m), _clock);

        Assert.Equal((Product, (VariantId?)large), (recipe.ProductId, recipe.VariantId));
        Assert.Equal(large, DomainAssert.Raised<RecipeChanged>(recipe).VariantId);
        // La consommation se calcule de la même façon, quelle que soit la cible de la recette.
        Assert.Equal(G(1200m), Assert.Single(recipe.ComputeConsumption(3)).Quantity);
    }

    [Fact]
    public void Create_rejects_a_second_recipe_for_the_same_product_and_variant() =>
        DomainAssert.Throws(
            InventoryErrors.RecipeNotUniqueForProduct,
            () => Recipe.Create(Tenant, Dish, isUniqueForProduct: false, VariantId.New()));

    [Fact]
    public void Create_rejects_empty_ids()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => Recipe.Create(default, Dish, isUniqueForProduct: true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => Recipe.Create(Tenant, Dish, isUniqueForProduct: true, default(VariantId)));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new ProductReference(Tenant, default));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new ProductReference(default, Product));
    }

    [Fact]
    public void Create_rejects_a_product_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Recipe.Create(Tenant, new ProductReference(TenantId.New(), Product), isUniqueForProduct: true));

    [Fact]
    public void Create_rejects_a_second_recipe_for_the_same_product() =>
        DomainAssert.Throws(
            InventoryErrors.RecipeNotUniqueForProduct,
            () => Recipe.Create(Tenant, Dish, isUniqueForProduct: false));

    [Fact]
    public void AddIngredient_adds_the_component_and_raises_RecipeChanged()
    {
        var recipe = NewRecipe();
        var flour = Ingredient();

        recipe.AddIngredient(flour, G(250m), _clock);

        var component = Assert.Single(recipe.Ingredients);
        Assert.Equal(flour.IngredientId, component.IngredientId);
        Assert.Equal(G(250m), component.Quantity);
        Assert.Equal(
            new RecipeChanged(Tenant, recipe.Id, Product, null, RecipeChangeType.IngredientAdded, flour.IngredientId, G(250m), _clock.UtcNow),
            DomainAssert.Raised<RecipeChanged>(recipe));
    }

    [Fact]
    public void AddIngredient_accepts_a_unit_compatible_with_the_ingredient_base_unit()
    {
        var recipe = NewRecipe();

        recipe.AddIngredient(Ingredient(UnitOfMeasure.Gram), Quantity.Create(0.25m, UnitOfMeasure.Kilogram), _clock);

        Assert.Equal(UnitOfMeasure.Kilogram, Assert.Single(recipe.Ingredients).Quantity.Unit);
    }

    [Fact]
    public void AddIngredient_rejects_a_duplicate_ingredient()
    {
        var flour = Ingredient();
        var recipe = NewRecipeWith(flour, G(250m));

        DomainAssert.Throws(InventoryErrors.RecipeIngredientDuplicate, () => recipe.AddIngredient(flour, G(100m), _clock));
        DomainAssert.Throws(
            InventoryErrors.RecipeIngredientDuplicate,
            () => recipe.AddIngredient(flour, Quantity.Create(1m, UnitOfMeasure.Kilogram), _clock));

        Assert.Equal(G(250m), Assert.Single(recipe.Ingredients).Quantity);
        DomainAssert.NoEvents(recipe);
    }

    [Fact]
    public void AddIngredient_rejects_a_zero_quantity()
    {
        var recipe = NewRecipe();

        DomainAssert.Throws(InventoryErrors.RecipeQuantityNotPositive, () => recipe.AddIngredient(Ingredient(), G(0m), _clock));
        Assert.Empty(recipe.Ingredients);
        DomainAssert.NoEvents(recipe);
    }

    [Fact]
    public void AddIngredient_rejects_a_unit_incompatible_with_the_ingredient_base_unit()
    {
        var recipe = NewRecipe();

        DomainAssert.Throws(
            InventoryErrors.UnitIncompatible,
            () => recipe.AddIngredient(Ingredient(UnitOfMeasure.Liter), G(10m), _clock));
    }

    [Fact]
    public void AddIngredient_rejects_an_ingredient_of_another_tenant()
    {
        var recipe = NewRecipe();

        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => recipe.AddIngredient(Ingredient(tenantId: TenantId.New()), G(10m), _clock));
    }

    [Fact]
    public void AddIngredient_rejects_an_inactive_ingredient()
    {
        var recipe = NewRecipe();

        DomainAssert.Throws(
            InventoryErrors.IngredientInactive,
            () => recipe.AddIngredient(Ingredient(isActive: false), G(10m), _clock));
    }

    [Fact]
    public void AddIngredient_rejects_more_ingredients_than_the_maximum()
    {
        var recipe = NewRecipe();
        for (var index = 0; index < Recipe.MaxIngredients; index++)
        {
            recipe.AddIngredient(Ingredient(), G(1m), _clock);
        }

        DomainAssert.Throws(InventoryErrors.RecipeTooManyIngredients, () => recipe.AddIngredient(Ingredient(), G(1m), _clock));
        Assert.Equal(Recipe.MaxIngredients, recipe.Ingredients.Count);
    }

    [Fact]
    public void ChangeQuantity_replaces_the_quantity_and_raises_RecipeChanged()
    {
        var flour = Ingredient();
        var recipe = NewRecipeWith(flour, G(250m));
        _clock.Advance(TimeSpan.FromMinutes(1));

        recipe.ChangeQuantity(flour.IngredientId, G(300m), _clock);

        Assert.Equal(G(300m), Assert.Single(recipe.Ingredients).Quantity);
        Assert.Equal(
            new RecipeChanged(Tenant, recipe.Id, Product, null, RecipeChangeType.QuantityChanged, flour.IngredientId, G(300m), _clock.UtcNow),
            DomainAssert.Raised<RecipeChanged>(recipe));
    }

    [Fact]
    public void ChangeQuantity_accepts_another_unit_of_the_same_dimension()
    {
        var flour = Ingredient();
        var recipe = NewRecipeWith(flour, G(250m));

        recipe.ChangeQuantity(flour.IngredientId, Quantity.Create(0.3m, UnitOfMeasure.Kilogram), _clock);

        Assert.Equal(Quantity.Create(0.3m, UnitOfMeasure.Kilogram), Assert.Single(recipe.Ingredients).Quantity);
    }

    [Fact]
    public void ChangeQuantity_rejects_a_unit_of_another_dimension()
    {
        var flour = Ingredient();
        var recipe = NewRecipeWith(flour, G(250m));

        DomainAssert.Throws(
            InventoryErrors.UnitIncompatible,
            () => recipe.ChangeQuantity(flour.IngredientId, Quantity.Create(1m, UnitOfMeasure.Liter), _clock));
    }

    [Fact]
    public void ChangeQuantity_rejects_a_zero_quantity()
    {
        var flour = Ingredient();
        var recipe = NewRecipeWith(flour, G(250m));

        DomainAssert.Throws(InventoryErrors.RecipeQuantityNotPositive, () => recipe.ChangeQuantity(flour.IngredientId, G(0m), _clock));
        Assert.Equal(G(250m), Assert.Single(recipe.Ingredients).Quantity);
        DomainAssert.NoEvents(recipe);
    }

    [Fact]
    public void ChangeQuantity_to_the_same_quantity_is_a_silent_no_op()
    {
        var flour = Ingredient();
        var recipe = NewRecipeWith(flour, G(250m));

        recipe.ChangeQuantity(flour.IngredientId, G(250m), _clock);

        Assert.Equal(G(250m), Assert.Single(recipe.Ingredients).Quantity);
        DomainAssert.NoEvents(recipe);
    }

    [Fact]
    public void ChangeQuantity_rejects_an_ingredient_that_is_not_in_the_recipe()
    {
        var recipe = NewRecipeWith(Ingredient(), G(250m));

        DomainAssert.Throws(InventoryErrors.RecipeIngredientNotFound, () => recipe.ChangeQuantity(IngredientId.New(), G(1m), _clock));
    }

    [Fact]
    public void RemoveIngredient_removes_the_component_and_raises_RecipeChanged()
    {
        var flour = Ingredient();
        var recipe = NewRecipeWith(flour, G(250m));

        recipe.RemoveIngredient(flour.IngredientId, _clock);

        Assert.Empty(recipe.Ingredients);
        Assert.Equal(
            new RecipeChanged(Tenant, recipe.Id, Product, null, RecipeChangeType.IngredientRemoved, flour.IngredientId, null, _clock.UtcNow),
            DomainAssert.Raised<RecipeChanged>(recipe));
    }

    [Fact]
    public void RemoveIngredient_rejects_an_ingredient_that_is_not_in_the_recipe()
    {
        var recipe = NewRecipeWith(Ingredient(), G(250m));

        DomainAssert.Throws(InventoryErrors.RecipeIngredientNotFound, () => recipe.RemoveIngredient(IngredientId.New(), _clock));
        Assert.Single(recipe.Ingredients);
        DomainAssert.NoEvents(recipe);
    }

    [Fact]
    public void A_removed_ingredient_can_be_added_again()
    {
        var flour = Ingredient();
        var recipe = NewRecipeWith(flour, G(250m));
        recipe.RemoveIngredient(flour.IngredientId, _clock);

        recipe.AddIngredient(flour, G(200m), _clock);

        Assert.Equal(G(200m), Assert.Single(recipe.Ingredients).Quantity);
    }

    [Fact]
    public void Every_change_raises_its_own_RecipeChanged()
    {
        var recipe = NewRecipe();
        var flour = Ingredient();

        recipe.AddIngredient(flour, G(250m), _clock);
        recipe.ChangeQuantity(flour.IngredientId, G(300m), _clock);
        recipe.RemoveIngredient(flour.IngredientId, _clock);

        Assert.Equal(
            [RecipeChangeType.IngredientAdded, RecipeChangeType.QuantityChanged, RecipeChangeType.IngredientRemoved],
            recipe.DomainEvents.OfType<RecipeChanged>().Select(changed => changed.ChangeType));
    }

    [Fact]
    public void ComputeConsumption_multiplies_each_component_by_the_units_sold()
    {
        var flour = Ingredient();
        var milk = Ingredient(UnitOfMeasure.Milliliter);
        var egg = Ingredient(UnitOfMeasure.Piece);
        var recipe = NewRecipe();
        recipe.AddIngredient(flour, G(125.5m), _clock);
        recipe.AddIngredient(milk, Quantity.Create(0.2m, UnitOfMeasure.Liter), _clock);
        recipe.AddIngredient(egg, Quantity.Create(2m, UnitOfMeasure.Piece), _clock);

        var consumption = recipe.ComputeConsumption(3).ToDictionary(line => line.IngredientId, line => line.Quantity);

        Assert.Equal(3, consumption.Count);
        Assert.Equal(G(376.5m), consumption[flour.IngredientId]);
        Assert.Equal(Quantity.Create(0.6m, UnitOfMeasure.Liter), consumption[milk.IngredientId]);
        Assert.Equal(Quantity.Create(6m, UnitOfMeasure.Piece), consumption[egg.IngredientId]);
    }

    [Fact]
    public void ComputeConsumption_for_one_unit_is_the_recipe_itself()
    {
        var recipe = NewRecipeWith(Ingredient(), G(250m));

        Assert.Equal(recipe.Ingredients, recipe.ComputeConsumption(1));
    }

    [Fact]
    public void ComputeConsumption_is_pure()
    {
        var recipe = NewRecipeWith(Ingredient(), G(250m));

        recipe.ComputeConsumption(10);

        Assert.Equal(G(250m), Assert.Single(recipe.Ingredients).Quantity);
        DomainAssert.NoEvents(recipe);
    }

    [Fact]
    public void ComputeConsumption_of_an_empty_recipe_is_empty() =>
        Assert.Empty(NewRecipe().ComputeConsumption(5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ComputeConsumption_rejects_non_positive_units_sold(int unitsSold)
    {
        var recipe = NewRecipeWith(Ingredient(), G(250m));

        DomainAssert.Throws(InventoryErrors.RecipeUnitsSoldNotPositive, () => recipe.ComputeConsumption(unitsSold));
    }

    [Fact]
    public void ComputeConsumption_keeps_the_quantity_bound()
    {
        var recipe = NewRecipeWith(Ingredient(), G(Quantity.MaxAmount / 2m));

        Assert.Equal(G(Quantity.MaxAmount), Assert.Single(recipe.ComputeConsumption(2)).Quantity);
        DomainAssert.Throws(InventoryErrors.QuantityTooLarge, () => recipe.ComputeConsumption(3));
    }
}
