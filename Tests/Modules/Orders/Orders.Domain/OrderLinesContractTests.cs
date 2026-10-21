namespace Orders.Domain;

/// <summary>Variante, note cuisine et description de ligne transmises à la cuisine et au stock.</summary>
public class OrderLinesContractTests
{
    [Fact]
    public void AddItem_keeps_the_variant_that_was_sold()
    {
        var order = Given.DraftOrder();
        var large = VariantId.New();

        order.AddItem(Given.VariantProduct(Given.Eur(14m), large, "Large"), Given.Qty(1));

        var item = Assert.Single(order.Items);
        Assert.Equal(large, item.VariantId);
        Assert.Equal("Large", item.VariantName);
        // Le prix figé inclut déjà la variante : rien n'est ajouté par-dessus.
        Assert.Equal(Given.Eur(14m), item.UnitPriceWithModifiers);
    }

    [Fact]
    public void AddItem_of_a_product_without_variant_leaves_the_variant_empty()
    {
        var order = Given.DraftOrder();

        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));

        var item = Assert.Single(order.Items);
        Assert.Null(item.VariantId);
        Assert.Null(item.VariantName);
        Assert.Null(item.Note);
    }

    [Fact]
    public void AddItem_keeps_the_trimmed_kitchen_note()
    {
        var order = Given.DraftOrder();

        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1), "  No onion ");
        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1), "   ");

        Assert.Equal("No onion", order.Items.First().Note);
        Assert.Null(order.Items.Last().Note);
    }

    [Fact]
    public void AddItem_accepts_a_note_of_the_maximum_length_and_rejects_one_more()
    {
        var order = Given.DraftOrder();

        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1), new string('n', OrderItem.MaxNoteLength));
        DomainAssert.Throws(
            OrderErrors.ItemNoteTooLong,
            () => order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1), new string('n', OrderItem.MaxNoteLength + 1)));

        Assert.Equal(OrderItem.MaxNoteLength, Assert.Single(order.Items).Note!.Length);
    }

    [Fact]
    public void ChangeItemNote_sets_replaces_and_removes_the_note_without_touching_the_totals()
    {
        var order = Given.DraftOrder();
        var line = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));
        order.ClearDomainEvents();

        order.ChangeItemNote(line, " Well done ");
        Assert.Equal("Well done", Assert.Single(order.Items).Note);

        order.ChangeItemNote(line, "Rare");
        Assert.Equal("Rare", Assert.Single(order.Items).Note);

        order.ChangeItemNote(line, " ");
        Assert.Null(Assert.Single(order.Items).Note);

        Assert.Equal(Given.Eur(12m), order.Total);
        DomainAssert.NoEvents(order);
    }

    [Fact]
    public void ChangeItemNote_to_the_same_note_changes_nothing()
    {
        var order = Given.DraftOrder();
        var line = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1), "No onion");

        order.ChangeItemNote(line, "  No onion  ");
        Assert.Equal("No onion", Assert.Single(order.Items).Note);

        var bare = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));
        order.ChangeItemNote(bare, null);
        Assert.Null(order.Items.Last().Note);
    }

    [Fact]
    public void ChangeItemNote_rejects_a_too_long_note_or_an_unknown_line()
    {
        var order = Given.DraftOrder();
        var line = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1), "No onion");

        DomainAssert.Throws(OrderErrors.ItemNoteTooLong, () => order.ChangeItemNote(line, new string('n', OrderItem.MaxNoteLength + 1)));
        DomainAssert.Throws(OrderErrors.ItemNotFound, () => order.ChangeItemNote(OrderItemId.New(), "Rare"));

        Assert.Equal("No onion", Assert.Single(order.Items).Note);
    }

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    public void ChangeItemNote_is_only_allowed_on_a_draft(OrderStatus status)
    {
        var order = Given.OrderIn(status);

        DomainAssert.Throws(OrderErrors.NotEditable, () => order.ChangeItemNote(order.Items.First().Id, "Too late"));

        Assert.Null(Assert.Single(order.Items).Note);
    }

    [Fact]
    public void DescribeLines_gives_what_the_kitchen_and_the_stock_need_for_every_line()
    {
        var order = Given.DraftOrder();
        var large = VariantId.New();
        var cheese = Given.Modifier("Cheese", Given.Eur(1m));
        var noBun = Given.Modifier("No bun", Given.Eur(0m));
        var pizza = Given.VariantProduct(Given.Eur(14m), large, "Large");
        var burger = Given.Product(Given.Eur(10m), null, "Burger", null, cheese, noBun);
        var pizzaLine = order.AddItem(pizza, Given.Qty(2));
        var burgerLine = order.AddItem(burger, Given.Qty(3), "Well done");

        var lines = order.DescribeLines();

        Assert.Equal(2, lines.Count);
        Assert.Equal(
            (pizzaLine, pizza.ProductId, (VariantId?)large, "Pizza", "Large", 2, (string?)null),
            (lines[0].OrderItemId, lines[0].ProductId, lines[0].VariantId, lines[0].ProductName, lines[0].VariantName, lines[0].Quantity, lines[0].Note));
        Assert.Empty(lines[0].Modifiers);
        Assert.Equal(
            (burgerLine, burger.ProductId, (VariantId?)null, "Burger", (string?)null, 3, "Well done"),
            (lines[1].OrderItemId, lines[1].ProductId, lines[1].VariantId, lines[1].ProductName, lines[1].VariantName, lines[1].Quantity, lines[1].Note));
        Assert.Equal(
            [(cheese.ModifierId, "Cheese"), (noBun.ModifierId, "No bun")],
            lines[1].Modifiers.Select(modifier => (modifier.ModifierId, modifier.Name)).ToArray());
    }

    [Theory]
    [InlineData(OrderStatus.Draft)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void DescribeLines_is_a_pure_read_available_in_every_status(OrderStatus status)
    {
        var order = Given.OrderIn(status);
        order.ClearDomainEvents();

        var line = Assert.Single(order.DescribeLines());

        Assert.Equal((order.Items.First().Id, "Burger", 1), (line.OrderItemId, line.ProductName, line.Quantity));
        Assert.Equal(status, order.Status);
        DomainAssert.NoEvents(order);
    }

    [Fact]
    public void DescribeLines_returns_a_detached_immutable_description()
    {
        var order = Given.DraftOrder();
        var line = order.AddItem(Given.Product(Given.Eur(10m), null, "Burger", null, Given.Modifier("Cheese", Given.Eur(1m))), Given.Qty(1));

        var before = order.DescribeLines();
        order.ChangeItemQuantity(line, Given.Qty(5));
        order.ChangeItemNote(line, "Rare");
        order.AddItem(Given.Product(Given.Eur(1m)), Given.Qty(1));

        // Ce qui a été transmis ne bouge pas avec la commande, et ne peut pas être modifié par un downcast.
        Assert.Equal((1, (string?)null), (Assert.Single(before).Quantity, before[0].Note));
        Assert.IsNotType<OrderLineView[]>(before);
        Assert.IsNotType<List<OrderLineView>>(before);
        Assert.Throws<NotSupportedException>(() => ((IList<OrderLineView>)before).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<OrderLineModifierView>)before[0].Modifiers).Clear());
        Assert.Equal(2, order.DescribeLines().Count);
    }

    [Fact]
    public void DescribeLines_never_exceeds_the_bounds_promised_to_the_kitchen()
    {
        var order = Given.DraftOrder();
        var modifiers = Enumerable.Range(0, OrderItem.MaxModifiers)
            .Select(i => OrderItemModifier.Create(ModifierId.New(), new string((char)('a' + (i % 26)), OrderItemModifier.MaxNameLength), Given.Eur(0m)))
            .ToArray();
        var product = ProductSnapshot.Create(
            Given.Tenant, ProductId.New(), new string('p', ProductSnapshot.MaxNameLength), Given.Eur(10m), Given.Vat20, modifiers,
            VariantId.New(), new string('v', ProductSnapshot.MaxVariantNameLength));

        order.AddItem(product, Given.Qty(Quantity.MaxValue), new string('n', OrderItem.MaxNoteLength));

        var line = Assert.Single(order.DescribeLines());
        Assert.Equal((200, 120, 200, 50), (line.ProductName.Length, line.VariantName!.Length, line.Note!.Length, line.Modifiers.Count));
        Assert.All(line.Modifiers, modifier => Assert.Equal(120, modifier.Name.Length));
        // Le contrat est exprimé en constantes : en changer une doit être une décision délibérée et inter-modules.
        Assert.Equal(
            (200, 120, 120, 200, 50),
            (ProductSnapshot.MaxNameLength, ProductSnapshot.MaxVariantNameLength, OrderItemModifier.MaxNameLength, OrderItem.MaxNoteLength, OrderItem.MaxModifiers));
    }

    [Fact]
    public void Line_views_can_only_be_obtained_from_an_order()
    {
        Assert.Empty(typeof(OrderLineView).GetConstructors());
        Assert.Empty(typeof(OrderLineModifierView).GetConstructors());
    }
}
