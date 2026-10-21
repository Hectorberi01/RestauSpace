namespace Orders.Domain;

public class OrderItemsTests
{
    [Fact]
    public void AddItem_copies_the_product_snapshot_onto_the_line()
    {
        var order = Given.DraftOrder();
        var cheese = Given.Modifier("Cheese", Given.Eur(1.00m));
        var product = Given.Product(Given.Eur(9.99m), Given.Vat10, "Burger", null, cheese);

        var itemId = order.AddItem(product, Given.Qty(2));

        var item = Assert.Single(order.Items);
        Assert.Equal(itemId, item.Id);
        Assert.Equal(product.ProductId, item.ProductId);
        Assert.Equal("Burger", item.ProductName);
        Assert.Equal(Given.Eur(9.99m), item.UnitPrice);
        Assert.Equal(2, item.Quantity.Value);
        Assert.Equal(Given.Vat10, item.Tax);
        Assert.Equal(cheese, Assert.Single(item.Modifiers));
    }

    [Fact]
    public void Line_total_is_unit_price_plus_modifier_deltas_times_quantity()
    {
        var order = Given.DraftOrder();
        var product = Given.Product(
            Given.Eur(9.99m), Given.Vat10, "Burger", null,
            Given.Modifier("Cheese", Given.Eur(1.50m)),
            Given.Modifier("No bun", Given.Eur(-0.49m)));

        order.AddItem(product, Given.Qty(3));

        var item = Assert.Single(order.Items);
        Assert.Equal(Given.Eur(11.00m), item.UnitPriceWithModifiers);
        Assert.Equal(Given.Eur(33.00m), item.LineTotal);
        Assert.Equal(Given.Eur(0m), item.DiscountAmount);
        Assert.Equal(Given.Eur(33.00m), item.TaxableAmount);
        Assert.Equal(Given.Eur(3.30m), item.TaxAmount);
        Assert.Equal(Given.Eur(36.30m), item.TotalWithTax);
    }

    [Fact]
    public void Line_total_of_a_line_without_modifier_is_unit_price_times_quantity()
    {
        var order = Given.DraftOrder(Currency.XOF);

        order.AddItem(Given.Product(Given.Xof(2500m), Given.Vat18), Given.Qty(4));

        Assert.Equal(Given.Xof(10000m), Assert.Single(order.Items).LineTotal);
    }

    [Fact]
    public void AddItem_keeps_the_same_product_added_twice_as_two_lines()
    {
        var order = Given.DraftOrder();
        var product = Given.Product(Given.Eur(5m));

        var first = order.AddItem(product, Given.Qty(1));
        var second = order.AddItem(product, Given.Qty(2));

        Assert.NotEqual(first, second);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(Given.Eur(15m), order.SubTotal);
    }

    [Fact]
    public void AddItem_rejects_a_product_priced_in_another_currency()
    {
        var order = Given.DraftOrder(Currency.EUR);

        DomainAssert.Throws(OrderErrors.CurrencyMismatch, () => order.AddItem(Given.Product(Given.Xof(1000m)), Given.Qty(1)));

        Assert.Empty(order.Items);
        Assert.Equal(Given.Eur(0m), order.Total);
    }

    [Fact]
    public void AddItem_rejects_a_modifier_priced_in_another_currency()
    {
        var order = Given.DraftOrder(Currency.EUR);
        var product = Given.Product(Given.Eur(10m), null, "Burger", null, Given.Modifier("Sauce", Given.Xof(100m)));

        DomainAssert.Throws(OrderErrors.CurrencyMismatch, () => order.AddItem(product, Given.Qty(1)));

        Assert.Empty(order.Items);
    }

    [Fact]
    public void AddItem_rejects_a_product_of_another_tenant()
    {
        var order = Given.DraftOrder();
        var foreign = Given.Product(Given.Eur(10m), tenant: TenantId.New());

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => order.AddItem(foreign, Given.Qty(1)));

        Assert.Empty(order.Items);
    }

    [Fact]
    public void AddItem_rejects_the_same_modifier_twice_in_the_snapshot()
    {
        var order = Given.DraftOrder();
        var cheese = Given.Modifier("Cheese", Given.Eur(1m));
        var product = Given.Product(Given.Eur(10m), null, "Burger", null, cheese, cheese);

        DomainAssert.Throws(OrderErrors.ItemModifierDuplicate, () => order.AddItem(product, Given.Qty(1)));

        Assert.Empty(order.Items);
    }

    [Fact]
    public void AddItem_rejects_modifiers_that_make_the_unit_price_negative()
    {
        var order = Given.DraftOrder();
        var product = Given.Product(Given.Eur(1m), null, "Burger", null, Given.Modifier("Refund trick", Given.Eur(-1.01m)));

        DomainAssert.Throws(OrderErrors.ItemUnitPriceNegative, () => order.AddItem(product, Given.Qty(1)));
    }

    [Fact]
    public void AddItem_accepts_modifiers_that_bring_the_unit_price_to_exactly_zero()
    {
        var order = Given.DraftOrder();
        var product = Given.Product(Given.Eur(1m), null, "Burger", null, Given.Modifier("Offered", Given.Eur(-1m)));

        order.AddItem(product, Given.Qty(3));

        Assert.Equal(Given.Eur(0m), order.Total);
    }

    [Fact]
    public void AddItem_rejects_one_line_more_than_the_maximum()
    {
        var order = Given.DraftOrder();
        var product = Given.Product(Given.Eur(1m));
        for (var i = 0; i < Order.MaxItems; i++)
        {
            order.AddItem(product, Given.Qty(1));
        }

        DomainAssert.Throws(OrderErrors.TooManyItems, () => order.AddItem(product, Given.Qty(1)));
        Assert.Equal(Order.MaxItems, order.Items.Count);
    }

    [Fact]
    public void AddItem_requires_a_snapshot_and_a_quantity()
    {
        var order = Given.DraftOrder();

        Assert.Throws<ArgumentNullException>(() => order.AddItem(null!, Given.Qty(1)));
        Assert.Throws<ArgumentNullException>(() => order.AddItem(Given.Product(Given.Eur(1m)), null!));
    }

    [Fact]
    public void Item_mutations_raise_no_event()
    {
        var order = Given.DraftOrder();
        order.ClearDomainEvents();

        var id = order.AddItem(Given.Product(Given.Eur(1m)), Given.Qty(1));
        order.ChangeItemQuantity(id, Given.Qty(2));
        order.ApplyDiscount(Given.Discount(Given.Eur(0.50m)));
        order.RemoveItem(id);

        DomainAssert.NoEvents(order);
    }

    [Fact]
    public void RemoveItem_removes_the_line_and_recalculates()
    {
        var order = Given.DraftOrder();
        var burger = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));
        order.AddItem(Given.Product(Given.Eur(4m), Given.Vat10), Given.Qty(1));

        order.RemoveItem(burger);

        Assert.Single(order.Items);
        Assert.Equal(Given.Eur(4m), order.SubTotal);
        Assert.Equal(Given.Eur(0.40m), order.TaxTotal);
        Assert.Equal(Given.Eur(4.40m), order.Total);
    }

    [Fact]
    public void RemoveItem_of_the_last_line_brings_every_total_back_to_zero()
    {
        var order = Given.DraftOrder();
        var burger = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(3m)));

        order.RemoveItem(burger);

        Assert.Equal(Given.Eur(0m), order.SubTotal);
        Assert.Equal(Given.Eur(0m), order.DiscountTotal);
        Assert.Equal(Given.Eur(0m), order.TaxTotal);
        Assert.Equal(Given.Eur(0m), order.Total);
    }

    [Fact]
    public void RemoveItem_rejects_an_unknown_line()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));

        DomainAssert.Throws(OrderErrors.ItemNotFound, () => order.RemoveItem(OrderItemId.New()));

        Assert.Single(order.Items);
    }

    [Fact]
    public void ChangeItemQuantity_updates_the_line_and_the_totals()
    {
        var order = Given.DraftOrder();
        var burger = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));

        order.ChangeItemQuantity(burger, Given.Qty(3));

        var item = Assert.Single(order.Items);
        Assert.Equal(3, item.Quantity.Value);
        Assert.Equal(Given.Eur(30m), item.LineTotal);
        Assert.Equal(Given.Eur(30m), order.SubTotal);
        Assert.Equal(Given.Eur(6m), order.TaxTotal);
        Assert.Equal(Given.Eur(36m), order.Total);
    }

    [Fact]
    public void ChangeItemQuantity_to_the_same_quantity_changes_nothing()
    {
        var order = Given.DraftOrder();
        var id = order.AddItem(Given.Product(Given.Eur(10m), Given.Vat20), Given.Qty(2));
        order.ClearDomainEvents();

        order.ChangeItemQuantity(id, Given.Qty(2));

        Assert.Equal(2, Assert.Single(order.Items).Quantity.Value);
        Assert.Equal(Given.Eur(24m), order.Total);
        DomainAssert.NoEvents(order);
    }

    [Fact]
    public void ChangeItemQuantity_rejects_an_unknown_line() =>
        DomainAssert.Throws(OrderErrors.ItemNotFound, () => Given.DraftOrder().ChangeItemQuantity(OrderItemId.New(), Given.Qty(2)));

    [Fact]
    public void A_quantity_of_zero_cannot_even_be_expressed()
    {
        var order = Given.DraftOrder();
        var burger = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));

        DomainAssert.Throws(OrderErrors.QuantityNotPositive, () => order.ChangeItemQuantity(burger, Quantity.Create(0)));
        DomainAssert.Throws(OrderErrors.QuantityNotPositive, () => order.AddItem(Given.Product(Given.Eur(1m)), Quantity.Create(-2)));
        Assert.Equal(1, Assert.Single(order.Items).Quantity.Value);
    }

    [Fact]
    public void AddItemModifier_adds_the_option_and_recalculates()
    {
        var order = Given.DraftOrder();
        var burger = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(2));

        order.AddItemModifier(burger, Given.Modifier("Bacon", Given.Eur(2m)));

        Assert.Equal(Given.Eur(24m), Assert.Single(order.Items).LineTotal);
        Assert.Equal(Given.Eur(24m), order.SubTotal);
        Assert.Equal(Given.Eur(28.80m), order.Total);
    }

    [Fact]
    public void AddItemModifier_rejects_a_duplicate_modifier()
    {
        var order = Given.DraftOrder();
        var bacon = Given.Modifier("Bacon", Given.Eur(2m));
        var burger = order.AddItem(Given.Product(Given.Eur(10m), null, "Burger", null, bacon), Given.Qty(1));

        DomainAssert.Throws(OrderErrors.ItemModifierDuplicate, () => order.AddItemModifier(burger, bacon));

        Assert.Equal(Given.Eur(12m), order.SubTotal);
    }

    [Fact]
    public void AddItemModifier_rejects_another_currency()
    {
        var order = Given.DraftOrder();
        var burger = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));

        DomainAssert.Throws(
            OrderErrors.CurrencyMismatch,
            () => order.AddItemModifier(burger, Given.Modifier("Bacon", Money.Of(2m, Currency.USD))));

        Assert.Empty(Assert.Single(order.Items).Modifiers);
    }

    [Fact]
    public void AddItemModifier_rejects_a_delta_that_makes_the_unit_price_negative()
    {
        var order = Given.DraftOrder();
        var burger = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));

        DomainAssert.Throws(
            OrderErrors.ItemUnitPriceNegative,
            () => order.AddItemModifier(burger, Given.Modifier("Too generous", Given.Eur(-10.01m))));
    }

    [Fact]
    public void AddItemModifier_rejects_an_unknown_line() =>
        DomainAssert.Throws(
            OrderErrors.ItemNotFound,
            () => Given.DraftOrder().AddItemModifier(OrderItemId.New(), Given.Modifier("Bacon", Given.Eur(2m))));

    [Fact]
    public void RemoveItemModifier_removes_the_option_and_recalculates()
    {
        var order = Given.DraftOrder();
        var bacon = Given.Modifier("Bacon", Given.Eur(2m));
        var burger = order.AddItem(Given.Product(Given.Eur(10m), null, "Burger", null, bacon), Given.Qty(1));

        order.RemoveItemModifier(burger, bacon.ModifierId);

        Assert.Empty(Assert.Single(order.Items).Modifiers);
        Assert.Equal(Given.Eur(12m), order.Total);
    }

    [Fact]
    public void RemoveItemModifier_rejects_an_unknown_modifier_or_line()
    {
        var order = Given.DraftOrder();
        var burger = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));

        DomainAssert.Throws(OrderErrors.ItemModifierNotFound, () => order.RemoveItemModifier(burger, ModifierId.New()));
        DomainAssert.Throws(OrderErrors.ItemNotFound, () => order.RemoveItemModifier(OrderItemId.New(), ModifierId.New()));
    }

    [Fact]
    public void RemoveItemModifier_rejects_removing_a_surcharge_that_covers_a_negative_delta()
    {
        var order = Given.DraftOrder();
        var surcharge = Given.Modifier("Double", Given.Eur(3m));
        var rebate = Given.Modifier("Offered", Given.Eur(-4m));
        var burger = order.AddItem(Given.Product(Given.Eur(2m), null, "Burger", null, surcharge, rebate), Given.Qty(1));

        DomainAssert.Throws(OrderErrors.ItemUnitPriceNegative, () => order.RemoveItemModifier(burger, surcharge.ModifierId));

        Assert.Equal(2, Assert.Single(order.Items).Modifiers.Count);
        Assert.Equal(Given.Eur(1m), order.SubTotal);
    }

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    public void Lines_and_discounts_are_frozen_once_the_order_is_placed(OrderStatus status)
    {
        var order = Given.OrderIn(status);
        var itemId = order.Items.First().Id;
        var totalBefore = order.Total;

        DomainAssert.Throws(OrderErrors.NotEditable, () => order.AddItem(Given.Product(Given.Eur(1m)), Given.Qty(1)));
        DomainAssert.Throws(OrderErrors.NotEditable, () => order.RemoveItem(itemId));
        DomainAssert.Throws(OrderErrors.NotEditable, () => order.ChangeItemQuantity(itemId, Given.Qty(5)));
        DomainAssert.Throws(OrderErrors.NotEditable, () => order.ChangeItemNote(itemId, "No onion"));
        DomainAssert.Throws(OrderErrors.NotEditable, () => order.AddItemModifier(itemId, Given.Modifier("Bacon", Given.Eur(2m))));
        DomainAssert.Throws(OrderErrors.NotEditable, () => order.RemoveItemModifier(itemId, ModifierId.New()));
        DomainAssert.Throws(OrderErrors.NotEditable, () => order.ApplyDiscount(Given.Discount(Given.Eur(1m))));
        DomainAssert.Throws(OrderErrors.NotEditable, () => order.RemoveDiscount(DiscountSource.Coupon, "WELCOME10"));
        DomainAssert.Throws(OrderErrors.NotEditable, () => order.ClearDiscounts());

        Assert.Equal(totalBefore, order.Total);
        Assert.Single(order.Items);
    }
}
