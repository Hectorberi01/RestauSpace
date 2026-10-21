using Inventory.Application;
using Inventory.Application.Abstractions;
using Inventory.Domain;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure;

public sealed class InventoryPersistenceTests(InventoryDatabase database) : IClassFixture<InventoryDatabase>
{
    private FakeClock Clock => database.Clock;

    private static Quantity G(decimal amount) => Quantity.Create(amount, UnitOfMeasure.Gram);

    private static Quantity Kg(decimal amount) => Quantity.Create(amount, UnitOfMeasure.Kilogram);

    private async Task SaveAsync(TenantId tenantId, Action<IServiceProvider> change)
    {
        await using var scope = database.NewScope(tenantId);
        change(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IInventoryUnitOfWork>().SaveChangesAsync();
    }

    private async Task EditAsync<T>(TenantId tenantId, Func<IServiceProvider, Task<T>> load, Action<T> change)
    {
        await using var scope = database.NewScope(tenantId);
        change(await load(scope.ServiceProvider));
        await scope.ServiceProvider.GetRequiredService<IInventoryUnitOfWork>().SaveChangesAsync();
    }

    private async Task<T> InScopeAsync<T>(TenantId? tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = database.NewScope(tenantId);
        return await read(scope.ServiceProvider);
    }

    private async Task<Ingredient> GivenIngredientAsync(TenantId tenantId, string name, UnitOfMeasure unit = UnitOfMeasure.Gram)
    {
        var ingredient = Ingredient.Create(tenantId, name, unit, isNameUnique: true);
        await SaveAsync(tenantId, services => services.GetRequiredService<IIngredientRepository>().Add(ingredient));
        return ingredient;
    }

    private async Task<InventoryItem> GivenItemAsync(TenantId tenantId, LocationId locationId, Ingredient ingredient, UnitOfMeasure unit, decimal threshold = 0m)
    {
        var item = InventoryItem.Create(
            tenantId,
            new LocationReference(tenantId, locationId, IsOperational: true),
            ingredient.ToReference(),
            unit,
            Quantity.Create(threshold, unit),
            isUniqueForLocation: true);
        await SaveAsync(tenantId, services => services.GetRequiredService<IInventoryItemRepository>().Add(item));
        return item;
    }

    private Task<InventoryItem?> LoadItemAsync(IServiceProvider services, TenantId tenantId, InventoryItem item) =>
        services.GetRequiredService<IInventoryItemRepository>().FindAsync(tenantId, item.Id);

    [Fact]
    public async Task Movements_are_appended_to_the_ledger_and_the_stock_comes_back_without_its_history()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var flour = await GivenIngredientAsync(tenant, "Farine");
        var item = await GivenItemAsync(tenant, location, flour, UnitOfMeasure.Kilogram, threshold: 2m);

        await EditAsync(tenant, services => LoadItemAsync(services, tenant, item), loaded => loaded!.Receive(Kg(10m), Clock, "BL-1"));
        Clock.Advance(TimeSpan.FromMinutes(1));
        await EditAsync(tenant, services => LoadItemAsync(services, tenant, item), loaded => loaded!.Consume(G(500m), Clock));
        Clock.Advance(TimeSpan.FromMinutes(1));
        await EditAsync(tenant, services => LoadItemAsync(services, tenant, item), loaded => loaded!.RecordWaste(Kg(1m), "Sac percé", Clock));
        Clock.Advance(TimeSpan.FromMinutes(1));
        await EditAsync(tenant, services => LoadItemAsync(services, tenant, item), loaded => loaded!.Adjust(Kg(8.5m), "Inventaire", Clock));

        var reloaded = (await InScopeAsync(tenant, services => LoadItemAsync(services, tenant, item)))!;
        var ledger = await InScopeAsync(tenant, services => services.GetRequiredService<IStockMovementLedger>().ListAsync(tenant, item.Id, 0, 10));

        Assert.Equal(8.5m, reloaded.CurrentQuantity);
        Assert.Equal(Kg(2m), reloaded.ReorderThreshold);
        Assert.Empty(reloaded.Movements);
        // Les plus récents d'abord ; l'inventaire a trouvé exactement le stock attendu (10 - 0,5 - 1) : une trace de delta nul.
        Assert.Equal(
            new[] { StockMovementType.CountConfirmed, StockMovementType.Waste, StockMovementType.Consumption, StockMovementType.Receipt },
            ledger.Select(movement => movement.Type).ToArray());
        Assert.Equal(new[] { 0m, -1m, -0.5m, 10m }, ledger.Select(movement => movement.QuantityDelta).ToArray());
        Assert.Equal("BL-1", ledger[^1].Reference);
        Assert.Equal("Sac percé", ledger[1].Reason);
    }

    [Fact]
    public async Task Two_simultaneous_movements_on_one_item_cannot_both_start_from_the_same_stock()
    {
        var tenant = TenantId.New();
        var flour = await GivenIngredientAsync(tenant, "Farine");
        var item = await GivenItemAsync(tenant, LocationId.New(), flour, UnitOfMeasure.Gram);
        await EditAsync(tenant, services => LoadItemAsync(services, tenant, item), loaded => loaded!.Receive(G(100m), Clock));

        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var mine = await LoadItemAsync(first.ServiceProvider, tenant, item);
        var theirs = await LoadItemAsync(second.ServiceProvider, tenant, item);
        mine!.Consume(G(80m), Clock);
        theirs!.Consume(G(80m), Clock);

        await first.ServiceProvider.GetRequiredService<IInventoryUnitOfWork>().SaveChangesAsync();
        // Sans la version, les deux sorties passeraient et le stock tomberait à -60 g malgré la policy Forbid.
        await Assert.ThrowsAsync<ConcurrencyException>(() => second.ServiceProvider.GetRequiredService<IInventoryUnitOfWork>().SaveChangesAsync());
        Assert.Equal(20m, (await InScopeAsync(tenant, services => LoadItemAsync(services, tenant, item)))!.CurrentQuantity);
    }

    [Fact]
    public async Task The_low_stock_list_compares_the_stock_to_the_threshold_in_the_database()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var flour = await GivenIngredientAsync(tenant, "Farine");
        var sugar = await GivenIngredientAsync(tenant, "Sucre");
        var low = await GivenItemAsync(tenant, location, flour, UnitOfMeasure.Gram, threshold: 500m);
        var full = await GivenItemAsync(tenant, location, sugar, UnitOfMeasure.Gram, threshold: 500m);
        await EditAsync(tenant, services => LoadItemAsync(services, tenant, full), loaded => loaded!.Receive(G(800m), Clock));

        var lowStock = await InScopeAsync(tenant, services => services.GetRequiredService<IInventoryItemRepository>().ListAsync(tenant, location, lowStockOnly: true));
        var all = await InScopeAsync(tenant, services => services.GetRequiredService<IInventoryItemRepository>().ListAsync(tenant, null, lowStockOnly: false));

        Assert.Equal(low.Id, Assert.Single(lowStock).Id);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task A_recipe_and_its_ingredient_lines_come_back_whole_and_change_in_place()
    {
        var tenant = TenantId.New();
        var dough = await GivenIngredientAsync(tenant, "Pâte");
        var cheese = await GivenIngredientAsync(tenant, "Mozzarella");
        var product = ProductId.New();
        var recipe = Recipe.Create(tenant, new ProductReference(tenant, product), isUniqueForProduct: true);
        recipe.AddIngredient(dough.ToReference(), G(250m), Clock);
        recipe.AddIngredient(cheese.ToReference(), Kg(0.1m), Clock);
        await SaveAsync(tenant, services => services.GetRequiredService<IRecipeRepository>().Add(recipe));

        await EditAsync(
            tenant,
            services => services.GetRequiredService<IRecipeRepository>().FindAsync(tenant, recipe.Id),
            loaded =>
            {
                loaded!.ChangeQuantity(dough.Id, G(300m), Clock);
                loaded.RemoveIngredient(cheese.Id, Clock);
            });
        var reloaded = (await InScopeAsync(tenant, services => services.GetRequiredService<IRecipeRepository>().FindAsync(tenant, recipe.Id)))!;
        var forProduct = await InScopeAsync(tenant, services => services.GetRequiredService<IRecipeRepository>().ListForProductsAsync(tenant, [product]));

        var line = Assert.Single(reloaded.Ingredients);
        Assert.Equal((dough.Id, G(300m)), (line.IngredientId, line.Quantity));
        Assert.Null(reloaded.VariantId);
        Assert.Equal(recipe.Id, Assert.Single(forProduct).Id);
    }

    [Fact]
    public async Task Changing_only_a_recipe_line_still_protects_the_recipe_against_a_concurrent_change()
    {
        var tenant = TenantId.New();
        var dough = await GivenIngredientAsync(tenant, "Pâte");
        var recipe = Recipe.Create(tenant, new ProductReference(tenant, ProductId.New()), isUniqueForProduct: true);
        recipe.AddIngredient(dough.ToReference(), G(250m), Clock);
        await SaveAsync(tenant, services => services.GetRequiredService<IRecipeRepository>().Add(recipe));

        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var mine = await first.ServiceProvider.GetRequiredService<IRecipeRepository>().FindAsync(tenant, recipe.Id);
        var theirs = await second.ServiceProvider.GetRequiredService<IRecipeRepository>().FindAsync(tenant, recipe.Id);
        mine!.ChangeQuantity(dough.Id, G(260m), Clock);
        theirs!.ChangeQuantity(dough.Id, G(270m), Clock);

        await first.ServiceProvider.GetRequiredService<IInventoryUnitOfWork>().SaveChangesAsync();
        await Assert.ThrowsAsync<ConcurrencyException>(() => second.ServiceProvider.GetRequiredService<IInventoryUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task Unique_indexes_back_the_checks_made_before_writing()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var flour = await GivenIngredientAsync(tenant, "Farine");
        var product = ProductId.New();
        var variant = VariantId.New();
        await SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IRecipeRepository>().Add(Recipe.Create(tenant, new ProductReference(tenant, product), isUniqueForProduct: true));
            services.GetRequiredService<IRecipeRepository>().Add(Recipe.Create(tenant, new ProductReference(tenant, product), isUniqueForProduct: true, variant));
        });
        await GivenItemAsync(tenant, location, flour, UnitOfMeasure.Gram);

        // Les contrôles « unique » ont été contournés (deux requêtes simultanées) : la base refuse quand même.
        var name = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, services =>
            services.GetRequiredService<IIngredientRepository>().Add(Ingredient.Create(tenant, "FARINE", UnitOfMeasure.Gram, isNameUnique: true))));
        var item = await Assert.ThrowsAsync<DomainException>(() => GivenItemAsync(tenant, location, flour, UnitOfMeasure.Kilogram));
        var productRecipe = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, services =>
            services.GetRequiredService<IRecipeRepository>().Add(Recipe.Create(tenant, new ProductReference(tenant, product), isUniqueForProduct: true))));
        var variantRecipe = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, services =>
            services.GetRequiredService<IRecipeRepository>().Add(Recipe.Create(tenant, new ProductReference(tenant, product), isUniqueForProduct: true, variant))));
        var checkers = await InScopeAsync(tenant, async services => (
            await services.GetRequiredService<IIngredientNameUniquenessChecker>().IsUniqueAsync(tenant, " farine ", null),
            await services.GetRequiredService<IRecipeUniquenessChecker>().IsUniqueAsync(tenant, product, null),
            await services.GetRequiredService<IRecipeUniquenessChecker>().IsUniqueAsync(tenant, product, VariantId.New())));

        Assert.Equal(InventoryErrors.IngredientNameNotUnique, name.Error);
        Assert.Equal(InventoryErrors.ItemNotUniqueForLocation, item.Error);
        Assert.Equal(InventoryErrors.RecipeNotUniqueForProduct, productRecipe.Error);
        Assert.Equal(InventoryErrors.RecipeNotUniqueForProduct, variantRecipe.Error);
        Assert.Equal((false, false, true), checkers);
    }

    [Fact]
    public async Task An_order_is_consumed_only_once_even_by_two_simultaneous_messages()
    {
        var tenant = TenantId.New();
        var order = OrderId.New();
        await SaveAsync(tenant, services => services.GetRequiredService<IOrderConsumptionLog>().Record(tenant, order, LocationId.New(), Clock.UtcNow));

        var twice = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, services =>
            services.GetRequiredService<IOrderConsumptionLog>().Record(tenant, order, LocationId.New(), Clock.UtcNow)));
        var consumed = await InScopeAsync(tenant, services => services.GetRequiredService<IOrderConsumptionLog>().HasConsumedAsync(tenant, order));

        Assert.Equal(InventoryApplicationErrors.OrderAlreadyConsumed, twice.Error);
        Assert.True(consumed);
    }

    [Fact]
    public async Task The_stock_of_another_tenant_cannot_be_read_even_with_its_ids()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var flour = await GivenIngredientAsync(tenantA, "Farine");
        var item = await GivenItemAsync(tenantA, LocationId.New(), flour, UnitOfMeasure.Gram);
        await EditAsync(tenantA, services => LoadItemAsync(services, tenantA, item), loaded => loaded!.Receive(G(10m), Clock));

        Assert.Null(await InScopeAsync(tenantB, services => LoadItemAsync(services, tenantA, item)));
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IIngredientRepository>().FindAsync(tenantB, flour.Id)));
        Assert.Empty(await InScopeAsync(tenantB, services => services.GetRequiredService<IStockMovementLedger>().ListAsync(tenantB, item.Id, 0, 10)));
        // Sans restaurant sélectionné, rien n'est visible.
        Assert.Empty(await InScopeAsync(null, services => services.GetRequiredService<InventoryDbContext>().Items.ToListAsync()));
    }
}
