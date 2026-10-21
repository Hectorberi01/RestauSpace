using Orders.Application.Commands;
using Orders.Application.Queries;
using Orders.Domain;

namespace Orders.Application;

public sealed class OrderUseCaseTests
{
    private readonly OrdersFixture _fixture = new();

    [Fact]
    public async Task A_draft_takes_the_tenant_currency_and_price_mode_and_a_number_of_the_day()
    {
        var id = await _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway));

        var order = _fixture.Orders.Stored(id);
        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Equal(Currency.XOF, order.Currency);
        Assert.Equal(PriceTaxMode.TaxInclusive, order.PriceTaxMode);
        Assert.Equal("20260115-001", order.Number.Value);
        Assert.Equal(1, _fixture.SaveCount);
        DomainAssert.Raised<OrderCreated>(order);
    }

    [Fact]
    public async Task A_line_is_priced_for_the_location_and_the_channel_of_its_order()
    {
        var kiosk = await _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway, Origin: new OrderOrigin(OrderChannel.Kiosk, Guid.NewGuid())));

        await _fixture.AddItem.HandleAsync(new AddOrderItemCommand(kiosk, _fixture.GivenProduct("Burger", 2500m, 0.18m), 1));

        Assert.Equal((LocationId.From(_fixture.Location), OrderChannel.Kiosk), _fixture.LastSale);
    }

    [Fact]
    public async Task Lines_are_priced_from_the_catalog_in_the_order_price_mode()
    {
        var (id, _) = await _fixture.GivenDraftAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;
        var inEuros = await _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.GivenLocation(_fixture.TenantB), OrderType.Takeaway));
        await _fixture.AddItem.HandleAsync(new AddOrderItemCommand(
            inEuros, _fixture.GivenProduct("Croissant", 10m, 0.20m, _fixture.TenantB), 2, ModifierIds: [Guid.NewGuid()], Note: "  bien chaud "));

        var taxInclusive = _fixture.Orders.Stored(id);
        var taxExclusive = await _fixture.GetById.HandleAsync(new GetOrderByIdQuery(inEuros));

        // TTC : 2 x 3 500 = 7 000 dont 1 068 de taxe (7 000 - 7 000 / 1,18, arrondi au franc).
        Assert.Equal((7000m, 1068m, 7000m), (taxInclusive.SubTotal.Amount, taxInclusive.TaxTotal.Amount, taxInclusive.Total.Amount));
        // HT : 2 x (10 + 5 d'option) = 30, plus 20 % = 36.
        Assert.Equal((30m, 6m, 36m), (taxExclusive.SubTotal, taxExclusive.TaxTotal, taxExclusive.Total));
        var line = Assert.Single(taxExclusive.Items);
        Assert.Equal("bien chaud", line.Note);
        Assert.Equal(5m, Assert.Single(line.Modifiers).PriceDelta);
    }

    [Fact]
    public async Task A_line_needs_a_product_of_the_current_tenant_and_a_valid_quantity()
    {
        var (id, _) = await _fixture.GivenDraftAsync();

        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.AddItem.HandleAsync(
            new AddOrderItemCommand(id, _fixture.GivenProduct(tenantId: _fixture.TenantB), 1)));
        var zero = await Assert.ThrowsAsync<DomainException>(() => _fixture.AddItem.HandleAsync(
            new AddOrderItemCommand(id, _fixture.GivenProduct(), 0)));

        Assert.Equal(OrdersApplicationErrors.ProductNotFound, foreign.Error);
        Assert.Equal(OrderErrors.QuantityNotPositive, zero.Error);
        Assert.Single(_fixture.Orders.Stored(id).Items);
    }

    [Fact]
    public async Task A_dine_in_order_joins_an_open_session_of_its_location_only()
    {
        var open = _fixture.GivenSession();
        var closed = _fixture.GivenSession(isOpen: false);

        var id = await _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.DineIn, open));
        var notOpen = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.DineIn, closed)));
        var takeaway = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway, open)));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.DineIn, Guid.NewGuid())));

        Assert.Equal(TableSessionId.From(open), _fixture.Orders.Stored(id).TableSessionId);
        Assert.Equal(OrderErrors.TableSessionNotOpen, notOpen.Error);
        Assert.Equal(OrderErrors.TableSessionRequiresDineIn, takeaway.Error);
        Assert.Equal(OrdersApplicationErrors.TableSessionNotFound, unknown.Error);
    }

    [Fact]
    public async Task An_order_is_taken_only_in_an_operational_location_of_the_current_tenant()
    {
        var closed = _fixture.GivenLocation(isOperational: false);
        var ofB = _fixture.GivenLocation(_fixture.TenantB);

        var notOperational = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Create.HandleAsync(new CreateOrderCommand(closed, OrderType.Takeaway)));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.Create.HandleAsync(new CreateOrderCommand(ofB, OrderType.Takeaway)));

        Assert.Equal(OrderErrors.LocationNotOperational, notOperational.Error);
        Assert.Equal(OrdersApplicationErrors.LocationNotFound, foreign.Error);
        Assert.Empty(_fixture.Orders.Items);
    }

    [Fact]
    public async Task A_manual_discount_lowers_the_total_and_its_tax_and_can_be_removed()
    {
        var (id, item) = await _fixture.GivenDraftAsync();

        await _fixture.ApplyDiscount.HandleAsync(new ApplyManualDiscountCommand(id, 1000m, "geste-123", "Retard en cuisine"));
        var discounted = await _fixture.GetById.HandleAsync(new GetOrderByIdQuery(id));
        await _fixture.ChangeQuantity.HandleAsync(new ChangeOrderItemQuantityCommand(id, item, 1));
        var capped = await _fixture.GetById.HandleAsync(new GetOrderByIdQuery(id));
        await _fixture.RemoveDiscount.HandleAsync(new RemoveDiscountCommand(id, DiscountSource.Manual, "GESTE-123"));

        Assert.Equal((1000m, 6000m), (discounted.DiscountTotal, discounted.Total));
        Assert.Equal(915m, discounted.TaxTotal);
        Assert.Equal((1000m, 2500m), (capped.DiscountTotal, capped.Total));
        Assert.Equal(3500m, _fixture.Orders.Stored(id).Total.Amount);
    }

    [Fact]
    public async Task The_whole_service_ends_only_once_the_payments_cover_the_total()
    {
        var (id, _) = await _fixture.GivenDraftAsync();
        await _fixture.Place.HandleAsync(new PlaceOrderCommand(id));
        await _fixture.Accept.HandleAsync(new AcceptOrderCommand(id));
        await _fixture.StartPreparation.HandleAsync(new StartOrderPreparationCommand(id));
        await _fixture.MarkReady.HandleAsync(new MarkOrderReadyCommand(id));

        var unpaid = await Assert.ThrowsAsync<DomainException>(() => _fixture.Complete.HandleAsync(new CompleteOrderCommand(id)));
        _fixture.Paid[OrderId.From(id)] = 7000m;
        await _fixture.Complete.HandleAsync(new CompleteOrderCommand(id));

        Assert.Equal(OrderErrors.NotSettled, unpaid.Error);
        var order = _fixture.Orders.Stored(id);
        Assert.Equal(OrderStatus.Completed, order.Status);
        DomainAssert.Raised<OrderPlaced>(order);
        DomainAssert.Raised<OrderCompleted>(order);
    }

    [Fact]
    public async Task The_monthly_order_limit_of_the_plan_counts_placed_orders_of_the_month_only()
    {
        _fixture.MonthlyOrderLimit = 2;
        var (lastMonth, _) = await _fixture.GivenDraftAsync();
        await _fixture.Place.HandleAsync(new PlaceOrderCommand(lastMonth));
        _fixture.Clock.Advance(TimeSpan.FromDays(32));
        var (first, _) = await _fixture.GivenDraftAsync();
        var (second, _) = await _fixture.GivenDraftAsync();
        var (third, _) = await _fixture.GivenDraftAsync();

        await _fixture.Place.HandleAsync(new PlaceOrderCommand(first));
        await _fixture.Place.HandleAsync(new PlaceOrderCommand(second));
        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Place.HandleAsync(new PlaceOrderCommand(third)));

        Assert.Equal(OrdersApplicationErrors.MonthlyOrderLimitReached, refused.Error);
        Assert.Equal(OrderStatus.Draft, _fixture.Orders.Stored(third).Status);
        _fixture.MonthlyOrderLimit = null;
        await _fixture.Place.HandleAsync(new PlaceOrderCommand(third));
        Assert.Equal(OrderStatus.Placed, _fixture.Orders.Stored(third).Status);
    }

    [Fact]
    public async Task An_already_placed_order_reports_its_state_and_not_the_plan_limit()
    {
        var (id, _) = await _fixture.GivenDraftAsync();
        await _fixture.Place.HandleAsync(new PlaceOrderCommand(id));
        _fixture.MonthlyOrderLimit = 0;

        var again = await Assert.ThrowsAsync<DomainException>(() => _fixture.Place.HandleAsync(new PlaceOrderCommand(id)));

        Assert.NotEqual(OrdersApplicationErrors.MonthlyOrderLimitReached, again.Error);
    }

    [Fact]
    public async Task A_placed_order_is_frozen_and_can_still_be_cancelled_with_a_reason()
    {
        var (id, item) = await _fixture.GivenDraftAsync();
        await _fixture.Place.HandleAsync(new PlaceOrderCommand(id));

        var frozen = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ChangeNote.HandleAsync(new ChangeOrderItemNoteCommand(id, item, "sans piment")));
        var noReason = await Assert.ThrowsAsync<DomainException>(() => _fixture.Cancel.HandleAsync(new CancelOrderCommand(id, "  ")));
        await _fixture.Cancel.HandleAsync(new CancelOrderCommand(id, "Client parti"));

        Assert.Equal(OrderErrors.NotEditable, frozen.Error);
        Assert.Equal(OrderErrors.CancellationReasonInvalid, noReason.Error);
        var read = await _fixture.GetById.HandleAsync(new GetOrderByIdQuery(id));
        Assert.Equal((OrderStatus.Cancelled, "Client parti"), (read.Status, read.CancellationReason));
    }

    [Fact]
    public async Task A_table_session_keeps_open_orders_until_each_one_is_finished_or_cancelled()
    {
        var session = _fixture.GivenSession();
        var (first, _) = await _fixture.GivenDraftAsync(session);
        var (second, _) = await _fixture.GivenDraftAsync(session);

        Assert.True(await _fixture.HasOpenOrders.HandleAsync(new HasOpenOrdersForTableSessionQuery(session)));
        await _fixture.Cancel.HandleAsync(new CancelOrderCommand(first, "Erreur de saisie"));
        Assert.True(await _fixture.HasOpenOrders.HandleAsync(new HasOpenOrdersForTableSessionQuery(session)));
        await _fixture.Cancel.HandleAsync(new CancelOrderCommand(second, "Erreur de saisie"));
        Assert.False(await _fixture.HasOpenOrders.HandleAsync(new HasOpenOrdersForTableSessionQuery(session)));
        Assert.False(await _fixture.HasOpenOrders.HandleAsync(new HasOpenOrdersForTableSessionQuery(Guid.Empty)));
    }

    [Fact]
    public async Task Without_a_selected_restaurant_nothing_can_be_read_or_written()
    {
        var (id, _) = await _fixture.GivenDraftAsync();
        _fixture.CurrentTenantId = null;

        var read = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.GetById.HandleAsync(new GetOrderByIdQuery(id)));
        var write = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Place.HandleAsync(new PlaceOrderCommand(id)));

        Assert.Equal(TenancyErrors.TenantRequired, read.Error);
        Assert.Equal(TenancyErrors.TenantRequired, write.Error);
    }

    [Fact]
    public async Task An_order_of_another_tenant_is_not_found_and_a_suspended_restaurant_cannot_take_orders()
    {
        var (id, _) = await _fixture.GivenDraftAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;
        var read = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetById.HandleAsync(new GetOrderByIdQuery(id)));
        var place = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Place.HandleAsync(new PlaceOrderCommand(id)));
        _fixture.CurrentTenantId = _fixture.TenantA;
        _fixture.Tenants[_fixture.TenantA] = _fixture.Tenants[_fixture.TenantA] with { AcceptsBusinessWrites = false };
        var suspended = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway)));

        Assert.Equal(OrdersApplicationErrors.OrderNotFound, read.Error);
        Assert.Equal(OrdersApplicationErrors.OrderNotFound, place.Error);
        Assert.Equal(OrdersApplicationErrors.TenantNotWritable, suspended.Error);
        Assert.Equal(OrderStatus.Draft, (await _fixture.GetById.HandleAsync(new GetOrderByIdQuery(id))).Status);
    }

    [Fact]
    public async Task Orders_are_listed_newest_first_with_their_filters()
    {
        var (older, _) = await _fixture.GivenDraftAsync();
        _fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        var (newer, _) = await _fixture.GivenDraftAsync();
        await _fixture.Place.HandleAsync(new PlaceOrderCommand(newer));

        var all = await _fixture.List.HandleAsync(new ListOrdersQuery());
        var placed = await _fixture.List.HandleAsync(new ListOrdersQuery(Status: OrderStatus.Placed));
        var invalid = await Assert.ThrowsAsync<DomainException>(() => _fixture.List.HandleAsync(new ListOrdersQuery(PageSize: 0)));

        Assert.Equal(new[] { newer, older }, all.Items.Select(order => order.Id).ToArray());
        Assert.Equal(newer, Assert.Single(placed.Items).Id);
        Assert.Equal(7000m, all.Items[0].Total);
        Assert.Equal(OrdersApplicationErrors.PagingInvalid, invalid.Error);
    }

    [Fact]
    public async Task The_history_is_filtered_by_period_number_type_and_amount()
    {
        var start = _fixture.Clock.UtcNow;
        var (first, _) = await _fixture.GivenDraftAsync();
        _fixture.Clock.Advance(TimeSpan.FromHours(2));
        var takeaway = await _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway));
        var end = _fixture.Clock.UtcNow;
        var firstNumber = _fixture.Orders.Stored(first).Number.Value;

        var byType = await _fixture.List.HandleAsync(new ListOrdersQuery(Type: OrderType.Takeaway));
        var bySuffix = await _fixture.List.HandleAsync(new ListOrdersQuery(Number: "#1"));
        var byFull = await _fixture.List.HandleAsync(new ListOrdersQuery(Number: firstNumber.ToLowerInvariant()));
        var byPeriod = await _fixture.List.HandleAsync(new ListOrdersQuery(FromUtc: start, ToUtc: end));
        var byAmount = await _fixture.List.HandleAsync(new ListOrdersQuery(MinTotal: 1000m));
        var withoutDrafts = await _fixture.List.HandleAsync(new ListOrdersQuery(ExcludeDrafts: true));
        var period = await Assert.ThrowsAsync<DomainException>(() => _fixture.List.HandleAsync(new ListOrdersQuery(FromUtc: end, ToUtc: start)));
        var amounts = await Assert.ThrowsAsync<DomainException>(() => _fixture.List.HandleAsync(new ListOrdersQuery(MinTotal: 50m, MaxTotal: 10m)));

        Assert.Equal(takeaway, Assert.Single(byType.Items).Id);
        Assert.Equal(first, Assert.Single(bySuffix.Items).Id);
        Assert.Equal(first, Assert.Single(byFull.Items).Id);
        Assert.Equal(first, Assert.Single(byPeriod.Items).Id);
        Assert.Equal(first, Assert.Single(byAmount.Items).Id);
        Assert.Empty(withoutDrafts.Items);
        Assert.Equal(OrdersApplicationErrors.PeriodInvalid, period.Error);
        Assert.Equal(OrdersApplicationErrors.AmountRangeInvalid, amounts.Error);
    }

    [Fact]
    public async Task An_open_order_can_be_attached_to_a_customer_detached_but_never_to_an_erased_one()
    {
        var (id, _) = await _fixture.GivenDraftAsync();
        var customer = _fixture.GivenCustomer();
        var erased = _fixture.GivenCustomer(isAnonymized: true);
        var elsewhere = _fixture.GivenCustomer(tenantId: _fixture.TenantB);

        await _fixture.ChangeCustomer.HandleAsync(new ChangeOrderCustomerCommand(id, customer));
        var attached = _fixture.Orders.Stored(id).CustomerId?.Value;
        var anonymized = await Assert.ThrowsAsync<DomainException>(() => _fixture.ChangeCustomer.HandleAsync(new ChangeOrderCustomerCommand(id, erased)));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ChangeCustomer.HandleAsync(new ChangeOrderCustomerCommand(id, elsewhere)));
        await _fixture.ChangeCustomer.HandleAsync(new ChangeOrderCustomerCommand(id, null));
        await _fixture.Cancel.HandleAsync(new CancelOrderCommand(id, "Erreur de saisie"));
        var finalized = await Assert.ThrowsAsync<DomainException>(() => _fixture.ChangeCustomer.HandleAsync(new ChangeOrderCustomerCommand(id, customer)));

        Assert.Equal(customer, attached);
        Assert.Null(_fixture.Orders.Stored(id).CustomerId);
        Assert.Equal(OrderErrors.CustomerAnonymized, anonymized.Error);
        Assert.Equal(OrdersApplicationErrors.CustomerNotFound, unknown.Error);
        Assert.Equal(OrderErrors.Finalized, finalized.Error);
    }

    [Fact]
    public async Task A_customer_history_lists_only_their_orders()
    {
        var customer = _fixture.GivenCustomer();
        var mine = await _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway, CustomerId: customer));
        await _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway));

        var history = await _fixture.List.HandleAsync(new ListOrdersQuery(CustomerId: customer));
        var empty = await _fixture.List.HandleAsync(new ListOrdersQuery(CustomerId: Guid.Empty));

        Assert.Equal(mine, Assert.Single(history.Items).Id);
        Assert.Equal(1, history.TotalCount);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task Downstream_modules_read_the_lines_without_prices_in_their_order()
    {
        var (id, item) = await _fixture.GivenDraftAsync();
        await _fixture.AddItem.HandleAsync(new AddOrderItemCommand(id, _fixture.GivenProduct("Bissap", 500m), 1, ModifierIds: [Guid.NewGuid()], Note: "glacé"));
        var handler = new GetOrderLinesHandler(_fixture.Workspace);

        var lines = await handler.HandleAsync(new GetOrderLinesQuery(id));

        Assert.Equal(_fixture.Location, lines.LocationId);
        Assert.Equal(new[] { "Poulet DG", "Bissap" }, lines.Lines.Select(line => line.ProductName).ToArray());
        Assert.Equal(item, lines.Lines[0].OrderItemId);
        Assert.Equal(("glacé", "Option 1"), (lines.Lines[1].Note, Assert.Single(lines.Lines[1].ModifierNames)));
    }

    [Fact]
    public async Task An_order_can_be_taken_for_a_customer_of_the_restaurant_but_never_for_an_erased_one()
    {
        var customer = _fixture.GivenCustomer();
        var erased = _fixture.GivenCustomer(isAnonymized: true);
        var elsewhere = _fixture.GivenCustomer(tenantId: _fixture.TenantB);

        var id = await _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway, CustomerId: customer));
        var anonymized = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway, CustomerId: erased)));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway, CustomerId: elsewhere)));

        Assert.Equal(customer, _fixture.Orders.Stored(id).CustomerId?.Value);
        Assert.Equal(customer, (await _fixture.GetById.HandleAsync(new GetOrderByIdQuery(id))).CustomerId);
        Assert.Equal((OrderErrors.CustomerAnonymized, OrdersApplicationErrors.CustomerNotFound), (anonymized.Error, unknown.Error));
        Assert.Single(_fixture.Orders.Items);
    }
}
