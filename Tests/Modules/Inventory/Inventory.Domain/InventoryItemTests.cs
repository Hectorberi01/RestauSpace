namespace Inventory.Domain;

public class InventoryItemTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly LocationId Location = LocationId.New();
    private static readonly LocationReference Place = new(Tenant, Location, IsOperational: true);

    private readonly Dictionary<IngredientId, Ingredient> _ingredients = [];

    private readonly FakeClock _clock = new();

    private static Quantity Kg(decimal amount) => Quantity.Create(amount, UnitOfMeasure.Kilogram);

    private static Quantity G(decimal amount) => Quantity.Create(amount, UnitOfMeasure.Gram);

    private static Quantity L(decimal amount) => Quantity.Create(amount, UnitOfMeasure.Liter);

    // Un IngredientReference ne peut pas être construit à la main : chaque test passe par un vrai ingrédient.
    private IngredientReference Flour(TenantId? tenantId = null, bool isActive = true)
    {
        var ingredient = Ingredient.Create(tenantId ?? Tenant, "Flour", UnitOfMeasure.Gram, isNameUnique: true);
        _ingredients.Add(ingredient.Id, ingredient);
        if (!isActive)
        {
            ingredient.Deactivate();
        }

        return ingredient.ToReference();
    }

    /// <summary>Article en kilogrammes, seuil de 2 kg, éventuellement approvisionné (événements effacés).</summary>
    private InventoryItem NewItem(
        decimal stockKg = 0m,
        decimal thresholdKg = 2m,
        NegativeStockPolicy policy = NegativeStockPolicy.Forbid,
        IngredientReference? ingredient = null)
    {
        var item = InventoryItem.Create(
            Tenant, Place, ingredient ?? Flour(), UnitOfMeasure.Kilogram, Kg(thresholdKg), isUniqueForLocation: true, policy);
        if (stockKg > 0m)
        {
            item.Receive(Kg(stockKg), _clock, "initial");
        }

        item.ClearDomainEvents();
        return item;
    }

    /// <summary>Article du même ingrédient dans un autre établissement : l'autre côté d'un transfert.</summary>
    private InventoryItem Counterpart(
        InventoryItem item,
        decimal stockKg = 0m,
        UnitOfMeasure unit = UnitOfMeasure.Kilogram,
        NegativeStockPolicy policy = NegativeStockPolicy.Forbid,
        TenantId? tenant = null)
    {
        // Même ingrédient que l'article ; un article d'un autre tenant ne peut porter qu'un ingrédient de ce tenant.
        var owner = tenant ?? Tenant;
        var counterpart = InventoryItem.Create(
            owner,
            new LocationReference(owner, LocationId.New(), IsOperational: true),
            owner == Tenant ? _ingredients[item.IngredientId].ToReference() : Flour(owner),
            unit,
            Quantity.Zero(unit),
            isUniqueForLocation: true,
            policy);
        if (stockKg > 0m)
        {
            counterpart.Receive(Kg(stockKg), _clock, "initial");
        }

        counterpart.ClearDomainEvents();
        return counterpart;
    }

    private static void AssertProjectionMatchesLedger(InventoryItem item) =>
        Assert.Equal(item.Movements.Sum(movement => movement.QuantityDelta), item.CurrentQuantity);

    /// <summary>Ce que donne un rechargement : la quantité stockée, sans mouvement (historique non réhydraté).</summary>
    private static void Reload(InventoryItem item) =>
        ((List<StockMovement>)typeof(InventoryItem)
            .GetField("_movements", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(item)!).Clear();

    // ---- Création (Create)

    [Fact]
    public void Create_starts_with_an_empty_ledger_and_zero_stock()
    {
        var ingredient = Flour();

        var item = InventoryItem.Create(
            Tenant, Place, ingredient, UnitOfMeasure.Kilogram, Kg(2m), isUniqueForLocation: true);

        Assert.Equal(Tenant, item.TenantId);
        Assert.Equal(Location, item.LocationId);
        Assert.Equal(ingredient.IngredientId, item.IngredientId);
        Assert.Equal(UnitOfMeasure.Kilogram, item.Unit);
        Assert.Equal(0m, item.CurrentQuantity);
        Assert.Equal(Kg(2m), item.ReorderThreshold);
        Assert.Equal(NegativeStockPolicy.Forbid, item.NegativeStockPolicy);
        Assert.Empty(item.Movements);
        DomainAssert.NoEvents(item);
    }

    [Fact]
    public void Create_accepts_a_unit_compatible_with_the_ingredient_base_unit()
    {
        var item = InventoryItem.Create(
            Tenant, Place, Flour(), UnitOfMeasure.Kilogram, Kg(1m), isUniqueForLocation: true);

        Assert.Equal(UnitOfMeasure.Kilogram, item.Unit);
    }

    [Theory]
    [InlineData(UnitOfMeasure.Liter)]
    [InlineData(UnitOfMeasure.Milliliter)]
    [InlineData(UnitOfMeasure.Piece)]
    public void Create_rejects_a_unit_incompatible_with_the_ingredient_base_unit(UnitOfMeasure unit) =>
        DomainAssert.Throws(
            InventoryErrors.UnitIncompatible,
            () => InventoryItem.Create(Tenant, Place, Flour(), unit, Kg(1m), isUniqueForLocation: true));

    [Fact]
    public void Create_rejects_an_unknown_unit() =>
        DomainAssert.Throws(
            InventoryErrors.UnitUnknown,
            () => InventoryItem.Create(Tenant, Place, Flour(), (UnitOfMeasure)0, Kg(1m), isUniqueForLocation: true));

    [Fact]
    public void Create_converts_the_threshold_to_the_item_unit()
    {
        var item = InventoryItem.Create(
            Tenant, Place, Flour(), UnitOfMeasure.Kilogram, G(1500m), isUniqueForLocation: true);

        Assert.Equal(Kg(1.5m), item.ReorderThreshold);
    }

    [Fact]
    public void Create_rejects_a_threshold_in_an_incompatible_unit() =>
        DomainAssert.Throws(
            InventoryErrors.UnitIncompatible,
            () => InventoryItem.Create(Tenant, Place, Flour(), UnitOfMeasure.Kilogram, L(1m), isUniqueForLocation: true));

    [Fact]
    public void Create_rejects_an_ingredient_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => InventoryItem.Create(
                Tenant, Place, Flour(TenantId.New()), UnitOfMeasure.Kilogram, Kg(1m), isUniqueForLocation: true));

    [Fact]
    public void Create_rejects_an_inactive_ingredient() =>
        DomainAssert.Throws(
            InventoryErrors.IngredientInactive,
            () => InventoryItem.Create(
                Tenant, Place, Flour(isActive: false), UnitOfMeasure.Kilogram, Kg(1m), isUniqueForLocation: true));

    [Fact]
    public void Create_rejects_an_empty_tenant_and_an_empty_location()
    {
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => InventoryItem.Create(default, Place, Flour(), UnitOfMeasure.Kilogram, Kg(1m), isUniqueForLocation: true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(Tenant, default, true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(default, Location, true));
    }

    [Fact]
    public void Create_rejects_a_location_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => InventoryItem.Create(
                Tenant,
                new LocationReference(TenantId.New(), Location, true),
                Flour(),
                UnitOfMeasure.Kilogram,
                Kg(1m),
                isUniqueForLocation: true));

    [Fact]
    public void Create_accepts_a_location_that_is_not_operational()
    {
        // Le stock peut être configuré pour un établissement pas encore ouvert ou fermé pour travaux.
        var item = InventoryItem.Create(
            Tenant, new LocationReference(Tenant, Location, IsOperational: false), Flour(), UnitOfMeasure.Kilogram, Kg(1m), isUniqueForLocation: true);

        Assert.Equal(Location, item.LocationId);
    }

    [Fact]
    public void Create_rejects_a_second_item_for_the_same_location_and_ingredient() =>
        DomainAssert.Throws(
            InventoryErrors.ItemNotUniqueForLocation,
            () => InventoryItem.Create(Tenant, Place, Flour(), UnitOfMeasure.Kilogram, Kg(1m), isUniqueForLocation: false));

    [Fact]
    public void Create_rejects_an_unknown_negative_stock_policy() =>
        DomainAssert.Throws(
            InventoryErrors.ItemPolicyUnknown,
            () => InventoryItem.Create(
                Tenant, Place, Flour(), UnitOfMeasure.Kilogram, Kg(1m), isUniqueForLocation: true, (NegativeStockPolicy)9));

    // ---- Réception (Receive)

    [Fact]
    public void Receive_increases_the_stock_and_appends_a_receipt_movement()
    {
        var item = NewItem();

        item.Receive(Kg(10m), _clock, "  PO-2026-001 ");

        Assert.Equal(10m, item.CurrentQuantity);
        var movement = Assert.Single(item.Movements);
        Assert.Equal(StockMovementType.Receipt, movement.Type);
        Assert.Equal(10m, movement.QuantityDelta);
        Assert.Equal(UnitOfMeasure.Kilogram, movement.Unit);
        Assert.Equal(_clock.UtcNow, movement.OccurredAtUtc);
        Assert.Equal("PO-2026-001", movement.Reference);
        Assert.Null(movement.Reason);
    }

    [Fact]
    public void Receive_raises_StockReceived_with_the_resulting_stock()
    {
        var item = NewItem(stockKg: 3m);

        item.Receive(Kg(4m), _clock, "PO-2");

        var received = DomainAssert.Raised<StockReceived>(item);
        Assert.Equal(
            new StockReceived(Tenant, item.Id, Location, item.IngredientId, 4m, 7m, UnitOfMeasure.Kilogram, "PO-2", _clock.UtcNow),
            received);
    }

    [Fact]
    public void Receive_converts_a_compatible_unit_to_the_item_unit()
    {
        var item = NewItem();

        item.Receive(G(500m), _clock);

        Assert.Equal(0.5m, item.CurrentQuantity);
        Assert.Equal(0.5m, Assert.Single(item.Movements).QuantityDelta);
        Assert.Equal(UnitOfMeasure.Kilogram, Assert.Single(item.Movements).Unit);
    }

    [Fact]
    public void Receive_rejects_an_incompatible_unit()
    {
        var item = NewItem();

        DomainAssert.Throws(InventoryErrors.UnitIncompatible, () => item.Receive(L(1m), _clock));
        Assert.Empty(item.Movements);
        DomainAssert.NoEvents(item);
    }

    [Fact]
    public void Receive_rejects_a_zero_quantity()
    {
        var item = NewItem();

        DomainAssert.Throws(InventoryErrors.MovementQuantityNotPositive, () => item.Receive(Kg(0m), _clock));
        Assert.Empty(item.Movements);
    }

    [Fact]
    public void Receive_stores_a_blank_reference_as_null_and_rejects_a_too_long_one()
    {
        var item = NewItem();

        item.Receive(Kg(1m), _clock, "   ");
        Assert.Null(Assert.Single(item.Movements).Reference);

        DomainAssert.Throws(
            InventoryErrors.MovementReferenceTooLong,
            () => item.Receive(Kg(1m), _clock, new string('r', StockMovement.ReferenceMaxLength + 1)));
        Assert.Single(item.Movements);
        Assert.Equal(1m, item.CurrentQuantity);
    }

    [Fact]
    public void Receive_does_not_raise_LowStockReached()
    {
        var item = NewItem(stockKg: 1m);

        item.Receive(Kg(0.5m), _clock);

        DomainAssert.NotRaised<LowStockReached>(item);
    }

    // ---- Consommation (Consume)

    [Fact]
    public void Consume_decreases_the_stock_and_appends_a_negative_consumption_movement()
    {
        var item = NewItem(stockKg: 10m);
        _clock.Advance(TimeSpan.FromHours(1));

        item.Consume(Kg(2.5m), _clock, "order-42");

        Assert.Equal(7.5m, item.CurrentQuantity);
        var movement = item.Movements.Last();
        Assert.Equal(StockMovementType.Consumption, movement.Type);
        Assert.Equal(-2.5m, movement.QuantityDelta);
        Assert.Equal(_clock.UtcNow, movement.OccurredAtUtc);
        Assert.Equal("order-42", movement.Reference);
    }

    [Fact]
    public void Consume_raises_StockConsumed_with_the_resulting_stock()
    {
        var item = NewItem(stockKg: 10m);

        item.Consume(Kg(2.5m), _clock, "order-42");

        var consumed = DomainAssert.Raised<StockConsumed>(item);
        Assert.Equal(
            new StockConsumed(Tenant, item.Id, Location, item.IngredientId, 2.5m, 7.5m, UnitOfMeasure.Kilogram, "order-42", _clock.UtcNow),
            consumed);
    }

    [Fact]
    public void Consume_converts_a_compatible_unit_to_the_item_unit()
    {
        var item = NewItem(stockKg: 10m);

        item.Consume(G(250m), _clock);

        Assert.Equal(9.75m, item.CurrentQuantity);
        Assert.Equal(-0.25m, item.Movements.Last().QuantityDelta);
    }

    [Fact]
    public void Consume_rejects_an_incompatible_unit()
    {
        var item = NewItem(stockKg: 10m);

        DomainAssert.Throws(
            InventoryErrors.UnitIncompatible,
            () => item.Consume(Quantity.Create(1m, UnitOfMeasure.Piece), _clock));
        Assert.Equal(10m, item.CurrentQuantity);
    }

    [Fact]
    public void Consume_rejects_a_zero_quantity()
    {
        var item = NewItem(stockKg: 10m);

        DomainAssert.Throws(InventoryErrors.MovementQuantityNotPositive, () => item.Consume(G(0m), _clock));
    }

    [Fact]
    public void Consume_beyond_the_stock_is_rejected_when_negative_stock_is_forbidden()
    {
        var item = NewItem(stockKg: 3m);

        DomainAssert.Throws(InventoryErrors.InsufficientStock, () => item.Consume(Kg(3.001m), _clock));

        Assert.Equal(3m, item.CurrentQuantity);
        Assert.Single(item.Movements);
        DomainAssert.NoEvents(item);
    }

    [Fact]
    public void Consume_of_exactly_the_stock_is_accepted_when_negative_stock_is_forbidden()
    {
        var item = NewItem(stockKg: 3m);

        item.Consume(Kg(3m), _clock);

        Assert.Equal(0m, item.CurrentQuantity);
    }

    [Fact]
    public void Consume_beyond_the_stock_goes_negative_when_negative_stock_is_allowed()
    {
        var item = NewItem(stockKg: 3m, policy: NegativeStockPolicy.Allow);

        item.Consume(Kg(5m), _clock);

        Assert.Equal(-2m, item.CurrentQuantity);
        DomainAssert.Raised<StockConsumed>(item);
        AssertProjectionMatchesLedger(item);
    }

    [Fact]
    public void Consume_of_the_last_units_twice_rejects_the_second_attempt()
    {
        var item = NewItem(stockKg: 1m);

        item.Consume(Kg(1m), _clock, "order-1");

        DomainAssert.Throws(InventoryErrors.InsufficientStock, () => item.Consume(Kg(1m), _clock, "order-2"));
        Assert.Equal(0m, item.CurrentQuantity);
        Assert.Equal(2, item.Movements.Count);
    }

    // ---- Ajustement (Adjust)

    [Fact]
    public void Adjust_sets_the_stock_to_the_counted_quantity_with_a_negative_delta()
    {
        var item = NewItem(stockKg: 10m);

        item.Adjust(Kg(8.4m), " monthly count ", _clock, "count-2026-01");

        Assert.Equal(8.4m, item.CurrentQuantity);
        var movement = item.Movements.Last();
        Assert.Equal(StockMovementType.Adjustment, movement.Type);
        Assert.Equal(-1.6m, movement.QuantityDelta);
        Assert.Equal("monthly count", movement.Reason);
        Assert.Equal("count-2026-01", movement.Reference);
        AssertProjectionMatchesLedger(item);
    }

    [Fact]
    public void Adjust_sets_the_stock_to_the_counted_quantity_with_a_positive_delta()
    {
        var item = NewItem(stockKg: 10m);

        item.Adjust(G(10500m), "found a bag", _clock);

        Assert.Equal(10.5m, item.CurrentQuantity);
        Assert.Equal(0.5m, item.Movements.Last().QuantityDelta);
        AssertProjectionMatchesLedger(item);
    }

    [Fact]
    public void Adjust_raises_StockAdjusted_with_the_signed_delta()
    {
        var item = NewItem(stockKg: 10m);

        item.Adjust(Kg(8m), "count", _clock);

        var adjusted = DomainAssert.Raised<StockAdjusted>(item);
        Assert.Equal(
            new StockAdjusted(Tenant, item.Id, Location, item.IngredientId, -2m, 8m, UnitOfMeasure.Kilogram, "count", _clock.UtcNow),
            adjusted);
    }

    [Fact]
    public void Adjust_can_count_the_stock_down_to_zero()
    {
        var item = NewItem(stockKg: 4m);

        item.Adjust(Kg(0m), "all spoiled", _clock);

        Assert.Equal(0m, item.CurrentQuantity);
    }

    [Fact]
    public void Adjust_brings_a_negative_stock_back_to_the_counted_quantity()
    {
        var item = NewItem(stockKg: 1m, policy: NegativeStockPolicy.Allow);
        item.Consume(Kg(3m), _clock);

        item.Adjust(Kg(0.5m), "count", _clock);

        Assert.Equal(0.5m, item.CurrentQuantity);
        Assert.Equal(2.5m, item.Movements.Last().QuantityDelta);
        AssertProjectionMatchesLedger(item);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Adjust_requires_a_reason(string reason)
    {
        var item = NewItem(stockKg: 10m);

        DomainAssert.Throws(InventoryErrors.MovementReasonRequired, () => item.Adjust(Kg(8m), reason, _clock));
        Assert.Equal(10m, item.CurrentQuantity);
        Assert.Single(item.Movements);
    }

    [Fact]
    public void Adjust_rejects_a_too_long_reason()
    {
        var item = NewItem(stockKg: 10m);

        DomainAssert.Throws(
            InventoryErrors.MovementReasonTooLong,
            () => item.Adjust(Kg(8m), new string('x', StockMovement.ReasonMaxLength + 1), _clock));
        Assert.Equal(10m, item.CurrentQuantity);
    }

    [Fact]
    public void Adjust_to_a_count_equal_to_the_current_stock_records_the_confirmation()
    {
        var item = NewItem(stockKg: 10m);
        _clock.Advance(TimeSpan.FromHours(3));

        item.Adjust(G(10000m), "  monthly count ", _clock, "INV-2026-01");

        Assert.Equal(10m, item.CurrentQuantity);
        Assert.Equal(2, item.Movements.Count);
        var movement = item.Movements.Last();
        Assert.Equal(
            (StockMovementType.CountConfirmed, 0m, UnitOfMeasure.Kilogram, "monthly count", "INV-2026-01", _clock.UtcNow),
            (movement.Type, movement.QuantityDelta, movement.Unit, movement.Reason, movement.Reference, movement.OccurredAtUtc));
        Assert.Equal(
            new StockCountConfirmed(Tenant, item.Id, Location, item.IngredientId, 10m, UnitOfMeasure.Kilogram, "monthly count", _clock.UtcNow),
            DomainAssert.Raised<StockCountConfirmed>(item));
        DomainAssert.NotRaised<StockAdjusted>(item);
        DomainAssert.NotRaised<LowStockReached>(item);
        AssertProjectionMatchesLedger(item);
    }

    [Fact]
    public void Adjust_confirming_an_empty_or_low_stock_is_recorded_without_any_other_event()
    {
        var empty = NewItem();

        empty.Adjust(Kg(0m), "count", _clock);

        Assert.Equal(0m, empty.CurrentQuantity);
        Assert.Equal(StockMovementType.CountConfirmed, Assert.Single(empty.Movements).Type);
        Assert.IsType<StockCountConfirmed>(Assert.Single(empty.DomainEvents));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Adjust_confirming_the_stock_still_requires_a_reason(string reason)
    {
        var item = NewItem(stockKg: 10m);

        DomainAssert.Throws(InventoryErrors.MovementReasonRequired, () => item.Adjust(Kg(10m), reason, _clock));
        DomainAssert.Throws(
            InventoryErrors.MovementReasonTooLong,
            () => item.Adjust(Kg(10m), new string('x', StockMovement.ReasonMaxLength + 1), _clock));

        Assert.Single(item.Movements);
        DomainAssert.NoEvents(item);
    }

    [Fact]
    public void Only_a_count_confirmation_carries_a_zero_delta()
    {
        var item = NewItem(stockKg: 10m, policy: NegativeStockPolicy.Allow);
        item.Consume(Kg(4m), _clock);
        item.RecordWaste(Kg(1m), "dropped", _clock);
        item.Adjust(Kg(5m), "count", _clock);
        item.Adjust(Kg(7m), "count", _clock);
        item.ConsumeForSale(Kg(1m), "order-1", _clock);

        Assert.All(item.Movements, movement =>
            Assert.Equal(movement.Type == StockMovementType.CountConfirmed, movement.QuantityDelta == 0m));
        Assert.Single(item.Movements, movement => movement.Type == StockMovementType.CountConfirmed);
    }

    [Fact]
    public void Adjust_rejects_an_incompatible_unit()
    {
        var item = NewItem(stockKg: 10m);

        DomainAssert.Throws(InventoryErrors.UnitIncompatible, () => item.Adjust(L(8m), "count", _clock));
    }

    // ---- Perte (RecordWaste)

    [Fact]
    public void RecordWaste_decreases_the_stock_and_raises_WasteRecorded()
    {
        var item = NewItem(stockKg: 10m);

        item.RecordWaste(G(400m), " expired ", _clock, "batch-7");

        Assert.Equal(9.6m, item.CurrentQuantity);
        var movement = item.Movements.Last();
        Assert.Equal(StockMovementType.Waste, movement.Type);
        Assert.Equal(-0.4m, movement.QuantityDelta);
        Assert.Equal("expired", movement.Reason);
        Assert.Equal("batch-7", movement.Reference);
        var waste = DomainAssert.Raised<WasteRecorded>(item);
        Assert.Equal(
            new WasteRecorded(Tenant, item.Id, Location, item.IngredientId, 0.4m, 9.6m, UnitOfMeasure.Kilogram, "expired", _clock.UtcNow),
            waste);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void RecordWaste_requires_a_reason(string reason)
    {
        var item = NewItem(stockKg: 10m);

        DomainAssert.Throws(InventoryErrors.MovementReasonRequired, () => item.RecordWaste(Kg(1m), reason, _clock));
        Assert.Equal(10m, item.CurrentQuantity);
    }

    [Fact]
    public void RecordWaste_rejects_a_zero_quantity_and_an_incompatible_unit()
    {
        var item = NewItem(stockKg: 10m);

        DomainAssert.Throws(InventoryErrors.MovementQuantityNotPositive, () => item.RecordWaste(Kg(0m), "expired", _clock));
        DomainAssert.Throws(InventoryErrors.UnitIncompatible, () => item.RecordWaste(L(1m), "expired", _clock));
    }

    [Fact]
    public void RecordWaste_beyond_the_stock_is_rejected_when_negative_stock_is_forbidden()
    {
        var item = NewItem(stockKg: 1m);

        DomainAssert.Throws(InventoryErrors.InsufficientStock, () => item.RecordWaste(Kg(2m), "expired", _clock));
        Assert.Equal(1m, item.CurrentQuantity);
        DomainAssert.NoEvents(item);
    }

    [Fact]
    public void RecordWaste_beyond_the_stock_is_accepted_when_negative_stock_is_allowed()
    {
        var item = NewItem(stockKg: 1m, policy: NegativeStockPolicy.Allow);

        item.RecordWaste(Kg(2m), "expired", _clock);

        Assert.Equal(-1m, item.CurrentQuantity);
    }

    // ---- Consommation par la vente

    [Fact]
    public void ConsumeForSale_decreases_the_stock_and_raises_StockConsumed()
    {
        var item = NewItem(stockKg: 10m);
        _clock.Advance(TimeSpan.FromHours(1));

        item.ConsumeForSale(G(2500m), " order-42 ", _clock);

        Assert.Equal(7.5m, item.CurrentQuantity);
        var movement = item.Movements.Last();
        Assert.Equal((StockMovementType.Consumption, -2.5m, "order-42", _clock.UtcNow), (movement.Type, movement.QuantityDelta, movement.Reference, movement.OccurredAtUtc));
        Assert.Equal(
            new StockConsumed(Tenant, item.Id, Location, item.IngredientId, 2.5m, 7.5m, UnitOfMeasure.Kilogram, "order-42", _clock.UtcNow),
            DomainAssert.Raised<StockConsumed>(item));
        DomainAssert.NotRaised<StockWentNegative>(item);
        DomainAssert.NotRaised<LowStockReached>(item);
    }

    [Fact]
    public void ConsumeForSale_is_never_refused_even_when_negative_stock_is_forbidden()
    {
        var item = NewItem(stockKg: 3m, thresholdKg: 2m);
        Assert.Equal(NegativeStockPolicy.Forbid, item.NegativeStockPolicy);

        item.ConsumeForSale(Kg(5m), "order-42", _clock);

        Assert.Equal(-2m, item.CurrentQuantity);
        Assert.Equal(NegativeStockPolicy.Forbid, item.NegativeStockPolicy);
        Assert.Equal(-2m, DomainAssert.Raised<StockConsumed>(item).QuantityAfter);
        Assert.Equal(
            new StockWentNegative(Tenant, item.Id, Location, item.IngredientId, -2m, UnitOfMeasure.Kilogram, "order-42", _clock.UtcNow),
            DomainAssert.Raised<StockWentNegative>(item));
        DomainAssert.Raised<LowStockReached>(item);
        AssertProjectionMatchesLedger(item);
    }

    [Fact]
    public void ConsumeForSale_on_an_empty_item_goes_negative()
    {
        var item = NewItem();

        item.ConsumeForSale(Kg(1m), "order-1", _clock);

        Assert.Equal(-1m, item.CurrentQuantity);
        DomainAssert.Raised<StockWentNegative>(item);
        // Un nouvel article est déjà bas : pas de franchissement.
        DomainAssert.NotRaised<LowStockReached>(item);
    }

    [Fact]
    public void StockWentNegative_is_raised_on_the_crossing_only()
    {
        var item = NewItem(stockKg: 1m);
        item.ConsumeForSale(Kg(2m), "order-1", _clock);
        item.ClearDomainEvents();

        item.ConsumeForSale(Kg(1m), "order-2", _clock);

        Assert.Equal(-2m, item.CurrentQuantity);
        DomainAssert.Raised<StockConsumed>(item);
        DomainAssert.NotRaised<StockWentNegative>(item);
    }

    [Fact]
    public void ConsumeForSale_down_to_exactly_zero_is_not_negative()
    {
        var item = NewItem(stockKg: 3m);

        item.ConsumeForSale(Kg(3m), "order-1", _clock);

        Assert.Equal(0m, item.CurrentQuantity);
        DomainAssert.NotRaised<StockWentNegative>(item);
    }

    [Fact]
    public void Manual_Consume_still_honours_the_policy_after_a_forced_sale_consumption()
    {
        var item = NewItem(stockKg: 1m);
        item.ConsumeForSale(Kg(2m), "order-1", _clock);
        item.ClearDomainEvents();

        DomainAssert.Throws(InventoryErrors.InsufficientStock, () => item.Consume(Kg(0.5m), _clock));
        DomainAssert.Throws(InventoryErrors.InsufficientStock, () => item.RecordWaste(Kg(0.5m), "dropped", _clock));

        Assert.Equal(-1m, item.CurrentQuantity);
        DomainAssert.NoEvents(item);

        // L'inventaire après l'incident ramène le stock à la réalité.
        item.Adjust(Kg(0.2m), "count after negative stock", _clock);
        Assert.Equal(0.2m, item.CurrentQuantity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ConsumeForSale_requires_the_order_reference(string? orderReference)
    {
        var item = NewItem(stockKg: 10m);

        DomainAssert.Throws(InventoryErrors.OrderReferenceRequired, () => item.ConsumeForSale(Kg(1m), orderReference!, _clock));

        Assert.Equal(10m, item.CurrentQuantity);
        Assert.Single(item.Movements);
        DomainAssert.NoEvents(item);
    }

    [Fact]
    public void ConsumeForSale_rejects_a_zero_quantity_an_incompatible_unit_and_a_too_long_reference()
    {
        var item = NewItem(stockKg: 10m);

        DomainAssert.Throws(InventoryErrors.MovementQuantityNotPositive, () => item.ConsumeForSale(Kg(0m), "order-1", _clock));
        DomainAssert.Throws(InventoryErrors.UnitIncompatible, () => item.ConsumeForSale(L(1m), "order-1", _clock));
        DomainAssert.Throws(
            InventoryErrors.MovementReferenceTooLong,
            () => item.ConsumeForSale(Kg(1m), new string('r', StockMovement.ReferenceMaxLength + 1), _clock));

        Assert.Equal(10m, item.CurrentQuantity);
        DomainAssert.NoEvents(item);
    }

    [Fact]
    public void An_order_id_fits_the_movement_reference()
    {
        var item = NewItem(stockKg: 10m);
        var orderId = OrderId.New().ToString();

        item.ConsumeForSale(Kg(1m), orderId, _clock);

        Assert.InRange(orderId.Length, 1, StockMovement.ReferenceMaxLength);
        Assert.Equal(100, StockMovement.ReferenceMaxLength);
        Assert.Equal(orderId, item.Movements.Last().Reference);
    }

    // ---- Transferts

    [Fact]
    public void Transfer_moves_the_same_quantity_under_the_same_reference()
    {
        var source = NewItem(stockKg: 10m);
        var destination = Counterpart(source);

        StockTransfer.Execute(source, destination, Kg(4m), " TR-0001 ", _clock);

        var outgoing = source.Movements.Last();
        var incoming = Assert.Single(destination.Movements);
        Assert.Equal(StockMovementType.Transfer, outgoing.Type);
        Assert.Equal(StockMovementType.Transfer, incoming.Type);
        Assert.Equal(-4m, outgoing.QuantityDelta);
        Assert.Equal(4m, incoming.QuantityDelta);
        Assert.Equal("TR-0001", outgoing.Reference);
        Assert.Equal(outgoing.Reference, incoming.Reference);
        Assert.Equal(outgoing.OccurredAtUtc, incoming.OccurredAtUtc);
        Assert.Equal(6m, source.CurrentQuantity);
        Assert.Equal(4m, destination.CurrentQuantity);
    }

    [Fact]
    public void Transfer_raises_StockTransferredOut_on_the_source()
    {
        var item = NewItem(stockKg: 10m);
        var destination = Counterpart(item);

        StockTransfer.Execute(item, destination, G(4000m), "TR-1", _clock);

        Assert.Equal(
            new StockTransferredOut(Tenant, item.Id, Location, item.IngredientId, 4m, 6m, UnitOfMeasure.Kilogram, "TR-1", _clock.UtcNow),
            DomainAssert.Raised<StockTransferredOut>(item));
        DomainAssert.NotRaised<StockTransferredIn>(item);
    }

    [Fact]
    public void Transfer_raises_StockTransferredIn_on_the_destination()
    {
        var item = NewItem(stockKg: 1m);
        var source = Counterpart(item, stockKg: 10m);

        StockTransfer.Execute(source, item, Kg(4m), "TR-1", _clock);

        Assert.Equal(
            new StockTransferredIn(Tenant, item.Id, Location, item.IngredientId, 4m, 5m, UnitOfMeasure.Kilogram, "TR-1", _clock.UtcNow),
            DomainAssert.Raised<StockTransferredIn>(item));
        DomainAssert.NotRaised<StockTransferredOut>(item);
    }

    [Fact]
    public void Transfer_converts_the_quantity_to_each_item_unit()
    {
        var source = NewItem(stockKg: 10m);
        var destination = Counterpart(source, unit: UnitOfMeasure.Gram);

        StockTransfer.Execute(source, destination, Kg(1.5m), "TR-1", _clock);

        Assert.Equal(8.5m, source.CurrentQuantity);
        Assert.Equal(1500m, destination.CurrentQuantity);
        Assert.Equal(UnitOfMeasure.Gram, destination.Movements.Last().Unit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Transfer_requires_a_transfer_reference(string transferReference)
    {
        var item = NewItem(stockKg: 10m);
        var destination = Counterpart(item);

        DomainAssert.Throws(InventoryErrors.TransferReferenceRequired, () => StockTransfer.Execute(item, destination, Kg(1m), transferReference, _clock));
        Assert.Equal(10m, item.CurrentQuantity);
        Assert.Single(item.Movements);
        Assert.Empty(destination.Movements);
    }

    [Fact]
    public void Transfer_rejects_a_zero_quantity_an_incompatible_unit_and_a_too_long_reference()
    {
        var item = NewItem(stockKg: 10m);
        var destination = Counterpart(item);

        DomainAssert.Throws(InventoryErrors.MovementQuantityNotPositive, () => StockTransfer.Execute(item, destination, Kg(0m), "TR-1", _clock));
        DomainAssert.Throws(InventoryErrors.UnitIncompatible, () => StockTransfer.Execute(item, destination, L(1m), "TR-1", _clock));
        DomainAssert.Throws(
            InventoryErrors.MovementReferenceTooLong,
            () => StockTransfer.Execute(item, destination, Kg(1m), new string('r', StockMovement.ReferenceMaxLength + 1), _clock));

        Assert.Single(item.Movements);
        Assert.Empty(destination.Movements);
        DomainAssert.NoEvents(item);
        DomainAssert.NoEvents(destination);
    }

    [Fact]
    public void Transfer_beyond_the_stock_is_rejected_when_negative_stock_is_forbidden()
    {
        var item = NewItem(stockKg: 1m);
        var destination = Counterpart(item);

        DomainAssert.Throws(InventoryErrors.InsufficientStock, () => StockTransfer.Execute(item, destination, Kg(1.5m), "TR-1", _clock));

        Assert.Equal(1m, item.CurrentQuantity);
        Assert.Equal(0m, destination.CurrentQuantity);
        DomainAssert.NoEvents(item);
        DomainAssert.NoEvents(destination);
    }

    [Fact]
    public void Transfer_beyond_the_stock_is_accepted_when_negative_stock_is_allowed()
    {
        var item = NewItem(stockKg: 1m, policy: NegativeStockPolicy.Allow);
        var destination = Counterpart(item);

        StockTransfer.Execute(item, destination, Kg(1.5m), "TR-1", _clock);

        Assert.Equal(-0.5m, item.CurrentQuantity);
        Assert.Equal(1.5m, destination.CurrentQuantity);
    }

    [Fact]
    public void Transfer_rejects_items_of_different_tenants()
    {
        var source = NewItem(stockKg: 10m);
        var destination = Counterpart(source, tenant: TenantId.New());

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => StockTransfer.Execute(source, destination, Kg(1m), "TR-1", _clock));

        Assert.Equal(10m, source.CurrentQuantity);
        Assert.Empty(destination.Movements);
    }

    [Fact]
    public void Transfer_rejects_items_of_different_ingredients()
    {
        var source = NewItem(stockKg: 10m);
        var destination = NewItem();

        DomainAssert.Throws(InventoryErrors.TransferIngredientMismatch, () => StockTransfer.Execute(source, destination, Kg(1m), "TR-1", _clock));

        Assert.Equal(10m, source.CurrentQuantity);
        Assert.Empty(destination.Movements);
        DomainAssert.NoEvents(source);
    }

    [Fact]
    public void Transfer_rejects_the_same_item_on_both_sides()
    {
        var item = NewItem(stockKg: 10m);

        DomainAssert.Throws(InventoryErrors.TransferSameItem, () => StockTransfer.Execute(item, item, Kg(1m), "TR-1", _clock));

        Assert.Single(item.Movements);
        DomainAssert.NoEvents(item);
    }

    [Fact]
    public void Transfer_rejected_by_the_destination_unit_leaves_the_source_untouched()
    {
        // 1 000 000 kg tient dans la source (kg) mais pas dans la destination tenue en grammes (1e9 g est le maximum).
        var source = NewItem(policy: NegativeStockPolicy.Allow);
        var destination = Counterpart(source, unit: UnitOfMeasure.Gram);

        DomainAssert.Throws(
            InventoryErrors.QuantityTooLarge,
            () => StockTransfer.Execute(source, destination, Kg(1_000_000.001m), "TR-1", _clock));

        Assert.Empty(source.Movements);
        Assert.Empty(destination.Movements);
        DomainAssert.NoEvents(source);
        DomainAssert.NoEvents(destination);
    }

    [Fact]
    public void A_one_sided_transfer_cannot_be_written_from_outside()
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        Assert.All(
            new[] { "TransferOut", "TransferIn" },
            name => Assert.True(typeof(InventoryItem).GetMethod(name, flags)!.IsAssembly));
    }

    // ---- Stock bas

    [Fact]
    public void LowStockReached_is_raised_when_the_stock_falls_exactly_to_the_threshold()
    {
        var item = NewItem(stockKg: 5m, thresholdKg: 2m);

        item.Consume(Kg(3m), _clock);

        var low = DomainAssert.Raised<LowStockReached>(item);
        Assert.Equal(
            new LowStockReached(Tenant, item.Id, Location, item.IngredientId, 2m, 2m, UnitOfMeasure.Kilogram, _clock.UtcNow),
            low);
        Assert.True(item.IsLowStock);
    }

    [Fact]
    public void LowStockReached_is_raised_when_the_stock_falls_below_the_threshold()
    {
        var item = NewItem(stockKg: 5m, thresholdKg: 2m);

        item.Consume(Kg(4m), _clock);

        Assert.Equal(1m, DomainAssert.Raised<LowStockReached>(item).CurrentQuantity);
    }

    [Fact]
    public void LowStockReached_is_not_raised_while_the_stock_stays_above_the_threshold()
    {
        var item = NewItem(stockKg: 5m, thresholdKg: 2m);

        item.Consume(Kg(2.999m), _clock);

        DomainAssert.NotRaised<LowStockReached>(item);
        Assert.False(item.IsLowStock);
    }

    [Fact]
    public void LowStockReached_is_not_raised_again_while_the_stock_is_already_low()
    {
        var item = NewItem(stockKg: 5m, thresholdKg: 2m);
        item.Consume(Kg(3m), _clock);
        item.ClearDomainEvents();

        item.Consume(Kg(0.5m), _clock);
        item.RecordWaste(Kg(0.5m), "expired", _clock);
        item.Receive(Kg(0.2m), _clock);

        DomainAssert.NotRaised<LowStockReached>(item);
    }

    [Fact]
    public void LowStockReached_is_raised_again_after_a_replenishment_and_a_new_crossing()
    {
        var item = NewItem(stockKg: 5m, thresholdKg: 2m);
        item.Consume(Kg(4m), _clock);
        item.Receive(Kg(9m), _clock);
        item.ClearDomainEvents();

        item.Consume(Kg(8m), _clock);

        DomainAssert.Raised<LowStockReached>(item);
    }

    [Fact]
    public void LowStockReached_is_raised_by_waste_transfer_out_and_downward_adjustment()
    {
        var wasted = NewItem(stockKg: 5m, thresholdKg: 2m);
        wasted.RecordWaste(Kg(3m), "expired", _clock);
        DomainAssert.Raised<LowStockReached>(wasted);

        var transferred = NewItem(stockKg: 5m, thresholdKg: 2m);
        StockTransfer.Execute(transferred, Counterpart(transferred), Kg(3m), "TR-1", _clock);
        DomainAssert.Raised<LowStockReached>(transferred);

        var adjusted = NewItem(stockKg: 5m, thresholdKg: 2m);
        adjusted.Adjust(Kg(1m), "count", _clock);
        DomainAssert.Raised<LowStockReached>(adjusted);
    }

    [Fact]
    public void LowStockReached_is_not_raised_by_an_upward_adjustment_or_a_transfer_in()
    {
        var item = NewItem(stockKg: 1m, thresholdKg: 2m);

        item.Adjust(Kg(1.5m), "count", _clock);
        StockTransfer.Execute(Counterpart(item, stockKg: 1m), item, Kg(0.1m), "TR-1", _clock);

        DomainAssert.NotRaised<LowStockReached>(item);
    }

    [Fact]
    public void ChangeReorderThreshold_converts_the_threshold_and_raises_LowStockReached_when_it_overtakes_the_stock()
    {
        var item = NewItem(stockKg: 5m, thresholdKg: 2m);

        item.ChangeReorderThreshold(G(6000m), _clock);

        Assert.Equal(Kg(6m), item.ReorderThreshold);
        Assert.Equal(6m, DomainAssert.Raised<LowStockReached>(item).ReorderThreshold);
    }

    [Fact]
    public void ChangeReorderThreshold_raises_nothing_when_the_stock_stays_above_or_was_already_low()
    {
        var above = NewItem(stockKg: 5m, thresholdKg: 2m);
        above.ChangeReorderThreshold(Kg(3m), _clock);
        DomainAssert.NoEvents(above);

        var alreadyLow = NewItem(stockKg: 1m, thresholdKg: 2m);
        alreadyLow.ChangeReorderThreshold(Kg(4m), _clock);
        DomainAssert.NoEvents(alreadyLow);
    }

    [Fact]
    public void ChangeReorderThreshold_to_the_same_threshold_is_a_silent_no_op()
    {
        var item = NewItem(stockKg: 1m, thresholdKg: 2m);

        item.ChangeReorderThreshold(G(2000m), _clock);

        Assert.Equal(Kg(2m), item.ReorderThreshold);
        DomainAssert.NoEvents(item);
    }

    [Fact]
    public void ChangeReorderThreshold_rejects_an_incompatible_unit()
    {
        var item = NewItem(stockKg: 5m, thresholdKg: 2m);

        DomainAssert.Throws(InventoryErrors.UnitIncompatible, () => item.ChangeReorderThreshold(L(1m), _clock));
        Assert.Equal(Kg(2m), item.ReorderThreshold);
    }

    // ---- Politique de stock négatif

    [Fact]
    public void ChangeNegativeStockPolicy_switches_the_policy()
    {
        var item = NewItem(stockKg: 1m);

        item.ChangeNegativeStockPolicy(NegativeStockPolicy.Allow);
        item.Consume(Kg(2m), _clock);

        Assert.Equal(NegativeStockPolicy.Allow, item.NegativeStockPolicy);
        Assert.Equal(-1m, item.CurrentQuantity);
    }

    [Fact]
    public void ChangeNegativeStockPolicy_cannot_forbid_while_the_stock_is_negative()
    {
        var item = NewItem(stockKg: 1m, policy: NegativeStockPolicy.Allow);
        item.Consume(Kg(2m), _clock);

        DomainAssert.Throws(InventoryErrors.ItemStockIsNegative, () => item.ChangeNegativeStockPolicy(NegativeStockPolicy.Forbid));
        Assert.Equal(NegativeStockPolicy.Allow, item.NegativeStockPolicy);
    }

    [Fact]
    public void ChangeNegativeStockPolicy_to_the_current_policy_is_a_silent_no_op()
    {
        var forbidding = NewItem(stockKg: 1m);
        forbidding.ConsumeForSale(Kg(2m), "order-1", _clock);
        forbidding.ClearDomainEvents();
        var allowing = NewItem(policy: NegativeStockPolicy.Allow);

        // Toujours sans effet tant que le stock est négatif sous Forbid (forcé par une vente) : rien n'est modifié.
        forbidding.ChangeNegativeStockPolicy(NegativeStockPolicy.Forbid);
        allowing.ChangeNegativeStockPolicy(NegativeStockPolicy.Allow);

        Assert.Equal(NegativeStockPolicy.Forbid, forbidding.NegativeStockPolicy);
        Assert.Equal(NegativeStockPolicy.Allow, allowing.NegativeStockPolicy);
        DomainAssert.NoEvents(forbidding);
        DomainAssert.NoEvents(allowing);
    }

    [Fact]
    public void ChangeNegativeStockPolicy_rejects_an_unknown_policy() =>
        DomainAssert.Throws(InventoryErrors.ItemPolicyUnknown, () => NewItem().ChangeNegativeStockPolicy((NegativeStockPolicy)0));

    // ---- Registre = vérité

    [Fact]
    public void Current_quantity_always_equals_the_sum_of_the_ledger_deltas()
    {
        var item = NewItem(policy: NegativeStockPolicy.Allow);
        var other = Counterpart(item, stockKg: 50m, policy: NegativeStockPolicy.Allow);
        Action[] operations =
        [
            () => item.Receive(Kg(12.5m), _clock, "PO-1"),
            () => item.Consume(G(730m), _clock, "order-1"),
            () => item.RecordWaste(Kg(0.27m), "dropped", _clock),
            () => StockTransfer.Execute(item, other, Kg(3m), "TR-1", _clock),
            () => StockTransfer.Execute(other, item, G(1250m), "TR-2", _clock),
            () => item.ConsumeForSale(Kg(20m), "order-3", _clock),
            () => item.Adjust(Kg(9.001m), "count", _clock),
            () => item.Consume(Kg(11m), _clock, "order-2"),
            () => item.Adjust(Kg(0m), "count", _clock),
            () => item.Adjust(Kg(0m), "count again", _clock),
            () => item.Receive(G(1m), _clock),
        ];

        foreach (var operation in operations)
        {
            operation();
            AssertProjectionMatchesLedger(item);
            AssertProjectionMatchesLedger(other);
        }

        Assert.Equal(operations.Length, item.Movements.Count);
        Assert.Equal(0.001m, item.CurrentQuantity);
    }

    [Fact]
    public void Quantity_moves_by_exactly_the_appended_deltas_over_a_long_random_sequence()
    {
        var item = NewItem(policy: NegativeStockPolicy.Forbid);
        var other = Counterpart(item, stockKg: 500m);
        var random = new Random(20261006);
        var stored = new List<StockMovement>();

        for (var step = 0; step < 3000; step++)
        {
            // Régulièrement, l'article est « rechargé » : sa quantité stockée reste, ses mouvements disparaissent.
            if (step % 6 == 0)
            {
                stored.AddRange(item.Movements);
                Reload(item);
                Assert.Empty(item.Movements);
            }

            var quantityBefore = item.CurrentQuantity;
            var movementsBefore = item.Movements.Count;
            var grams = G(random.Next(0, 4000));
            try
            {
                switch (random.Next(0, 9))
                {
                    case 0:
                    case 1: item.Receive(grams, _clock, "PO"); break;
                    case 2: item.Consume(grams, _clock); break;
                    case 3: item.ConsumeForSale(grams, $"order-{step}", _clock); break;
                    case 4: item.RecordWaste(grams, "dropped", _clock); break;
                    case 5: item.Adjust(grams, "count", _clock); break;
                    case 6: item.Adjust(Quantity.Create(Math.Max(0m, item.CurrentQuantity), UnitOfMeasure.Kilogram), "count", _clock); break;
                    case 7: StockTransfer.Execute(item, other, grams, $"TR-{step}", _clock); break;
                    default: StockTransfer.Execute(other, item, grams, $"TR-{step}", _clock); break;
                }
            }
            catch (DomainException)
            {
                // Une opération rejetée ne doit rien ajouter ni toucher à la quantité ; vérifié plus bas.
            }

            var appended = item.Movements.Skip(movementsBefore).ToArray();
            Assert.InRange(appended.Length, 0, 1);
            Assert.Equal(quantityBefore + appended.Sum(movement => movement.QuantityDelta), item.CurrentQuantity);
            Assert.All(appended, movement => Assert.Equal((item.TenantId, item.Id), (movement.TenantId, movement.InventoryItemId)));
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        // Le registre stocké reste la vérité : la projection égale sa somme, bien qu'il n'ait jamais été rechargé.
        stored.AddRange(item.Movements);
        Assert.True(stored.Count > 1000);
        Assert.Equal(stored.Sum(movement => movement.QuantityDelta), item.CurrentQuantity);
        Assert.Equal(stored.Count, stored.Select(movement => movement.Id).Distinct().Count());
        Assert.Contains(stored, movement => movement.Type == StockMovementType.CountConfirmed);
        Assert.All(stored, movement => Assert.Equal(movement.Type == StockMovementType.CountConfirmed, movement.QuantityDelta == 0m));
    }

    [Fact]
    public void Operations_do_not_need_the_history_in_memory()
    {
        var item = NewItem(stockKg: 10m, thresholdKg: 2m);
        Reload(item);

        item.Consume(Kg(8m), _clock, "order-1");
        DomainAssert.Throws(InventoryErrors.InsufficientStock, () => item.Consume(Kg(3m), _clock));
        item.Adjust(Kg(2m), "count", _clock);

        Assert.Equal(2m, item.CurrentQuantity);
        Assert.Equal(
            [StockMovementType.Consumption, StockMovementType.CountConfirmed],
            item.Movements.Select(movement => movement.Type));
        DomainAssert.Raised<LowStockReached>(item);
    }

    [Fact]
    public void Transfer_stamps_both_sides_with_the_same_instant()
    {
        var source = NewItem(stockKg: 10m);
        var destination = Counterpart(source);
        var clock = new TickingClock();

        StockTransfer.Execute(source, destination, Kg(4m), "TR-1", clock);

        var outgoing = DomainAssert.Raised<StockTransferredOut>(source);
        var incoming = DomainAssert.Raised<StockTransferredIn>(destination);
        Assert.Equal(outgoing.OccurredAtUtc, incoming.OccurredAtUtc);
        Assert.Equal(source.Movements.Last().OccurredAtUtc, destination.Movements.Last().OccurredAtUtc);
        Assert.Equal(1, clock.Reads);
    }

    [Fact]
    public void A_rejected_operation_leaves_the_ledger_and_the_projection_untouched()
    {
        var item = NewItem(stockKg: 2m);

        Assert.Throws<DomainException>(() => item.Consume(Kg(5m), _clock));
        Assert.Throws<DomainException>(() => item.RecordWaste(Kg(1m), " ", _clock));
        Assert.Throws<DomainException>(() => StockTransfer.Execute(item, Counterpart(item), Kg(1m), "", _clock));
        Assert.Throws<DomainException>(() => item.Adjust(Kg(1m), " ", _clock));
        Assert.Throws<DomainException>(() => item.ConsumeForSale(Kg(1m), " ", _clock));
        Assert.Throws<DomainException>(() => item.Receive(L(1m), _clock));

        Assert.Single(item.Movements);
        Assert.Equal(2m, item.CurrentQuantity);
        DomainAssert.NoEvents(item);
        AssertProjectionMatchesLedger(item);
    }

    [Fact]
    public void Movements_are_timestamped_by_the_clock_in_order()
    {
        var item = NewItem();
        var first = _clock.UtcNow;

        item.Receive(Kg(1m), _clock);
        _clock.Advance(TimeSpan.FromMinutes(5));
        item.Consume(Kg(1m), _clock);

        Assert.Equal([first, first.AddMinutes(5)], item.Movements.Select(movement => movement.OccurredAtUtc));
    }

    [Fact]
    public void Movements_cannot_be_modified_from_outside()
    {
        var item = NewItem(stockKg: 1m);

        var exposed = Assert.IsAssignableFrom<ICollection<StockMovement>>(item.Movements);

        Assert.True(exposed.IsReadOnly);
        Assert.Throws<NotSupportedException>(exposed.Clear);
    }
}
