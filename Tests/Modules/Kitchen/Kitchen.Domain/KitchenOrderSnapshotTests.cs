namespace Kitchen.Domain;

public class KitchenOrderSnapshotTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly LocationId Location = LocationId.New();
    private static readonly KitchenStationReference Station = KitchenRefs.Station(Tenant, Location);

    [Fact]
    public void Line_create_trims_the_name_and_keeps_the_data()
    {
        var orderItemId = OrderItemId.New();

        var line = KitchenOrderLine.Create(orderItemId, "  Burger ", 2, Station, null, [], null);

        Assert.Equal(orderItemId, line.OrderItemId);
        Assert.Equal("Burger", line.Name);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(Station, line.Station);
        Assert.Equal(line, KitchenOrderLine.Create(orderItemId, "Burger", 2, Station, null, [], null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Line_create_rejects_an_empty_name(string? name) =>
        DomainAssert.Throws(KitchenTicketErrors.LineNameEmpty, () => KitchenOrderLine.Create(OrderItemId.New(), name, 1, Station, null, [], null));

    [Fact]
    public void Line_create_rejects_a_too_long_name() =>
        DomainAssert.Throws(
            KitchenTicketErrors.LineNameTooLong,
            () => KitchenOrderLine.Create(OrderItemId.New(), new string('x', KitchenOrderLine.MaxNameLength + 1), 1, Station, null, [], null));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Line_create_rejects_a_non_positive_quantity(int quantity) =>
        DomainAssert.Throws(
            KitchenTicketErrors.LineQuantityNotPositive,
            () => KitchenOrderLine.Create(OrderItemId.New(), "Burger", quantity, Station, null, [], null));

    [Fact]
    public void Line_create_rejects_an_empty_order_item_id() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => KitchenOrderLine.Create(default, "Burger", 1, Station, null, [], null));

    // ---- Ce dont le cuisinier a besoin : variante, modificateurs, note ----

    private static KitchenOrderLine Line(string? variantName = null, IEnumerable<string>? modifiers = null, string? note = null, OrderItemId? id = null) =>
        KitchenOrderLine.Create(id ?? OrderItemId.New(), "Burger", 1, Station, variantName, modifiers ?? [], note);

    [Fact]
    public void Line_create_keeps_the_trimmed_variant_modifiers_in_order_and_note()
    {
        var line = Line("  Large ", [" No onion ", "Extra cheese"], "  Peanut allergy ");

        Assert.Equal("Large", line.VariantName);
        Assert.Equal(["No onion", "Extra cheese"], line.Modifiers);
        Assert.Equal("Peanut allergy", line.Note);
    }

    [Fact]
    public void Line_create_treats_a_blank_variant_or_note_as_none_and_accepts_no_modifier()
    {
        var line = Line("  ", [], " ");

        Assert.Null(line.VariantName);
        Assert.Empty(line.Modifiers);
        Assert.Null(line.Note);
    }

    [Fact]
    public void Line_create_accepts_exactly_the_bounds_of_an_order_line()
    {
        var line = KitchenOrderLine.Create(
            OrderItemId.New(),
            new string('n', 200),
            1,
            Station,
            new string('v', 120),
            Enumerable.Range(0, 50).Select(_ => new string('m', 120)),
            new string('t', 200));

        Assert.Equal(200, line.Name.Length);
        Assert.Equal(120, line.VariantName!.Length);
        Assert.Equal(50, line.Modifiers.Count);
        Assert.All(line.Modifiers, m => Assert.Equal(120, m.Length));
        Assert.Equal(200, line.Note!.Length);
        Assert.Equal(200, KitchenOrderLine.MaxNameLength);
        Assert.Equal(120, KitchenOrderLine.MaxVariantNameLength);
        Assert.Equal(120, KitchenOrderLine.MaxModifierNameLength);
        Assert.Equal(50, KitchenOrderLine.MaxModifiers);
        Assert.Equal(200, KitchenOrderLine.MaxNoteLength);
    }

    [Fact]
    public void Line_create_rejects_a_variant_a_modifier_or_a_note_beyond_its_bound()
    {
        DomainAssert.Throws(KitchenTicketErrors.LineVariantNameTooLong, () => Line(variantName: new string('v', 121)));
        DomainAssert.Throws(KitchenTicketErrors.LineModifierNameTooLong, () => Line(modifiers: ["Cheese", new string('m', 121)]));
        DomainAssert.Throws(KitchenTicketErrors.LineTooManyModifiers, () => Line(modifiers: Enumerable.Repeat("Cheese", 51)));
        DomainAssert.Throws(KitchenTicketErrors.LineNoteTooLong, () => Line(note: new string('t', 201)));
    }

    [Fact]
    public void Line_create_rejects_an_empty_or_null_modifier()
    {
        DomainAssert.Throws(KitchenTicketErrors.LineModifierNameEmpty, () => Line(modifiers: ["Cheese", "  "]));
        Assert.Throws<ArgumentNullException>(() => Line(modifiers: ["Cheese", null!]));
        Assert.Throws<ArgumentNullException>(() => KitchenOrderLine.Create(OrderItemId.New(), "Burger", 1, Station, null, null!, null));
    }

    [Fact]
    public void Line_copies_its_modifiers_and_does_not_expose_a_mutable_list()
    {
        var source = new List<string> { "Cheese" };
        var line = Line(modifiers: source);

        source.Add("Bacon");

        Assert.Equal(["Cheese"], line.Modifiers);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)line.Modifiers).Add("Bacon"));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)line.Modifiers)[0] = "Bacon");
    }

    [Fact]
    public void Line_equality_includes_variant_modifiers_and_note()
    {
        var id = OrderItemId.New();
        var line = Line("Large", ["Cheese", "Bacon"], "Well done", id);

        Assert.Equal(line, Line("Large", ["Cheese", "Bacon"], "Well done", id));
        Assert.Equal(line.GetHashCode(), Line("Large", ["Cheese", "Bacon"], "Well done", id).GetHashCode());
        Assert.NotEqual(line, Line("Small", ["Cheese", "Bacon"], "Well done", id));
        Assert.NotEqual(line, Line("Large", ["Bacon", "Cheese"], "Well done", id));
        Assert.NotEqual(line, Line("Large", ["Cheese"], "Well done", id));
        Assert.NotEqual(line, Line("Large", ["Cheese", "Bacon"], null, id));
    }

    [Fact]
    public void Snapshot_create_rejects_a_null_line() =>
        Assert.Throws<ArgumentNullException>(
            () => KitchenOrderSnapshot.Create(OrderId.New(), Tenant, Location, [Line(), null!]));

    [Fact]
    public void Snapshot_create_keeps_the_lines_in_order()
    {
        var orderId = OrderId.New();
        var first = KitchenOrderLine.Create(OrderItemId.New(), "Burger", 1, Station, null, [], null);
        var second = KitchenOrderLine.Create(OrderItemId.New(), "Fries", 2, Station, null, [], null);

        var snapshot = KitchenOrderSnapshot.Create(orderId, Tenant, Location, [first, second]);

        Assert.Equal(orderId, snapshot.OrderId);
        Assert.Equal(Tenant, snapshot.TenantId);
        Assert.Equal(Location, snapshot.LocationId);
        Assert.Equal([first, second], snapshot.Lines);
        Assert.Equal(snapshot, KitchenOrderSnapshot.Create(orderId, Tenant, Location, [first, second]));
    }

    [Fact]
    public void Snapshot_create_rejects_an_order_without_line() =>
        DomainAssert.Throws(
            KitchenTicketErrors.SnapshotWithoutLine,
            () => KitchenOrderSnapshot.Create(OrderId.New(), Tenant, Location, []));

    [Fact]
    public void Snapshot_create_rejects_the_same_order_item_twice()
    {
        var orderItemId = OrderItemId.New();

        DomainAssert.Throws(
            KitchenTicketErrors.SnapshotDuplicateOrderItem,
            () => KitchenOrderSnapshot.Create(
                OrderId.New(),
                Tenant,
                Location,
                [
                    KitchenOrderLine.Create(orderItemId, "Burger", 1, Station, null, [], null),
                    KitchenOrderLine.Create(orderItemId, "Burger", 1, Station, null, [], null),
                ]));
    }

    [Fact]
    public void Snapshot_create_rejects_empty_ids()
    {
        KitchenOrderLine[] lines = [KitchenOrderLine.Create(OrderItemId.New(), "Burger", 1, Station, null, [], null)];

        DomainAssert.Throws(CommonErrors.EmptyId, () => KitchenOrderSnapshot.Create(default, Tenant, Location, lines));
        DomainAssert.Throws(CommonErrors.EmptyId, () => KitchenOrderSnapshot.Create(OrderId.New(), default, Location, lines));
        DomainAssert.Throws(CommonErrors.EmptyId, () => KitchenOrderSnapshot.Create(OrderId.New(), Tenant, default, lines));
    }
}
