using Inventory.Application.Commands;
using Inventory.Application.Queries;
using Inventory.Domain;

namespace Inventory.Application;

public sealed class InventoryUseCaseTests
{
    private readonly InventoryFixture _fixture = new();

    private static QuantityInput Q(decimal amount, UnitOfMeasure? unit = null) => new(amount, unit);

    private Task<Guid> IngredientAsync(string name, UnitOfMeasure unit = UnitOfMeasure.Gram) =>
        _fixture.CreateIngredient.HandleAsync(new CreateIngredientCommand(name, unit));

    /// <summary>Un article de stock à l'établissement principal (ou donné), éventuellement approvisionné.</summary>
    private async Task<Guid> ItemAsync(Guid ingredient, decimal stock = 0m, UnitOfMeasure? unit = null, Guid? location = null, decimal threshold = 0m)
    {
        var id = await _fixture.CreateItem.HandleAsync(
            new CreateInventoryItemCommand(location ?? _fixture.Location, ingredient, unit, Q(threshold)));
        if (stock > 0m)
        {
            await _fixture.Receive.HandleAsync(new ReceiveStockCommand(id, Q(stock), "BL-1"));
        }

        return id;
    }

    [Fact]
    public async Task An_ingredient_name_is_unique_in_its_restaurant_whatever_the_case()
    {
        await IngredientAsync("Farine");

        var twice = await Assert.ThrowsAsync<DomainException>(() => IngredientAsync(" farine "));
        _fixture.CurrentTenantId = _fixture.TenantB;
        await IngredientAsync("Farine");

        Assert.Equal(InventoryErrors.IngredientNameNotUnique, twice.Error);
        Assert.Equal(2, _fixture.Ingredients.Count);
    }

    [Fact]
    public async Task Stock_is_received_consumed_wasted_and_counted_through_the_ledger()
    {
        var flour = await IngredientAsync("Farine");
        var item = await ItemAsync(flour, unit: UnitOfMeasure.Kilogram);

        await _fixture.Receive.HandleAsync(new ReceiveStockCommand(item, Q(10m), "BL-42"));
        await _fixture.Consume.HandleAsync(new ConsumeStockCommand(item, Q(500m, UnitOfMeasure.Gram)));
        await _fixture.Waste.HandleAsync(new RecordWasteCommand(item, Q(1m), "Sac percé"));
        await _fixture.Count.HandleAsync(new CountStockCommand(item, Q(9m), "Inventaire du soir"));
        var dto = await _fixture.GetItem.HandleAsync(new GetInventoryItemByIdQuery(item));
        var movements = await _fixture.ListMovements.HandleAsync(new ListStockMovementsQuery(item));

        Assert.Equal((9m, UnitOfMeasure.Kilogram, "Farine"), (dto.CurrentQuantity, dto.Unit, dto.IngredientName));
        // Les plus récents d'abord : l'inventaire a trouvé 0,5 kg de plus que prévu (9 - 8,5).
        Assert.Equal(
            new[] { StockMovementType.Adjustment, StockMovementType.Waste, StockMovementType.Consumption, StockMovementType.Receipt },
            movements.Select(movement => movement.Type).ToArray());
        Assert.Equal(new[] { 0.5m, -1m, -0.5m, 10m }, movements.Select(movement => movement.QuantityDelta).ToArray());
        Assert.Equal("BL-42", movements[^1].Reference);
    }

    [Fact]
    public async Task A_manual_exit_below_zero_is_refused_unless_negative_stock_is_allowed()
    {
        var flour = await IngredientAsync("Farine");
        var item = await ItemAsync(flour, stock: 100m);

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Consume.HandleAsync(new ConsumeStockCommand(item, Q(150m))));
        await _fixture.ChangePolicy.HandleAsync(new ChangeNegativeStockPolicyCommand(item, NegativeStockPolicy.Allow));
        await _fixture.Consume.HandleAsync(new ConsumeStockCommand(item, Q(150m)));

        Assert.Equal(InventoryErrors.InsufficientStock, refused.Error);
        Assert.Equal(-50m, _fixture.ItemById(item).CurrentQuantity);
    }

    [Fact]
    public async Task One_stock_item_per_ingredient_and_location_of_the_same_restaurant()
    {
        var flour = await IngredientAsync("Farine");
        await ItemAsync(flour);

        var twice = await Assert.ThrowsAsync<DomainException>(() => ItemAsync(flour));
        var foreignLocation = await Assert.ThrowsAsync<NotFoundException>(() => ItemAsync(flour, location: _fixture.GivenLocation(_fixture.TenantB)));
        var otherLocation = await ItemAsync(flour, location: _fixture.GivenLocation());
        var wrongUnit = await Assert.ThrowsAsync<DomainException>(() => ItemAsync(flour, unit: UnitOfMeasure.Liter, location: _fixture.GivenLocation()));

        Assert.Equal(InventoryErrors.ItemNotUniqueForLocation, twice.Error);
        Assert.Equal(InventoryApplicationErrors.LocationNotFound, foreignLocation.Error);
        Assert.NotEqual(Guid.Empty, otherLocation);
        Assert.Equal(InventoryErrors.UnitIncompatible, wrongUnit.Error);
    }

    [Fact]
    public async Task A_transfer_moves_the_same_quantity_between_two_locations_in_one_save()
    {
        var flour = await IngredientAsync("Farine");
        var source = await ItemAsync(flour, stock: 5000m);
        var destination = await ItemAsync(flour, unit: UnitOfMeasure.Kilogram, location: _fixture.GivenLocation());
        var saves = _fixture.SaveCount;

        await _fixture.Transfer.HandleAsync(new TransferStockCommand(source, destination, Q(2m, UnitOfMeasure.Kilogram), "TR-7"));
        var tooMuch = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Transfer.HandleAsync(new TransferStockCommand(source, destination, Q(4m, UnitOfMeasure.Kilogram), "TR-8")));
        var same = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Transfer.HandleAsync(new TransferStockCommand(source, source, Q(1m), "TR-9")));

        Assert.Equal((3000m, 2m), (_fixture.ItemById(source).CurrentQuantity, _fixture.ItemById(destination).CurrentQuantity));
        Assert.Equal(saves + 1, _fixture.SaveCount);
        Assert.Equal(InventoryErrors.InsufficientStock, tooMuch.Error);
        Assert.Equal(InventoryErrors.TransferSameItem, same.Error);
        Assert.All(_fixture.Ledger.Where(movement => movement.Type == StockMovementType.Transfer), movement => Assert.Equal("TR-7", movement.Reference));
    }

    [Fact]
    public async Task The_low_stock_list_shows_items_at_or_below_their_threshold()
    {
        var flour = await IngredientAsync("Farine");
        var sugar = await IngredientAsync("Sucre");
        var low = await ItemAsync(flour, stock: 100m, threshold: 500m);
        await ItemAsync(sugar, stock: 1000m, threshold: 500m);

        var lowStock = await _fixture.ListItems.HandleAsync(new ListInventoryItemsQuery(_fixture.Location, LowStockOnly: true));
        var all = await _fixture.ListItems.HandleAsync(new ListInventoryItemsQuery());

        Assert.Equal(low, Assert.Single(lowStock).Id);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task A_recipe_is_written_for_a_catalog_product_or_one_of_its_variants_once()
    {
        var (pizza, variants) = _fixture.GivenProduct(variants: 2);
        var (otherProduct, otherVariants) = _fixture.GivenProduct(variants: 1);
        var dough = await IngredientAsync("Pâte");

        var recipe = await _fixture.CreateRecipe.HandleAsync(new CreateRecipeCommand(pizza));
        var large = await _fixture.CreateRecipe.HandleAsync(new CreateRecipeCommand(pizza, variants[1]));
        var twice = await Assert.ThrowsAsync<DomainException>(() => _fixture.CreateRecipe.HandleAsync(new CreateRecipeCommand(pizza)));
        var foreignVariant = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.CreateRecipe.HandleAsync(new CreateRecipeCommand(pizza, otherVariants[0])));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.CreateRecipe.HandleAsync(new CreateRecipeCommand(Guid.NewGuid())));
        await _fixture.AddToRecipe.HandleAsync(new AddRecipeIngredientCommand(recipe, dough, Q(250m)));
        await _fixture.ChangeRecipeQuantity.HandleAsync(new ChangeRecipeQuantityCommand(recipe, dough, Q(0.3m, UnitOfMeasure.Kilogram)));
        var dto = await _fixture.GetRecipe.HandleAsync(new GetRecipeByIdQuery(recipe));

        Assert.NotEqual(recipe, large);
        Assert.Equal(InventoryErrors.RecipeNotUniqueForProduct, twice.Error);
        Assert.Equal(InventoryApplicationErrors.VariantNotFound, foreignVariant.Error);
        Assert.Equal(InventoryApplicationErrors.ProductNotFound, unknown.Error);
        var line = Assert.Single(dto.Ingredients);
        Assert.Equal(("Pâte", 0.3m, UnitOfMeasure.Kilogram), (line.IngredientName, line.Quantity, line.Unit));
        Assert.NotEqual(Guid.Empty, otherProduct);
    }

    [Fact]
    public async Task An_inactive_ingredient_joins_no_new_recipe_nor_stock_item()
    {
        var (pizza, _) = _fixture.GivenProduct();
        var basil = await IngredientAsync("Basilic");
        var recipe = await _fixture.CreateRecipe.HandleAsync(new CreateRecipeCommand(pizza));
        await _fixture.DeactivateIngredient.HandleAsync(new DeactivateIngredientCommand(basil));

        var inRecipe = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.AddToRecipe.HandleAsync(new AddRecipeIngredientCommand(recipe, basil, Q(5m))));
        var asItem = await Assert.ThrowsAsync<DomainException>(() => ItemAsync(basil));
        var listed = await _fixture.ListIngredients.HandleAsync(new ListIngredientsQuery());

        Assert.Equal(InventoryErrors.IngredientInactive, inRecipe.Error);
        Assert.Equal(InventoryErrors.IngredientInactive, asItem.Error);
        Assert.Empty(listed);
    }

    /// <summary>Margherita (250 g de pâte, 0,1 kg de mozzarella) ; la grande a sa propre recette (400 g de pâte).</summary>
    private async Task<(Guid Pizza, Guid Large, Guid Dough, Guid Mozzarella)> GivenPizzaAsync()
    {
        var (pizza, variants) = _fixture.GivenProduct(variants: 1);
        var dough = await IngredientAsync("Pâte");
        var mozzarella = await IngredientAsync("Mozzarella");
        var recipe = await _fixture.CreateRecipe.HandleAsync(new CreateRecipeCommand(pizza));
        await _fixture.AddToRecipe.HandleAsync(new AddRecipeIngredientCommand(recipe, dough, Q(250m)));
        await _fixture.AddToRecipe.HandleAsync(new AddRecipeIngredientCommand(recipe, mozzarella, Q(0.1m, UnitOfMeasure.Kilogram)));
        var large = await _fixture.CreateRecipe.HandleAsync(new CreateRecipeCommand(pizza, variants[0]));
        await _fixture.AddToRecipe.HandleAsync(new AddRecipeIngredientCommand(large, dough, Q(400m)));
        return (pizza, variants[0], dough, mozzarella);
    }

    [Fact]
    public async Task An_order_in_preparation_consumes_its_recipes_once_per_ingredient_variant_first()
    {
        var (pizza, large, dough, mozzarella) = await GivenPizzaAsync();
        var doughItem = await ItemAsync(dough, stock: 5000m);
        var mozzarellaItem = await ItemAsync(mozzarella, stock: 2m, unit: UnitOfMeasure.Kilogram);
        var order = _fixture.GivenOrder([(pizza, null, 2), (pizza, large, 1)]);

        var result = await _fixture.ConsumeOrder.HandleAsync(new ConsumeStockForOrderCommand(order));

        // Pâte : 2 × 250 g + 1 × 400 g (recette de la variante) = 900 g, en une seule sortie ; mozzarella : 2 × 0,1 kg.
        Assert.Equal(2, result.MovementCount);
        Assert.False(result.AlreadyConsumed);
        Assert.Equal(4100m, _fixture.ItemById(doughItem).CurrentQuantity);
        Assert.Equal(1.8m, _fixture.ItemById(mozzarellaItem).CurrentQuantity);
        var sale = Assert.Single(_fixture.Ledger, movement => movement.InventoryItemId.Value == doughItem && movement.Type == StockMovementType.Consumption);
        Assert.Equal((-900m, order.ToString()), (sale.QuantityDelta, sale.Reference));
    }

    [Fact]
    public async Task Consuming_an_order_twice_changes_nothing_the_second_time()
    {
        var (pizza, _, dough, _) = await GivenPizzaAsync();
        var doughItem = await ItemAsync(dough, stock: 1000m);
        var order = _fixture.GivenOrder([(pizza, null, 1)]);

        await _fixture.ConsumeOrder.HandleAsync(new ConsumeStockForOrderCommand(order));
        var again = await _fixture.ConsumeOrder.HandleAsync(new ConsumeStockForOrderCommand(order));

        Assert.True(again.AlreadyConsumed);
        Assert.Equal(750m, _fixture.ItemById(doughItem).CurrentQuantity);
    }

    [Fact]
    public async Task A_sale_is_recorded_even_below_zero_and_missing_recipes_or_items_are_reported()
    {
        var (pizza, _, dough, mozzarella) = await GivenPizzaAsync();
        var (water, _) = _fixture.GivenProduct();
        var doughItem = await ItemAsync(dough, stock: 100m);
        var order = _fixture.GivenOrder([(pizza, null, 1), (water, null, 3)]);
        // Le restaurant suspendu ne bloque pas un fait déjà arrivé (la vente).
        _fixture.Writable[_fixture.TenantA] = false;

        var result = await _fixture.ConsumeOrder.HandleAsync(new ConsumeStockForOrderCommand(order));

        Assert.Equal(-150m, _fixture.ItemById(doughItem).CurrentQuantity);
        Assert.Contains(_fixture.ItemById(doughItem).DomainEvents, domainEvent => domainEvent is StockWentNegative);
        Assert.Equal(new[] { water }, result.ProductsWithoutRecipe.ToArray());
        Assert.Equal(new[] { mozzarella }, result.IngredientsWithoutStockItem.ToArray());
    }

    [Fact]
    public async Task An_order_not_yet_in_preparation_or_of_another_restaurant_consumes_nothing()
    {
        var (pizza, _, dough, _) = await GivenPizzaAsync();
        var doughItem = await ItemAsync(dough, stock: 1000m);
        var waiting = _fixture.GivenOrder([(pizza, null, 1)], started: false);
        var foreign = _fixture.GivenOrder([(pizza, null, 1)], tenantId: _fixture.TenantB);

        var notStarted = await Assert.ThrowsAsync<DomainException>(() => _fixture.ConsumeOrder.HandleAsync(new ConsumeStockForOrderCommand(waiting)));
        var notFound = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ConsumeOrder.HandleAsync(new ConsumeStockForOrderCommand(foreign)));

        Assert.Equal(InventoryApplicationErrors.OrderNotInPreparation, notStarted.Error);
        Assert.Equal(InventoryApplicationErrors.OrderNotFound, notFound.Error);
        Assert.Equal(1000m, _fixture.ItemById(doughItem).CurrentQuantity);
        Assert.Empty(_fixture.Consumed);
    }

    [Fact]
    public async Task Another_restaurant_sees_and_changes_nothing()
    {
        var flour = await IngredientAsync("Farine");
        var item = await ItemAsync(flour, stock: 10m);
        _fixture.CurrentTenantId = _fixture.TenantB;

        var receive = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Receive.HandleAsync(new ReceiveStockCommand(item, Q(1m))));
        var history = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ListMovements.HandleAsync(new ListStockMovementsQuery(item)));
        var asItem = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.CreateItem.HandleAsync(new CreateInventoryItemCommand(_fixture.GivenLocation(_fixture.TenantB), flour)));

        Assert.Equal(InventoryApplicationErrors.ItemNotFound, receive.Error);
        Assert.Equal(InventoryApplicationErrors.ItemNotFound, history.Error);
        Assert.Equal(InventoryApplicationErrors.IngredientNotFound, asItem.Error);
        Assert.Empty(await _fixture.ListItems.HandleAsync(new ListInventoryItemsQuery()));
        Assert.Empty(await _fixture.ListIngredients.HandleAsync(new ListIngredientsQuery(IncludeInactive: true)));
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_stock_but_changes_nothing()
    {
        var flour = await IngredientAsync("Farine");
        var item = await ItemAsync(flour, stock: 10m);
        _fixture.Writable[_fixture.TenantA] = false;

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Receive.HandleAsync(new ReceiveStockCommand(item, Q(1m))));
        var missing = await Assert.ThrowsAsync<DomainException>(() => _fixture.Waste.HandleAsync(new RecordWasteCommand(item, null, "x")));

        Assert.Equal(InventoryApplicationErrors.TenantNotWritable, refused.Error);
        Assert.Equal(InventoryApplicationErrors.TenantNotWritable, missing.Error);
        Assert.Equal(10m, (await _fixture.GetItem.HandleAsync(new GetInventoryItemByIdQuery(item))).CurrentQuantity);
    }

    [Fact]
    public async Task A_missing_quantity_is_a_clear_error()
    {
        var flour = await IngredientAsync("Farine");
        var item = await ItemAsync(flour);

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Receive.HandleAsync(new ReceiveStockCommand(item, null)));

        Assert.Equal(InventoryApplicationErrors.QuantityRequired, refused.Error);
    }

    [Fact]
    public async Task A_purchase_order_received_in_two_deliveries_feeds_the_stock_and_its_cost()
    {
        var flour = await IngredientAsync("Farine", UnitOfMeasure.Kilogram);
        var item = await ItemAsync(flour, threshold: 5m);
        var supplier = await _fixture.SaveSupplier.HandleAsync(new SaveSupplierCommand(null, "Grossiste", "+229 00 00 00 00", null, null));
        var order = await _fixture.CreatePurchaseOrder.HandleAsync(
            new CreatePurchaseOrderCommand(_fixture.Location, supplier, [new PurchaseLineInput(item, Q(10m), 2m)], null));
        await _fixture.SendPurchaseOrder.HandleAsync(new SendPurchaseOrderCommand(order));

        await _fixture.ReceivePurchaseOrder.HandleAsync(new ReceivePurchaseOrderCommand(order, [new ReceivedLineInput(item, 4m)]));
        await _fixture.ReceivePurchaseOrder.HandleAsync(new ReceivePurchaseOrderCommand(order, [new ReceivedLineInput(item, 5m, 3m)], Close: true));
        var dto = await _fixture.GetPurchaseOrder.HandleAsync(new GetPurchaseOrderQuery(order));
        var stock = _fixture.ItemById(item);

        Assert.Equal(PurchaseOrderStatus.Received, dto.Status);
        Assert.Equal(("Grossiste", 9m), (dto.SupplierName, dto.Lines.Single().Received));
        Assert.Equal(9m, stock.CurrentQuantity);
        Assert.Equal(decimal.Round(23m / 9m, InventoryItem.CostDecimals), stock.AverageUnitCost);
        Assert.All(stock.Movements, movement => Assert.Equal(dto.Number, movement.Reference));
    }

    [Fact]
    public async Task A_purchase_order_is_for_a_supplier_of_the_same_restaurant()
    {
        var flour = await IngredientAsync("Farine", UnitOfMeasure.Kilogram);
        var item = await ItemAsync(flour);
        var supplier = await _fixture.SaveSupplier.HandleAsync(new SaveSupplierCommand(null, "Grossiste", null, null, null));
        _fixture.CurrentTenantId = _fixture.TenantB;

        var refused = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.CreatePurchaseOrder.HandleAsync(
            new CreatePurchaseOrderCommand(_fixture.GivenLocation(_fixture.TenantB), supplier, [new PurchaseLineInput(item, Q(1m))], null)));

        Assert.Equal(InventoryApplicationErrors.SupplierNotFound, refused.Error);
    }

    [Fact]
    public async Task Items_at_their_threshold_are_suggested_up_to_twice_the_threshold()
    {
        var flour = await IngredientAsync("Farine", UnitOfMeasure.Kilogram);
        var sugar = await IngredientAsync("Sucre", UnitOfMeasure.Kilogram);
        var low = await ItemAsync(flour, stock: 3m, threshold: 5m);
        await ItemAsync(sugar, stock: 20m, threshold: 5m);

        var suggestions = await _fixture.ReorderSuggestions.HandleAsync(new GetReorderSuggestionsQuery(_fixture.Location));

        var suggestion = Assert.Single(suggestions);
        Assert.Equal((low, "Farine", 7m), (suggestion.ItemId, suggestion.IngredientName, suggestion.SuggestedQuantity));
    }

    [Fact]
    public async Task A_recipe_cost_adds_the_average_cost_of_its_ingredients()
    {
        var (_, _, dough, mozzarella) = await GivenPizzaAsync();
        var doughItem = await ItemAsync(dough, unit: UnitOfMeasure.Kilogram);
        await _fixture.Receive.HandleAsync(new ReceiveStockCommand(doughItem, Q(10m), "BL-2", TotalCost: 20m));
        var mozzarellaItem = await ItemAsync(mozzarella, unit: UnitOfMeasure.Kilogram);

        var before = await _fixture.RecipeCosts.HandleAsync(new GetRecipeCostsQuery(_fixture.Location));
        await _fixture.SetUnitCost.HandleAsync(new SetUnitCostCommand(mozzarellaItem, 8m));
        var after = await _fixture.RecipeCosts.HandleAsync(new GetRecipeCostsQuery(_fixture.Location));

        var regular = before.Single(recipe => recipe.VariantId is null);
        Assert.Equal((0.5m, false), (regular.Cost, regular.IsComplete));
        var priced = after.Single(recipe => recipe.VariantId is null);
        Assert.Equal((1.3m, true), (priced.Cost, priced.IsComplete));
        Assert.Equal(0.8m, after.Single(recipe => recipe.VariantId is not null).Cost);
    }
}
