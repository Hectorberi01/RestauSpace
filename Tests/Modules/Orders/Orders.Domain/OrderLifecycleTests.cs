namespace Orders.Domain;

public class OrderLifecycleTests
{
    private static readonly Dictionary<string, Action<Order, IClock>> Transitions = new()
    {
        ["Place"] = (order, clock) => order.Place(clock),
        ["Accept"] = (order, clock) => order.Accept(clock),
        ["StartPreparation"] = (order, clock) => order.StartPreparation(clock),
        ["MarkReady"] = (order, clock) => order.MarkReady(clock),
        ["Complete"] = (order, clock) => order.Complete(Given.Settlement(order), clock),
    };

    [Fact]
    public void Place_rejects_an_empty_order()
    {
        var order = Given.DraftOrder();
        order.ClearDomainEvents();

        DomainAssert.Throws(OrderErrors.EmptyCannotBePlaced, () => order.Place(new FakeClock()));

        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Null(order.PlacedAtUtc);
        DomainAssert.NoEvents(order);
    }

    [Fact]
    public void Place_rejects_an_order_whose_last_line_was_removed()
    {
        var order = Given.DraftOrder();
        var line = order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));
        order.RemoveItem(line);

        DomainAssert.Throws(OrderErrors.EmptyCannotBePlaced, () => order.Place(new FakeClock()));
    }

    [Fact]
    public void Place_moves_a_draft_with_lines_to_placed_and_publishes_the_frozen_totals()
    {
        var clock = new FakeClock();
        var customer = CustomerId.New();
        var order = Given.DraftOrder(clock: clock, customerId: customer);
        order.AddItem(Given.Product(Given.Eur(10m), Given.Vat20), Given.Qty(2));
        order.ApplyDiscount(Given.Discount(Given.Eur(5m)));
        clock.Advance(TimeSpan.FromMinutes(3));

        order.Place(clock);

        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(clock.UtcNow, order.PlacedAtUtc);
        var placed = DomainAssert.Raised<OrderPlaced>(order);
        Assert.Equal(Given.Tenant, placed.TenantId);
        Assert.Equal(order.Id, placed.OrderId);
        Assert.Equal(Given.Location, placed.LocationId);
        Assert.Equal("A-0042", placed.OrderNumber);
        Assert.Equal(PriceTaxMode.TaxExclusive, placed.PriceTaxMode);
        Assert.Equal(Given.Eur(20m), placed.SubTotal);
        Assert.Equal(Given.Eur(5m), placed.DiscountTotal);
        Assert.Equal(Given.Eur(3m), placed.TaxTotal);
        Assert.Equal(Given.Eur(18m), placed.Total);
        Assert.Equal(customer, placed.CustomerId);
        Assert.Equal(clock.UtcNow, placed.OccurredAtUtc);
    }

    [Fact]
    public void Place_publishes_tax_inclusive_totals_with_their_mode()
    {
        var order = Given.InclusiveOrder();
        order.AddItem(Given.Product(Given.Eur(10m), Given.Vat20), Given.Qty(2));
        order.ApplyDiscount(Given.Discount(Given.Eur(5m)));

        order.Place(new FakeClock());

        // 15.00 payés, dont 15 - 15 / 1.2 = 2.50 de taxe.
        var placed = DomainAssert.Raised<OrderPlaced>(order);
        Assert.Equal(PriceTaxMode.TaxInclusive, placed.PriceTaxMode);
        Assert.Equal(Given.Eur(20m), placed.SubTotal);
        Assert.Equal(Given.Eur(5m), placed.DiscountTotal);
        Assert.Equal(Given.Eur(2.50m), placed.TaxTotal);
        Assert.Equal(Given.Eur(15m), placed.Total);
    }

    [Fact]
    public void Place_accepts_an_order_whose_total_is_zero()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(10m)));

        order.Place(new FakeClock());

        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(Given.Eur(0m), order.Total);
    }

    [Fact]
    public void Happy_path_goes_from_draft_to_completed_raising_one_event_per_step()
    {
        var clock = new FakeClock();
        var order = Given.DraftOrder(clock: clock);
        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));

        order.Place(clock);
        order.Accept(clock);
        Assert.Equal(OrderStatus.Accepted, order.Status);
        order.StartPreparation(clock);
        Assert.Equal(OrderStatus.Preparing, order.Status);
        order.MarkReady(clock);
        Assert.Equal(OrderStatus.Ready, order.Status);
        clock.Advance(TimeSpan.FromMinutes(20));
        order.Complete(Given.Settlement(order), clock);

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(clock.UtcNow, order.CompletedAtUtc);
        Assert.True(order.IsFinalized);
        Assert.Equal(
            [
                typeof(OrderCreated), typeof(OrderPlaced), typeof(OrderAccepted), typeof(OrderPreparationStarted),
                typeof(OrderReady), typeof(OrderCompleted),
            ],
            order.DomainEvents.Select(domainEvent => domainEvent.GetType()).ToArray());
        var completed = DomainAssert.Raised<OrderCompleted>(order);
        Assert.Equal(order.Id, completed.OrderId);
        Assert.Equal(Given.Tenant, completed.TenantId);
        Assert.Equal(Given.Location, completed.LocationId);
        Assert.Equal(PriceTaxMode.TaxExclusive, completed.PriceTaxMode);
        Assert.Equal(Given.Eur(12m), completed.Total);
        Assert.Equal(clock.UtcNow, completed.OccurredAtUtc);
    }

    [Fact]
    public void Complete_accepts_a_settlement_that_covers_exactly_the_total()
    {
        var order = Given.OrderIn(OrderStatus.Ready);

        order.Complete(Given.Settlement(order, Given.Eur(12m)), new FakeClock());

        Assert.Equal(OrderStatus.Completed, order.Status);
        DomainAssert.Raised<OrderCompleted>(order);
    }

    [Fact]
    public void Complete_accepts_an_overpayment()
    {
        var order = Given.OrderIn(OrderStatus.Ready);

        order.Complete(Given.Settlement(order, Given.Eur(15m)), new FakeClock());

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(Given.Eur(12m), DomainAssert.Raised<OrderCompleted>(order).Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11.99)]
    public void Complete_rejects_an_order_that_is_not_fully_paid(double paid)
    {
        var clock = new FakeClock();
        var order = Given.OrderIn(OrderStatus.Ready, clock);
        order.ClearDomainEvents();

        DomainAssert.Throws(OrderErrors.NotSettled, () => order.Complete(Given.Settlement(order, Given.Eur((decimal)paid)), clock));

        Assert.Equal(OrderStatus.Ready, order.Status);
        Assert.Null(order.CompletedAtUtc);
        DomainAssert.NoEvents(order);
    }

    [Fact]
    public void Complete_rejects_a_settlement_paid_in_another_currency()
    {
        var order = Given.OrderIn(OrderStatus.Ready);

        DomainAssert.Throws(OrderErrors.CurrencyMismatch, () => order.Complete(Given.Settlement(order, Given.Xof(5000m)), new FakeClock()));

        Assert.Equal(OrderStatus.Ready, order.Status);
        DomainAssert.NotRaised<OrderCompleted>(order);
    }

    [Fact]
    public void Complete_accepts_a_zero_settlement_when_the_total_is_zero()
    {
        var clock = new FakeClock();
        var order = Given.DraftOrder(clock: clock);
        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(10m)));
        order.Place(clock);
        order.Accept(clock);
        order.StartPreparation(clock);
        order.MarkReady(clock);

        order.Complete(Given.Settlement(order, Given.Eur(0m)), clock);

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(Given.Eur(0m), DomainAssert.Raised<OrderCompleted>(order).Total);
    }

    [Fact]
    public void Complete_rejects_a_settlement_of_another_tenant()
    {
        var order = Given.OrderIn(OrderStatus.Ready);
        var foreign = OrderSettlement.Create(TenantId.New(), order.Id, order.Total);

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => order.Complete(foreign, new FakeClock()));

        Assert.Equal(OrderStatus.Ready, order.Status);
        DomainAssert.NotRaised<OrderCompleted>(order);
    }

    [Fact]
    public void Complete_rejects_a_settlement_of_another_order()
    {
        var order = Given.OrderIn(OrderStatus.Ready);
        var other = OrderSettlement.Create(Given.Tenant, OrderId.New(), order.Total);

        DomainAssert.Throws(OrderErrors.SettlementOrderMismatch, () => order.Complete(other, new FakeClock()));

        Assert.Equal(OrderStatus.Ready, order.Status);
        DomainAssert.NotRaised<OrderCompleted>(order);
    }

    [Fact]
    public void Complete_checks_the_status_before_the_settlement()
    {
        var order = Given.OrderIn(OrderStatus.Preparing);
        var unpaid = Given.Settlement(order, Given.Eur(0m));

        DomainAssert.Throws(OrderErrors.InvalidTransition, () => order.Complete(unpaid, new FakeClock()));
    }

    [Fact]
    public void Complete_in_tax_inclusive_mode_requires_the_tax_inclusive_total()
    {
        var clock = new FakeClock();
        var order = Given.InclusiveOrder();
        order.AddItem(Given.Product(Given.Eur(12m), Given.Vat20), Given.Qty(1));
        order.Place(clock);
        order.Accept(clock);
        order.StartPreparation(clock);
        order.MarkReady(clock);

        // 10.00 est le montant net : le client doit les 12.00 affichés sur la carte.
        DomainAssert.Throws(OrderErrors.NotSettled, () => order.Complete(Given.Settlement(order, Given.Eur(10m)), clock));
        order.Complete(Given.Settlement(order, Given.Eur(12m)), clock);

        var completed = DomainAssert.Raised<OrderCompleted>(order);
        Assert.Equal(PriceTaxMode.TaxInclusive, completed.PriceTaxMode);
        Assert.Equal(Given.Eur(12m), completed.Total);
    }

    [Fact]
    public void Each_step_event_carries_tenant_order_and_time()
    {
        var clock = new FakeClock();
        var order = Given.OrderIn(OrderStatus.Ready, clock);

        var accepted = DomainAssert.Raised<OrderAccepted>(order);
        var started = DomainAssert.Raised<OrderPreparationStarted>(order);
        var ready = DomainAssert.Raised<OrderReady>(order);
        Assert.Equal((Given.Tenant, order.Id, Given.Location, clock.UtcNow), (accepted.TenantId, accepted.OrderId, accepted.LocationId, accepted.OccurredAtUtc));
        Assert.Equal((Given.Tenant, order.Id, Given.Location, clock.UtcNow), (started.TenantId, started.OrderId, started.LocationId, started.OccurredAtUtc));
        Assert.Equal((Given.Tenant, order.Id, Given.Location, clock.UtcNow), (ready.TenantId, ready.OrderId, ready.LocationId, ready.OccurredAtUtc));
    }

    [Theory]
    [InlineData(OrderStatus.Draft, "Accept")]
    [InlineData(OrderStatus.Draft, "StartPreparation")]
    [InlineData(OrderStatus.Draft, "MarkReady")]
    [InlineData(OrderStatus.Draft, "Complete")]
    [InlineData(OrderStatus.Placed, "Place")]
    [InlineData(OrderStatus.Placed, "StartPreparation")]
    [InlineData(OrderStatus.Placed, "MarkReady")]
    [InlineData(OrderStatus.Placed, "Complete")]
    [InlineData(OrderStatus.Accepted, "Place")]
    [InlineData(OrderStatus.Accepted, "Accept")]
    [InlineData(OrderStatus.Accepted, "MarkReady")]
    [InlineData(OrderStatus.Accepted, "Complete")]
    [InlineData(OrderStatus.Preparing, "Place")]
    [InlineData(OrderStatus.Preparing, "Accept")]
    [InlineData(OrderStatus.Preparing, "StartPreparation")]
    [InlineData(OrderStatus.Preparing, "Complete")]
    [InlineData(OrderStatus.Ready, "Place")]
    [InlineData(OrderStatus.Ready, "Accept")]
    [InlineData(OrderStatus.Ready, "StartPreparation")]
    [InlineData(OrderStatus.Ready, "MarkReady")]
    public void Transition_from_the_wrong_status_is_rejected(OrderStatus status, string transition)
    {
        var clock = new FakeClock();
        var order = Given.OrderIn(status, clock);
        order.ClearDomainEvents();

        DomainAssert.Throws(OrderErrors.InvalidTransition, () => Transitions[transition](order, clock));

        Assert.Equal(status, order.Status);
        DomainAssert.NoEvents(order);
    }

    [Theory]
    [InlineData(OrderStatus.Draft)]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    public void Cancel_is_allowed_from_every_non_terminal_status(OrderStatus status)
    {
        var clock = new FakeClock();
        var order = Given.OrderIn(status, clock);
        clock.Advance(TimeSpan.FromMinutes(1));

        order.Cancel("  Kitchen closed  ", clock);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Kitchen closed", order.CancellationReason);
        Assert.Equal(clock.UtcNow, order.CancelledAtUtc);
        Assert.True(order.IsFinalized);
        var cancelled = DomainAssert.Raised<OrderCancelled>(order);
        Assert.Equal(Given.Tenant, cancelled.TenantId);
        Assert.Equal(order.Id, cancelled.OrderId);
        Assert.Equal(status, cancelled.PreviousStatus);
        Assert.Equal("Kitchen closed", cancelled.Reason);
        Assert.Equal(Given.Location, cancelled.LocationId);
        Assert.Equal(Given.Eur(12m), cancelled.Total);
        Assert.Null(cancelled.CustomerId);
        Assert.Empty(cancelled.Discounts);
        Assert.Equal(clock.UtcNow, cancelled.OccurredAtUtc);
    }

    [Fact]
    public void Cancel_publishes_the_customer_and_the_origin_of_every_discount_to_undo()
    {
        var clock = new FakeClock();
        var customer = CustomerId.New();
        var order = Given.DraftOrder(clock: clock, customerId: customer);
        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(2m), "PROMO-LUNCH", DiscountSource.Promotion));
        // Supérieur au reste dû : plafonné par la commande, mais reste une utilisation de coupon à restituer.
        order.ApplyDiscount(Given.Discount(Given.Eur(50m), "WELCOME10", DiscountSource.Coupon));
        order.Place(clock);

        order.Cancel("Customer left", clock);

        var cancelled = DomainAssert.Raised<OrderCancelled>(order);
        Assert.Equal(customer, cancelled.CustomerId);
        Assert.Equal(
            [new DiscountOrigin(DiscountSource.Promotion, "PROMO-LUNCH"), new DiscountOrigin(DiscountSource.Coupon, "WELCOME10")],
            cancelled.Discounts);
    }

    [Fact]
    public void Cancel_does_not_publish_discounts_that_were_cleared()
    {
        var clock = new FakeClock();
        var order = Given.DraftOrder(clock: clock);
        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(2m)));
        order.ClearDiscounts();

        order.Cancel("Abandoned basket", clock);

        Assert.Empty(DomainAssert.Raised<OrderCancelled>(order).Discounts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_requires_a_reason(string? reason)
    {
        var order = Given.OrderIn(OrderStatus.Placed);

        DomainAssert.Throws(OrderErrors.CancellationReasonInvalid, () => order.Cancel(reason!, new FakeClock()));

        Assert.Equal(OrderStatus.Placed, order.Status);
        DomainAssert.NotRaised<OrderCancelled>(order);
    }

    [Fact]
    public void Cancel_rejects_a_reason_longer_than_the_maximum()
    {
        var order = Given.OrderIn(OrderStatus.Placed);

        order.Cancel(new string('r', Order.MaxCancellationReasonLength), new FakeClock());
        Assert.Equal(OrderStatus.Cancelled, order.Status);

        var other = Given.OrderIn(OrderStatus.Placed);
        DomainAssert.Throws(
            OrderErrors.CancellationReasonInvalid,
            () => other.Cancel(new string('r', Order.MaxCancellationReasonLength + 1), new FakeClock()));
    }

    [Fact]
    public void Transitions_require_a_clock()
    {
        var order = Given.OrderIn(OrderStatus.Draft);

        Assert.Throws<ArgumentNullException>(() => order.Place(null!));
        Assert.Throws<ArgumentNullException>(() => order.Cancel("reason", null!));
        Assert.Throws<ArgumentNullException>(() => order.Complete(Given.Settlement(order), null!));
        Assert.Throws<ArgumentNullException>(() => order.Complete(null!, new FakeClock()));
    }

    [Fact]
    public void Timestamps_of_a_transition_equal_the_time_of_its_event()
    {
        var clock = new TickingClock();
        var order = Given.DraftOrder(clock: clock);
        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));

        order.Place(clock);
        Assert.Equal(order.PlacedAtUtc, DomainAssert.Raised<OrderPlaced>(order).OccurredAtUtc);
        Assert.Equal(order.CreatedAtUtc, DomainAssert.Raised<OrderCreated>(order).OccurredAtUtc);

        order.Accept(clock);
        order.StartPreparation(clock);
        order.MarkReady(clock);
        order.Complete(Given.Settlement(order), clock);
        Assert.Equal(order.CompletedAtUtc, DomainAssert.Raised<OrderCompleted>(order).OccurredAtUtc);

        var cancelled = Given.DraftOrder(clock: clock);
        cancelled.Cancel("Abandoned basket", clock);
        Assert.Equal(cancelled.CancelledAtUtc, DomainAssert.Raised<OrderCancelled>(cancelled).OccurredAtUtc);
    }

    [Fact]
    public void Cancel_publishes_discounts_that_cannot_be_altered_by_a_consumer()
    {
        var order = Given.DraftOrder();
        order.ApplyDiscount(Given.Discount(Given.Eur(2m)));

        order.Cancel("Abandoned basket", new FakeClock());

        Assert.IsNotType<DiscountOrigin[]>(DomainAssert.Raised<OrderCancelled>(order).Discounts);
    }
}
