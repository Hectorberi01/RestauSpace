namespace Orders.Domain;

public class OrderCreationTests
{
    [Fact]
    public void Create_starts_an_empty_draft_with_zero_totals_in_its_currency()
    {
        var clock = new FakeClock();
        var customer = CustomerId.New();

        var order = Given.DraftOrder(Currency.XOF, OrderType.Delivery, clock, customerId: customer);

        Assert.Equal(Given.Tenant, order.TenantId);
        Assert.Equal(Given.Location, order.LocationId);
        Assert.Equal("A-0042", order.Number.Value);
        Assert.Equal(OrderType.Delivery, order.Type);
        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Equal(Currency.XOF, order.Currency);
        Assert.Equal(PriceTaxMode.TaxExclusive, order.PriceTaxMode);
        Assert.Equal(Given.Xof(0m), order.NetTotal);
        Assert.Equal(customer, order.CustomerId);
        Assert.Null(order.TableSessionId);
        Assert.Empty(order.Items);
        Assert.Empty(order.Discounts);
        Assert.Equal(Given.Xof(0m), order.SubTotal);
        Assert.Equal(Given.Xof(0m), order.DiscountTotal);
        Assert.Equal(Given.Xof(0m), order.TaxTotal);
        Assert.Equal(Given.Xof(0m), order.Total);
        Assert.Equal(clock.UtcNow, order.CreatedAtUtc);
        Assert.False(order.IsFinalized);
    }

    [Fact]
    public void Create_raises_OrderCreated()
    {
        var clock = new FakeClock();
        var session = TableSessionId.New();

        var order = Given.DraftOrder(type: OrderType.DineIn, clock: clock, tableSessionId: session);

        var created = DomainAssert.Raised<OrderCreated>(order);
        Assert.Equal(Given.Tenant, created.TenantId);
        Assert.Equal(order.Id, created.OrderId);
        Assert.Equal(Given.Location, created.LocationId);
        Assert.Equal("A-0042", created.OrderNumber);
        Assert.Equal(OrderType.DineIn, created.OrderType);
        Assert.Equal("EUR", created.CurrencyCode);
        Assert.Equal(PriceTaxMode.TaxExclusive, created.PriceTaxMode);
        Assert.Equal(session, created.TableSessionId);
        Assert.Equal(clock.UtcNow, created.OccurredAtUtc);
        Assert.Single(order.DomainEvents);
    }

    [Theory]
    [InlineData(PriceTaxMode.TaxInclusive)]
    [InlineData(PriceTaxMode.TaxExclusive)]
    public void Create_fixes_the_price_tax_mode_of_the_order(PriceTaxMode mode)
    {
        var order = Given.DraftOrder(mode: mode);

        Assert.Equal(mode, order.PriceTaxMode);
        Assert.Equal(mode, DomainAssert.Raised<OrderCreated>(order).PriceTaxMode);
    }

    [Fact]
    public void Create_rejects_an_unknown_price_tax_mode() =>
        DomainAssert.Throws(OrderErrors.PriceTaxModeUnknown, () => Given.DraftOrder(mode: (PriceTaxMode)0));

    [Fact]
    public void Several_orders_can_share_the_same_table_session()
    {
        var session = TableSessionId.New();

        var firstRound = Given.DraftOrder(type: OrderType.DineIn, tableSessionId: session);
        var secondRound = Given.DraftOrder(type: OrderType.DineIn, tableSessionId: session);

        Assert.NotEqual(firstRound.Id, secondRound.Id);
        Assert.Equal(session, firstRound.TableSessionId);
        Assert.Equal(session, secondRound.TableSessionId);
    }

    [Fact]
    public void Create_gives_each_order_its_own_id() =>
        Assert.NotEqual(Given.DraftOrder().Id, Given.DraftOrder().Id);

    [Fact]
    public void Create_accepts_a_dine_in_order_with_or_without_a_table_session()
    {
        var session = TableSessionId.New();

        Assert.Equal(session, Given.DraftOrder(type: OrderType.DineIn, tableSessionId: session).TableSessionId);
        Assert.Null(Given.DraftOrder(type: OrderType.DineIn).TableSessionId);
    }

    [Theory]
    [InlineData(OrderType.Takeaway)]
    [InlineData(OrderType.Delivery)]
    public void Create_rejects_a_table_session_on_a_non_dine_in_order(OrderType type) =>
        DomainAssert.Throws(
            OrderErrors.TableSessionRequiresDineIn,
            () => Given.DraftOrder(type: type, tableSessionId: TableSessionId.New()));

    [Fact]
    public void Create_rejects_an_unknown_order_type() =>
        DomainAssert.Throws(OrderErrors.TypeUnknown, () => Given.DraftOrder(type: (OrderType)99));

    [Fact]
    public void Create_requires_location_number_currency_and_clock()
    {
        var number = OrderNumber.Create("A-1");
        Assert.Throws<ArgumentNullException>(
            () => Order.Create(Given.Tenant, null!, number, OrderType.Takeaway, Currency.EUR, PriceTaxMode.TaxInclusive, new FakeClock()));
        Assert.Throws<ArgumentNullException>(
            () => Order.Create(Given.Tenant, Given.LocationRef, null!, OrderType.Takeaway, Currency.EUR, PriceTaxMode.TaxInclusive, new FakeClock()));
        Assert.Throws<ArgumentNullException>(
            () => Order.Create(Given.Tenant, Given.LocationRef, number, OrderType.Takeaway, null!, PriceTaxMode.TaxInclusive, new FakeClock()));
        Assert.Throws<ArgumentNullException>(
            () => Order.Create(Given.Tenant, Given.LocationRef, number, OrderType.Takeaway, Currency.EUR, PriceTaxMode.TaxInclusive, null!));
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_or_an_empty_foreign_id()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => Given.Create(tenant: default(TenantId)));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(Given.Tenant, default, true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(default, Given.Location, true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new CustomerReference(Given.Tenant, default, false));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new CustomerReference(default, CustomerId.New(), false));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new TableSessionReference(Given.Tenant, default, Given.Location, true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new TableSessionReference(Given.Tenant, TableSessionId.New(), default, true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new TableSessionReference(default, TableSessionId.New(), Given.Location, true));
    }

    [Fact]
    public void Create_keeps_only_the_ids_of_its_references()
    {
        var customer = Given.Customer();
        var session = Given.OpenSession();

        var order = Given.Create(customer: customer, tableSession: session);

        Assert.Equal(Given.Location, order.LocationId);
        Assert.Equal(customer.CustomerId, order.CustomerId);
        Assert.Equal(session.TableSessionId, order.TableSessionId);
        var created = DomainAssert.Raised<OrderCreated>(order);
        Assert.Equal((Given.Location, customer.CustomerId, session.TableSessionId), (created.LocationId, created.CustomerId!.Value, created.TableSessionId!.Value));
    }

    [Fact]
    public void Create_rejects_a_location_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Given.Create(location: new LocationReference(TenantId.New(), Given.Location, true)));

    [Fact]
    public void Create_rejects_a_location_that_is_not_operational() =>
        DomainAssert.Throws(
            OrderErrors.LocationNotOperational,
            () => Given.Create(location: new LocationReference(Given.Tenant, Given.Location, IsOperational: false)));

    [Fact]
    public void Create_rejects_a_customer_of_another_tenant() =>
        DomainAssert.Throws(CommonErrors.TenantMismatch, () => Given.Create(customer: Given.Customer(tenant: TenantId.New())));

    [Fact]
    public void Create_rejects_an_anonymized_customer() =>
        DomainAssert.Throws(OrderErrors.CustomerAnonymized, () => Given.Create(customer: Given.Customer(anonymized: true)));

    [Fact]
    public void Create_rejects_a_table_session_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Given.Create(tableSession: Given.OpenSession(tenant: TenantId.New())));

    [Fact]
    public void Create_rejects_a_table_session_of_another_location() =>
        DomainAssert.Throws(
            OrderErrors.TableSessionLocationMismatch,
            () => Given.Create(tableSession: Given.OpenSession(location: LocationId.New())));

    [Fact]
    public void Create_rejects_a_table_session_that_is_closed() =>
        DomainAssert.Throws(OrderErrors.TableSessionNotOpen, () => Given.Create(tableSession: Given.OpenSession(open: false)));
}
