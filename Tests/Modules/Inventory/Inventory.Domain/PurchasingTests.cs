namespace Inventory.Domain;

public class PurchasingTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly LocationReference Place = new(Tenant, LocationId.New(), IsOperational: true);

    private readonly FakeClock _clock = new();

    private static Quantity Kg(decimal amount) => Quantity.Create(amount, UnitOfMeasure.Kilogram);

    private static InventoryItem NewItem(LocationReference? place = null, string name = "Farine")
    {
        var ingredient = Ingredient.Create(Tenant, name, UnitOfMeasure.Kilogram, isNameUnique: true);
        return InventoryItem.Create(Tenant, place ?? Place, ingredient.ToReference(), UnitOfMeasure.Kilogram, Kg(1m), isUniqueForLocation: true);
    }

    private Supplier NewSupplier() => Supplier.Create(Tenant, "Grossiste", null, null, null, _clock);

    private PurchaseOrder SentOrder(InventoryItem item, decimal ordered = 10m, decimal? price = 2m)
    {
        var order = PurchaseOrder.Create(Tenant, Place, NewSupplier(), _clock);
        order.Compose([(item, Kg(ordered), price)], "Livrer le matin");
        order.Send(_clock);
        return order;
    }

    [Fact]
    public void A_priced_receipt_sets_the_unit_cost()
    {
        var item = NewItem();

        item.Receive(Kg(4m), _clock, "BL-1", totalCost: 10m);

        Assert.Equal(2.5m, item.AverageUnitCost);
        Assert.Equal(2.5m, item.Movements.Single().UnitCost);
    }

    [Fact]
    public void Receipts_average_the_cost_by_quantity_on_hand()
    {
        var item = NewItem();
        item.Receive(Kg(4m), _clock, totalCost: 8m);
        item.Consume(Kg(2m), _clock);

        item.Receive(Kg(2m), _clock, totalCost: 8m);

        Assert.Equal(3m, item.AverageUnitCost);
    }

    [Fact]
    public void An_unpriced_receipt_keeps_the_cost()
    {
        var item = NewItem();
        item.Receive(Kg(2m), _clock, totalCost: 6m);

        item.Receive(Kg(2m), _clock);

        Assert.Equal(3m, item.AverageUnitCost);
        Assert.Null(item.Movements.Last().UnitCost);
    }

    [Fact]
    public void Receiving_into_an_empty_stock_restarts_the_average()
    {
        var item = NewItem();
        item.Receive(Kg(2m), _clock, totalCost: 2m);
        item.Consume(Kg(2m), _clock);

        item.Receive(Kg(1m), _clock, totalCost: 5m);

        Assert.Equal(5m, item.AverageUnitCost);
    }

    [Fact]
    public void A_negative_cost_is_refused()
    {
        var item = NewItem();

        DomainAssert.Throws(InventoryErrors.CostInvalid, () => item.Receive(Kg(1m), _clock, totalCost: -1m));
        DomainAssert.Throws(InventoryErrors.CostInvalid, () => item.SetUnitCost(-0.5m));
    }

    [Fact]
    public void The_unit_cost_can_be_set_and_cleared()
    {
        var item = NewItem();

        item.SetUnitCost(1.234m);
        Assert.Equal(1.234m, item.AverageUnitCost);

        item.SetUnitCost(null);
        Assert.Null(item.AverageUnitCost);
    }

    [Fact]
    public void A_new_purchase_order_is_a_numbered_draft()
    {
        var order = PurchaseOrder.Create(Tenant, Place, NewSupplier(), _clock);

        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.StartsWith("BC-", order.Number, StringComparison.Ordinal);
        Assert.Equal(14, order.Number.Length);
    }

    [Fact]
    public void An_inactive_supplier_takes_no_order()
    {
        var supplier = NewSupplier();
        supplier.Deactivate();

        DomainAssert.Throws(PurchasingErrors.SupplierInactive, () => PurchaseOrder.Create(Tenant, Place, supplier, _clock));
    }

    [Fact]
    public void An_empty_order_cannot_be_sent()
    {
        var order = PurchaseOrder.Create(Tenant, Place, NewSupplier(), _clock);

        DomainAssert.Throws(PurchasingErrors.OrderEmpty, () => order.Send(_clock));
    }

    [Fact]
    public void Lines_belong_to_the_order_location()
    {
        var order = PurchaseOrder.Create(Tenant, Place, NewSupplier(), _clock);
        var elsewhere = NewItem(new LocationReference(Tenant, LocationId.New(), IsOperational: true));

        DomainAssert.Throws(PurchasingErrors.LineElsewhere, () => order.Compose([(elsewhere, Kg(1m), null)], null));
    }

    [Fact]
    public void An_item_appears_once_on_an_order()
    {
        var order = PurchaseOrder.Create(Tenant, Place, NewSupplier(), _clock);
        var item = NewItem();

        DomainAssert.Throws(PurchasingErrors.LineDuplicate, () => order.Compose([(item, Kg(1m), null), (item, Kg(2m), null)], null));
    }

    [Fact]
    public void The_estimated_total_needs_every_price()
    {
        var order = PurchaseOrder.Create(Tenant, Place, NewSupplier(), _clock);
        var flour = NewItem();
        var sugar = NewItem(name: "Sucre");

        order.Compose([(flour, Kg(10m), 2m), (sugar, Kg(5m), 3m)], null);
        Assert.Equal(35m, order.EstimatedTotal);

        order.Compose([(flour, Kg(10m), 2m), (sugar, Kg(5m), null)], null);
        Assert.Null(order.EstimatedTotal);
    }

    [Fact]
    public void A_sent_order_can_no_longer_be_changed()
    {
        var item = NewItem();
        var order = SentOrder(item);

        DomainAssert.Throws(PurchasingErrors.OrderNotDraft, () => order.Compose([(item, Kg(1m), null)], null));
    }

    [Fact]
    public void A_draft_cannot_be_received()
    {
        var item = NewItem();
        var order = PurchaseOrder.Create(Tenant, Place, NewSupplier(), _clock);
        order.Compose([(item, Kg(1m), null)], null);

        DomainAssert.Throws(PurchasingErrors.OrderNotReceivable, () => order.Receive([(item.Id, 1m, null)], _clock));
    }

    [Fact]
    public void A_partial_delivery_leaves_the_rest_to_come()
    {
        var item = NewItem();
        var order = SentOrder(item);

        var receipts = order.Receive([(item.Id, 4m, null)], _clock);

        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
        Assert.Equal(6m, order.Lines.Single().Remaining);
        var receipt = Assert.Single(receipts);
        Assert.Equal(Kg(4m), receipt.Quantity);
        Assert.Equal(8m, receipt.TotalCost);
    }

    [Fact]
    public void The_full_delivery_closes_the_order_at_the_observed_price()
    {
        var item = NewItem();
        var order = SentOrder(item);

        var receipts = order.Receive([(item.Id, 10m, 2.5m)], _clock);

        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Equal(_clock.UtcNow, order.ReceivedAtUtc);
        Assert.Equal(25m, receipts.Single().TotalCost);
        Assert.Equal(2.5m, order.Lines.Single().UnitPrice);
    }

    [Fact]
    public void A_partly_received_order_can_be_closed_but_not_cancelled()
    {
        var item = NewItem();
        var order = SentOrder(item);
        order.Receive([(item.Id, 4m, null)], _clock);

        DomainAssert.Throws(PurchasingErrors.OrderNotCancellable, order.Cancel);
        order.CloseAsReceived(_clock);

        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
    }

    [Fact]
    public void Receiving_an_item_not_on_the_order_is_refused()
    {
        var order = SentOrder(NewItem());

        DomainAssert.Throws(PurchasingErrors.LineNotOnOrder, () => order.Receive([(InventoryItemId.New(), 1m, null)], _clock));
    }

    [Fact]
    public void A_delivery_needs_a_quantity()
    {
        var item = NewItem();
        var order = SentOrder(item);

        DomainAssert.Throws(PurchasingErrors.NothingReceived, () => order.Receive([(item.Id, 0m, null)], _clock));
    }

    [Fact]
    public void A_supplier_needs_a_name()
    {
        DomainAssert.Throws(PurchasingErrors.SupplierNameInvalid, () => Supplier.Create(Tenant, "  ", null, null, null, _clock));
    }
}
