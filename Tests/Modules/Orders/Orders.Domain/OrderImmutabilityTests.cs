namespace Orders.Domain;

public class OrderImmutabilityTests
{
    /// <summary>Méthodes publiques d'Order sans effet ; tout le reste doit figurer dans <see cref="Mutators"/>.</summary>
    private static readonly string[] PureReads = ["DescribeLines"];

    private static readonly Dictionary<string, Action<Order, IClock>> Mutators = new()
    {
        ["AddItem"] = (order, _) => order.AddItem(Given.Product(Given.Eur(1m)), Given.Qty(1)),
        ["RemoveItem"] = (order, _) => order.RemoveItem(order.Items.First().Id),
        ["ChangeItemQuantity"] = (order, _) => order.ChangeItemQuantity(order.Items.First().Id, Given.Qty(9)),
        ["ChangeItemNote"] = (order, _) => order.ChangeItemNote(order.Items.First().Id, "No onion"),
        ["AddItemModifier"] = (order, _) => order.AddItemModifier(order.Items.First().Id, Given.Modifier("Bacon", Given.Eur(2m))),
        ["RemoveItemModifier"] = (order, _) => order.RemoveItemModifier(order.Items.First().Id, ModifierId.New()),
        ["ApplyDiscount"] = (order, _) => order.ApplyDiscount(Given.Discount(Given.Eur(1m))),
        ["RemoveDiscount"] = (order, _) => order.RemoveDiscount(DiscountSource.Coupon, "WELCOME10"),
        ["Place"] = (order, clock) => order.Place(clock),
        ["Accept"] = (order, clock) => order.Accept(clock),
        ["StartPreparation"] = (order, clock) => order.StartPreparation(clock),
        ["MarkReady"] = (order, clock) => order.MarkReady(clock),
        ["ClearDiscounts"] = (order, _) => order.ClearDiscounts(),
        ["Complete"] = (order, clock) => order.Complete(Given.Settlement(order), clock),
        ["Cancel"] = (order, clock) => order.Cancel("Too late", clock),
        ["ApplyDeliveryFee"] = (order, clock) => order.ApplyDeliveryFee(Given.Eur(2m), "DLV-1", clock),
        ["RemoveDeliveryFee"] = (order, clock) => order.RemoveDeliveryFee("DLV-1", clock),
        ["ChangeCustomer"] = (order, clock) => order.ChangeCustomer(null, clock),
        ["ReplaceItem"] = (order, _) => order.ReplaceItem(order.Items.First().Id, Given.Product(Given.Eur(1m)), Given.Qty(1)),
        ["MoveToTableSession"] = (order, clock) => order.MoveToTableSession(Given.OpenSession(), clock),
    };

    public static TheoryData<OrderStatus, string> EveryMutatorOnEveryTerminalStatus()
    {
        var data = new TheoryData<OrderStatus, string>();
        foreach (var status in new[] { OrderStatus.Completed, OrderStatus.Cancelled })
        {
            foreach (var mutator in Mutators.Keys)
            {
                data.Add(status, mutator);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryMutatorOnEveryTerminalStatus))]
    public void Finalized_order_rejects_every_mutation_and_stays_untouched(OrderStatus status, string mutator)
    {
        var clock = new FakeClock();
        var order = Given.OrderIn(status, clock);
        order.ClearDomainEvents();
        var item = Assert.Single(order.Items);
        var completedAt = order.CompletedAtUtc;
        var cancelledAt = order.CancelledAtUtc;
        var reason = order.CancellationReason;
        clock.Advance(TimeSpan.FromHours(1));

        DomainAssert.Throws(OrderErrors.Finalized, () => Mutators[mutator](order, clock));

        Assert.Equal(status, order.Status);
        Assert.Same(item, Assert.Single(order.Items));
        Assert.Equal(1, item.Quantity.Value);
        Assert.Empty(item.Modifiers);
        Assert.Null(item.Note);
        Assert.Empty(order.Discounts);
        Assert.Equal(Given.Eur(10m), order.SubTotal);
        Assert.Equal(Given.Eur(0m), order.DiscountTotal);
        Assert.Equal(Given.Eur(2m), order.TaxTotal);
        Assert.Equal(Given.Eur(12m), order.Total);
        Assert.Equal(completedAt, order.CompletedAtUtc);
        Assert.Equal(cancelledAt, order.CancelledAtUtc);
        Assert.Equal(reason, order.CancellationReason);
        DomainAssert.NoEvents(order);
    }

    [Fact]
    public void Every_public_mutator_of_Order_is_covered_by_the_immutability_matrix()
    {
        var publicMutators = typeof(Order)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Except(PureReads)
            .Order()
            .ToArray();

        Assert.Equal(Mutators.Keys.Order().ToArray(), publicMutators);
    }

    [Fact]
    public void Order_and_its_lines_expose_no_public_setter()
    {
        var setters = new[] { typeof(Order), typeof(OrderItem) }
            .SelectMany(type => type.GetProperties())
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}");

        Assert.Empty(setters);
    }
}
