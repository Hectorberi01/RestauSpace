namespace Orders.Domain;

public sealed class OrderLineRecomposeTests
{
    [Fact]
    public void A_draft_line_is_recomposed_in_place_with_new_options_and_totals()
    {
        var order = Given.DraftOrder();
        var burger = Given.Product(Given.Eur(10m), Given.Vat10, "Burger");
        var salad = Given.Product(Given.Eur(5m), Given.Vat10, "Salade");
        var first = order.AddItem(burger, Given.Qty(1));
        order.AddItem(salad, Given.Qty(1));
        var withCheese = ProductSnapshot.Create(
            Given.Tenant, burger.ProductId, "Burger", Given.Eur(10m), Given.Vat10, [Given.Modifier("Cheese", Given.Eur(1.5m))]);

        var replaced = order.ReplaceItem(first, withCheese, Given.Qty(2), "bien cuit");

        Assert.NotEqual(first, replaced);
        var line = order.Items.First();
        Assert.Equal((replaced, 2, "bien cuit"), (line.Id, line.Quantity.Value, line.Note));
        Assert.Equal(Given.Eur(23m), line.LineTotal);
        Assert.Equal(Given.Eur(28m), order.SubTotal);
        Assert.Equal("Salade", order.Items.Last().ProductName);
    }

    [Fact]
    public void Only_the_same_product_and_only_in_draft()
    {
        var order = Given.DraftOrder();
        var burger = Given.Product(Given.Eur(10m));
        var line = order.AddItem(burger, Given.Qty(1));

        var other = Assert.Throws<DomainException>(() => order.ReplaceItem(line, Given.Product(Given.Eur(3m)), Given.Qty(1)));
        Assert.Equal(OrderErrors.ItemProductMismatch, other.Error);

        order.Place(new FakeClock());
        var placed = Assert.Throws<DomainException>(() => order.ReplaceItem(line, burger, Given.Qty(2)));
        Assert.Equal(OrderErrors.NotEditable, placed.Error);
    }

    [Fact]
    public void A_dine_in_order_moves_to_another_open_table_of_its_location()
    {
        var clock = new FakeClock();
        var first = TableSessionId.New();
        var order = Given.DraftOrder(type: OrderType.DineIn, tableSessionId: first);
        order.ClearDomainEvents();
        var next = Given.OpenSession();

        order.MoveToTableSession(next, clock);
        order.MoveToTableSession(next, clock);

        Assert.Equal(next.TableSessionId, order.TableSessionId);
        var moved = Assert.Single(order.DomainEvents.OfType<OrderTableSessionChanged>());
        Assert.Equal((first, next.TableSessionId), (moved.PreviousTableSessionId!.Value, moved.TableSessionId));
        Assert.Equal(OrderErrors.TableSessionNotOpen, Assert.Throws<DomainException>(() => order.MoveToTableSession(Given.OpenSession(open: false), clock)).Error);
        Assert.Equal(
            OrderErrors.TableSessionLocationMismatch,
            Assert.Throws<DomainException>(() => order.MoveToTableSession(Given.OpenSession(location: LocationId.New()), clock)).Error);
        Assert.Equal(
            OrderErrors.TableSessionRequiresDineIn,
            Assert.Throws<DomainException>(() => Given.DraftOrder().MoveToTableSession(next, clock)).Error);
    }
}
