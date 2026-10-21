namespace Orders.Domain;

public class OrderOriginTests
{
    private static Order Create(FakeClock clock, OrderOrigin? origin = null, DateTimeOffset? scheduledFor = null) =>
        Order.Create(
            Given.Tenant, Given.LocationRef, OrderNumber.Create("W-0001"), OrderType.Takeaway, Currency.XOF, PriceTaxMode.TaxInclusive, clock,
            origin: origin, scheduledForUtc: scheduledFor);

    [Fact]
    public void An_order_taken_at_the_counter_by_default_is_due_as_soon_as_possible()
    {
        var order = Create(new FakeClock());

        Assert.Equal(OrderChannel.Counter, order.Channel);
        Assert.Null(order.SourceAppId);
        Assert.Null(order.ScheduledForUtc);
        Assert.Equal(OrderChannel.Counter, DomainAssert.Raised<OrderCreated>(order).Channel);
    }

    [Fact]
    public void An_online_order_keeps_its_channel_its_application_and_its_requested_time()
    {
        var clock = new FakeClock();
        var app = Guid.NewGuid();
        var pickup = clock.UtcNow.AddMinutes(45);

        var order = Create(clock, new OrderOrigin(OrderChannel.Web, app), pickup);

        Assert.Equal((OrderChannel.Web, (Guid?)app, (DateTimeOffset?)pickup), (order.Channel, order.SourceAppId, order.ScheduledForUtc));
        var created = DomainAssert.Raised<OrderCreated>(order);
        Assert.Equal((OrderChannel.Web, (DateTimeOffset?)pickup), (created.Channel, created.ScheduledForUtc));
    }

    [Fact]
    public void A_requested_time_is_in_the_future_and_within_two_weeks()
    {
        var clock = new FakeClock();

        DomainAssert.Throws(OrderErrors.ScheduleInPast, () => Create(clock, scheduledFor: clock.UtcNow));
        DomainAssert.Throws(OrderErrors.ScheduleTooFar, () => Create(clock, scheduledFor: clock.UtcNow + Order.MaxScheduleAhead + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Only_an_online_channel_names_its_application()
    {
        var clock = new FakeClock();

        DomainAssert.Throws(OrderErrors.SourceAppInvalid, () => Create(clock, new OrderOrigin(OrderChannel.Counter, Guid.NewGuid())));
        DomainAssert.Throws(OrderErrors.SourceAppInvalid, () => Create(clock, new OrderOrigin(OrderChannel.Kiosk, Guid.Empty)));
        DomainAssert.Throws(OrderErrors.ChannelUnknown, () => Create(clock, new OrderOrigin((OrderChannel)99)));
    }

    [Fact]
    public void A_delivery_platform_order_keeps_its_platform_and_its_number_there()
    {
        var order = Create(new FakeClock(), OrderOrigin.FromPlatform(" Glovo ", " GL-2041 "));

        Assert.Equal((OrderChannel.DeliveryPlatform, "glovo", "GL-2041"), (order.Channel, order.Platform, order.ExternalReference));
        Assert.Null(order.SourceAppId);
    }

    [Fact]
    public void A_platform_number_is_optional()
    {
        var order = Create(new FakeClock(), OrderOrigin.FromPlatform("gozem", " "));
        Assert.Null(order.ExternalReference);
    }

    [Fact]
    public void Only_a_delivery_platform_order_names_a_platform_and_its_code_is_checked()
    {
        var clock = new FakeClock();

        DomainAssert.Throws(OrderErrors.PlatformInvalid, () => Create(clock, new OrderOrigin(OrderChannel.DeliveryPlatform)));
        DomainAssert.Throws(OrderErrors.PlatformInvalid, () => Create(clock, new OrderOrigin(OrderChannel.Phone, Platform: "glovo")));
        DomainAssert.Throws(OrderErrors.PlatformInvalid, () => Create(clock, OrderOrigin.FromPlatform("g", null)));
        DomainAssert.Throws(OrderErrors.PlatformInvalid, () => Create(clock, OrderOrigin.FromPlatform("glo vo", null)));
        DomainAssert.Throws(OrderErrors.ExternalReferenceInvalid, () => Create(clock, OrderOrigin.FromPlatform("glovo", new string('x', 65))));
        DomainAssert.Throws(OrderErrors.ExternalReferenceInvalid, () => Create(clock, OrderOrigin.FromPlatform("glovo", "a\nb")));
        DomainAssert.Throws(OrderErrors.ExternalReferenceInvalid, () => Create(clock, new OrderOrigin(OrderChannel.Web, ExternalReference: "x")));
    }
}
