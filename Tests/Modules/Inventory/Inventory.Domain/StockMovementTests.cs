using System.Reflection;

namespace Inventory.Domain;

public class StockMovementTests
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly FakeClock _clock = new();

    private static Quantity Kg(decimal amount) => Quantity.Create(amount, UnitOfMeasure.Kilogram);

    private InventoryItem NewStockedItem()
    {
        var tenantId = TenantId.New();
        var item = InventoryItem.Create(
            tenantId,
            new LocationReference(tenantId, LocationId.New(), IsOperational: true),
            Ingredient.Create(tenantId, "Flour", UnitOfMeasure.Gram, isNameUnique: true).ToReference(),
            UnitOfMeasure.Kilogram,
            Kg(1m),
            isUniqueForLocation: true);
        item.Receive(Kg(10m), _clock, "PO-1");
        return item;
    }

    [Fact]
    public void StockMovement_exposes_no_public_or_internal_setter()
    {
        var properties = typeof(StockMovement).GetProperties(Declared)
            .Append(typeof(StockMovement).GetProperty(nameof(StockMovement.Id))!)
            .ToList();

        Assert.NotEmpty(properties);
        Assert.All(properties, property => Assert.True(
            property.SetMethod is null || property.SetMethod.IsPrivate,
            $"{property.Name} has a non-private setter."));
    }

    [Fact]
    public void StockMovement_exposes_no_public_or_internal_mutating_method()
    {
        var reachableInstanceMethods = typeof(StockMovement).GetMethods(Declared)
            .Where(method => !method.IsSpecialName && !method.IsPrivate)
            .Select(method => method.Name);

        Assert.Empty(reachableInstanceMethods);
    }

    [Fact]
    public void StockMovement_exposes_no_writable_field_and_no_public_constructor()
    {
        var writableFields = typeof(StockMovement).GetFields(Declared)
            .Where(field => !field.IsPrivate && !field.IsInitOnly)
            .Select(field => field.Name);

        Assert.Empty(writableFields);
        Assert.Empty(typeof(StockMovement).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void A_movement_keeps_its_values_whatever_happens_to_the_item_afterwards()
    {
        var item = NewStockedItem();
        var movement = Assert.Single(item.Movements);
        var recordedAt = _clock.UtcNow;

        _clock.Advance(TimeSpan.FromDays(1));
        item.Consume(Kg(4m), _clock, "order-1");
        item.Adjust(Kg(5m), "count", _clock);
        item.ChangeReorderThreshold(Kg(3m), _clock);

        Assert.Same(movement, item.Movements.First());
        Assert.Equal(StockMovementType.Receipt, movement.Type);
        Assert.Equal(10m, movement.QuantityDelta);
        Assert.Equal(UnitOfMeasure.Kilogram, movement.Unit);
        Assert.Equal(recordedAt, movement.OccurredAtUtc);
        Assert.Equal("PO-1", movement.Reference);
    }

    [Fact]
    public void A_mistake_is_corrected_by_a_new_movement_not_by_editing_the_ledger()
    {
        var item = NewStockedItem();
        var before = item.Movements.Select(movement => (movement.Id, movement.QuantityDelta)).ToList();

        item.Adjust(Kg(9m), "receipt was over-counted", _clock);

        Assert.Equal(before, item.Movements.Take(before.Count).Select(movement => (movement.Id, movement.QuantityDelta)));
        Assert.Equal(before.Count + 1, item.Movements.Count);
        Assert.Equal(-1m, item.Movements.Last().QuantityDelta);
    }

    [Fact]
    public void Each_movement_has_its_own_identity()
    {
        var item = NewStockedItem();
        item.Receive(Kg(10m), _clock, "PO-1");

        Assert.Equal(2, item.Movements.Select(movement => movement.Id).Distinct().Count());
        Assert.NotEqual(item.Movements.First(), item.Movements.Last());
    }

    [Fact]
    public void A_movement_carries_its_tenant_and_its_item()
    {
        var item = NewStockedItem();

        var movement = Assert.Single(item.Movements);

        Assert.Equal((item.TenantId, item.Id), (movement.TenantId, movement.InventoryItemId));
    }
}
