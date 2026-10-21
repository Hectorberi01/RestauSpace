namespace Delivery.Domain;

public class FulfillmentTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly LocationReference Place = new(Tenant, LocationId.New(), IsOperational: true);
    private static readonly PhoneNumber Phone = PhoneNumber.Create("+22990000001");

    private readonly FakeClock _clock = new();
    private readonly DeliverySettings _settings;
    private readonly DeliveryZoneId _zone;

    public FulfillmentTests()
    {
        _settings = DeliverySettings.Create(Tenant, Place, Currency.XOF, _clock);
        _zone = _settings.AddZone("Centre", Xof(1000), Xof(5000), 25, _clock);
        _settings.ConfigureDelivery(true, 30, _clock);
    }

    private static Money Xof(decimal amount) => Money.Of(amount, Currency.XOF);

    private static OrderReference Order(OrderKind kind, decimal total = 10000, bool isOpen = true, TenantId? tenant = null) =>
        new(tenant ?? Tenant, OrderId.New(), Place.LocationId, kind, Xof(total), isOpen);

    private Fulfillment NewPickup()
    {
        var fulfillment = Fulfillment.ForPickup(Tenant, Order(OrderKind.Takeaway), _settings, "Ada", null, null, _clock);
        fulfillment.ClearDomainEvents();
        return fulfillment;
    }

    private Fulfillment NewDelivery()
    {
        var fulfillment = Fulfillment.ForDelivery(
            Tenant, Order(OrderKind.Delivery), _settings, _zone, "Ada", Phone, "Rue 12, Cadjehoun", "Près de la pharmacie", null, _clock);
        fulfillment.ClearDomainEvents();
        return fulfillment;
    }

    // ---- Retrait

    [Fact]
    public void A_pickup_is_promised_after_the_pickup_lead_time()
    {
        var order = Order(OrderKind.Takeaway);

        var fulfillment = Fulfillment.ForPickup(Tenant, order, _settings, "  Ada  ", null, null, _clock);

        Assert.Equal((FulfillmentMode.Pickup, FulfillmentStatus.Pending, "Ada"), (fulfillment.Mode, fulfillment.Status, fulfillment.CustomerName));
        Assert.Equal(_clock.UtcNow.AddMinutes(DeliverySettings.DefaultPickupLeadMinutes), fulfillment.PromisedAtUtc);
        Assert.Equal(order.OrderId, DomainAssert.Raised<FulfillmentCreated>(fulfillment).OrderId);
    }

    [Fact]
    public void A_requested_delivery_time_is_promised_unless_it_is_earlier_than_the_lead_and_trip()
    {
        var later = _clock.UtcNow.AddHours(3);

        var scheduled = Fulfillment.ForDelivery(
            Tenant, Order(OrderKind.Delivery), _settings, _zone, "Ada", Phone, "Rue 12", null, null, _clock, later);
        var tooEarly = Fulfillment.ForDelivery(
            Tenant, Order(OrderKind.Delivery), _settings, _zone, "Ada", Phone, "Rue 12", null, null, _clock, _clock.UtcNow.AddMinutes(10));

        Assert.Equal(later, scheduled.PromisedAtUtc);
        Assert.Equal(_clock.UtcNow.AddMinutes(30 + 25), tooEarly.PromisedAtUtc);
        DomainAssert.Throws(FulfillmentErrors.DeliveryTimeInvalid, () => Fulfillment.ForDelivery(
            Tenant, Order(OrderKind.Delivery), _settings, _zone, "Ada", Phone, "Rue 12", null, null, _clock, _clock.UtcNow.AddDays(8)));
    }

    [Fact]
    public void A_later_pickup_time_is_kept_and_a_far_one_is_refused()
    {
        var later = _clock.UtcNow.AddHours(3);

        var fulfillment = Fulfillment.ForPickup(Tenant, Order(OrderKind.Takeaway), _settings, "Ada", null, later, _clock);

        Assert.Equal(later, fulfillment.PromisedAtUtc);
        DomainAssert.Throws(FulfillmentErrors.PickupTimeInvalid, () => Fulfillment.ForPickup(
            Tenant, Order(OrderKind.Takeaway), _settings, "Ada", null, _clock.UtcNow.AddDays(8), _clock));
    }

    [Fact]
    public void A_pickup_needs_an_open_takeaway_order_of_the_location()
    {
        DomainAssert.Throws(FulfillmentErrors.OrderKindMismatch, () => Fulfillment.ForPickup(
            Tenant, Order(OrderKind.Delivery), _settings, "Ada", null, null, _clock));
        DomainAssert.Throws(FulfillmentErrors.OrderNotOpen, () => Fulfillment.ForPickup(
            Tenant, Order(OrderKind.Takeaway, isOpen: false), _settings, "Ada", null, null, _clock));
        DomainAssert.Throws(FulfillmentErrors.OrderLocationMismatch, () => Fulfillment.ForPickup(
            Tenant, new OrderReference(Tenant, OrderId.New(), LocationId.New(), OrderKind.Takeaway, Xof(1000), true), _settings, "Ada", null, null, _clock));
        DomainAssert.Throws(FulfillmentErrors.CustomerNameRequired, () => Fulfillment.ForPickup(
            Tenant, Order(OrderKind.Takeaway), _settings, " ", null, null, _clock));
    }

    [Fact]
    public void Pickup_can_be_turned_off()
    {
        _settings.ConfigurePickup(false, 20, _clock);

        DomainAssert.Throws(FulfillmentErrors.PickupDisabled, () => Fulfillment.ForPickup(
            Tenant, Order(OrderKind.Takeaway), _settings, "Ada", null, null, _clock));
    }

    [Fact]
    public void A_pickup_goes_ready_then_collected()
    {
        var fulfillment = NewPickup();

        fulfillment.MarkReadyForPickup(_clock);
        fulfillment.ConfirmPickup(_clock);

        Assert.Equal(FulfillmentStatus.Completed, fulfillment.Status);
        Assert.NotNull(fulfillment.ReadyAtUtc);
        Assert.True(fulfillment.IsClosed);
        DomainAssert.Throws(FulfillmentErrors.WrongMode, () => fulfillment.Depart(_clock));
        DomainAssert.Throws(FulfillmentErrors.AlreadyClosed, () => fulfillment.Cancel(null, _clock));
    }

    // ---- Livraison

    [Fact]
    public void A_delivery_snapshots_the_zone_and_promises_lead_plus_travel_time()
    {
        var fulfillment = Fulfillment.ForDelivery(
            Tenant, Order(OrderKind.Delivery), _settings, _zone, "Ada", Phone, "  Rue 12  ", null, "Sonner deux fois", _clock);

        Assert.Equal(("Centre", 1000m, "Rue 12"), (fulfillment.ZoneName, fulfillment.DeliveryFee!.Amount, fulfillment.AddressLine));
        Assert.Equal(_clock.UtcNow.AddMinutes(30 + 25), fulfillment.PromisedAtUtc);

        _settings.UpdateZone(_zone, "Centre", Xof(2000), Xof(5000), 25, _clock);
        Assert.Equal(1000m, fulfillment.DeliveryFee!.Amount);
    }

    [Fact]
    public void A_delivery_checks_the_zone_the_minimum_the_phone_and_the_address()
    {
        DomainAssert.Throws(FulfillmentErrors.ZoneUnavailable, () => Fulfillment.ForDelivery(
            Tenant, Order(OrderKind.Delivery), _settings, DeliveryZoneId.New(), "Ada", Phone, "Rue 12", null, null, _clock));
        DomainAssert.Throws(FulfillmentErrors.BelowMinimumOrder, () => Fulfillment.ForDelivery(
            Tenant, Order(OrderKind.Delivery, total: 4000), _settings, _zone, "Ada", Phone, "Rue 12", null, null, _clock));
        DomainAssert.Throws(FulfillmentErrors.CustomerPhoneRequired, () => Fulfillment.ForDelivery(
            Tenant, Order(OrderKind.Delivery), _settings, _zone, "Ada", null, "Rue 12", null, null, _clock));
        DomainAssert.Throws(FulfillmentErrors.AddressRequired, () => Fulfillment.ForDelivery(
            Tenant, Order(OrderKind.Delivery), _settings, _zone, "Ada", Phone, " ", null, null, _clock));
        DomainAssert.Throws(CommonErrors.TenantMismatch, () => Fulfillment.ForDelivery(
            Tenant, Order(OrderKind.Delivery, tenant: TenantId.New()), _settings, _zone, "Ada", Phone, "Rue 12", null, null, _clock));
    }

    [Fact]
    public void A_delivery_is_assigned_departs_and_is_delivered()
    {
        var fulfillment = NewDelivery();

        fulfillment.AssignCourier("  Koffi ", null, _clock);
        fulfillment.AssignCourier("Bio", Phone, _clock);
        fulfillment.Depart(_clock);
        fulfillment.ConfirmDelivery(_clock);

        Assert.Equal((FulfillmentStatus.Completed, "Bio"), (fulfillment.Status, fulfillment.CourierName));
        Assert.Equal(
            new[] { FulfillmentStatus.Assigned, FulfillmentStatus.OutForDelivery, FulfillmentStatus.Completed },
            fulfillment.DomainEvents.OfType<FulfillmentStatusChanged>().Select(changed => changed.Status));
    }

    [Fact]
    public void A_failed_delivery_needs_a_reason_and_can_be_retried()
    {
        var fulfillment = NewDelivery();
        fulfillment.AssignCourier("Koffi", null, _clock);
        fulfillment.Depart(_clock);

        DomainAssert.Throws(FulfillmentErrors.ReasonRequired, () => fulfillment.MarkFailed(" ", _clock));
        fulfillment.MarkFailed("Client absent", _clock);
        fulfillment.Retry(_clock);

        Assert.Equal(FulfillmentStatus.Pending, fulfillment.Status);
        Assert.Null(fulfillment.CourierName);
        Assert.Equal("Client absent", fulfillment.FailureReason);
    }

    [Fact]
    public void Steps_out_of_order_are_refused()
    {
        var fulfillment = NewDelivery();

        DomainAssert.Throws(FulfillmentErrors.InvalidTransition, () => fulfillment.Depart(_clock));
        DomainAssert.Throws(FulfillmentErrors.InvalidTransition, () => fulfillment.ConfirmDelivery(_clock));
        DomainAssert.Throws(FulfillmentErrors.WrongMode, () => fulfillment.MarkReadyForPickup(_clock));
    }

    [Fact]
    public void The_address_changes_until_the_courier_leaves()
    {
        var fulfillment = NewDelivery();

        fulfillment.ChangeAddress("Rue 14", null, null);
        fulfillment.AssignCourier("Koffi", null, _clock);
        fulfillment.Depart(_clock);

        Assert.Equal("Rue 14", fulfillment.AddressLine);
        DomainAssert.Throws(FulfillmentErrors.InvalidTransition, () => fulfillment.ChangeAddress("Rue 16", null, null));
    }

    [Fact]
    public void A_cancelled_delivery_keeps_its_reason()
    {
        var fulfillment = NewDelivery();

        fulfillment.Cancel("Commande annulée", _clock);

        Assert.Equal((FulfillmentStatus.Cancelled, "Commande annulée"), (fulfillment.Status, fulfillment.CancellationReason));
        Assert.Equal(FulfillmentStatus.Cancelled, DomainAssert.Raised<FulfillmentStatusChanged>(fulfillment).Status);
    }
}
