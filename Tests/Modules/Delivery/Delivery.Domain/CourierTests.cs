namespace Delivery.Domain;

public class CourierTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly LocationReference Place = new(Tenant, LocationId.New(), IsOperational: true);
    private static readonly PhoneNumber Phone = PhoneNumber.Create("+22990000001");

    private readonly FakeClock _clock = new();
    private readonly DeliverySettings _settings;
    private readonly DeliveryZoneId _zone;

    public CourierTests()
    {
        _settings = DeliverySettings.Create(Tenant, Place, Currency.XOF, _clock);
        _zone = _settings.AddZone("Centre", Xof(1000), Xof(5000), 25, _clock);
        _settings.ConfigureDelivery(true, 30, _clock);
    }

    private static Money Xof(decimal amount) => Money.Of(amount, Currency.XOF);

    private Fulfillment Delivery()
    {
        var order = new OrderReference(Tenant, OrderId.New(), Place.LocationId, OrderKind.Delivery, Xof(10000), true);
        var fulfillment = Fulfillment.ForDelivery(Tenant, order, _settings, _zone, "Ada", Phone, "Rue 12", null, null, _clock);
        fulfillment.ClearDomainEvents();
        return fulfillment;
    }

    private Courier Zemidjan(decimal? fee = 500) =>
        Courier.Create(Tenant, "  Koffi  ", Phone, CourierKind.Independent, fee is { } value ? Xof(value) : null, _clock);

    private Fulfillment Delivered(Courier courier, decimal? due, decimal? collected = null, CollectionMethod method = CollectionMethod.Cash)
    {
        var fulfillment = Delivery();
        fulfillment.AssignCourier(courier, _clock);
        fulfillment.Depart(_clock, due is { } amount ? Xof(amount) : null);
        if (due is null)
        {
            fulfillment.ConfirmDelivery(_clock);
        }
        else
        {
            fulfillment.ConfirmDelivery(_clock, collected, method);
        }

        fulfillment.ClearDomainEvents();
        return fulfillment;
    }

    // ---- Fiche livreur

    [Fact]
    public void A_courier_has_a_name_a_kind_and_an_optional_fee()
    {
        var courier = Zemidjan();

        Assert.Equal(("Koffi", CourierKind.Independent, 500m, true), (courier.Name, courier.Kind, courier.FeeAmount, courier.IsActive));
        DomainAssert.Throws(CourierErrors.NameRequired, () => Courier.Create(Tenant, " ", null, CourierKind.Staff, null, _clock));
        DomainAssert.Throws(CourierErrors.KindInvalid, () => Courier.Create(Tenant, "Ali", null, (CourierKind)9, null, _clock));
        DomainAssert.Throws(CourierErrors.FeeInvalid, () => Courier.Create(Tenant, "Ali", null, CourierKind.Staff, Xof(-1), _clock));
    }

    [Fact]
    public void A_registered_courier_freezes_his_fee_on_the_delivery()
    {
        var courier = Zemidjan();
        var fulfillment = Delivery();

        fulfillment.AssignCourier(courier, _clock);
        courier.Update("Koffi", Phone, CourierKind.Independent, Xof(700), _clock);

        Assert.Equal((courier.Id, "Koffi", 500m), (fulfillment.CourierId!.Value, fulfillment.CourierName, fulfillment.CourierFeeAmount));
    }

    [Fact]
    public void A_deactivated_or_foreign_courier_cannot_take_a_delivery()
    {
        var inactive = Zemidjan();
        inactive.Deactivate(_clock);
        var foreign = Courier.Create(TenantId.New(), "Ali", null, CourierKind.Staff, null, _clock);

        DomainAssert.Throws(CourierErrors.Inactive, () => Delivery().AssignCourier(inactive, _clock));
        DomainAssert.Throws(FulfillmentErrors.CourierTenantMismatch, () => Delivery().AssignCourier(foreign, _clock));
    }

    // ---- Paiement à la livraison

    [Fact]
    public void The_courier_collects_what_is_due_and_the_payment_is_raised()
    {
        var fulfillment = Delivery();
        fulfillment.AssignCourier("Koffi", null, _clock);
        fulfillment.Depart(_clock, Xof(11000));

        fulfillment.ConfirmDelivery(_clock, null, CollectionMethod.Cash);

        Assert.Equal((11000m, 11000m, 11000m), (fulfillment.AmountToCollect!.Value, fulfillment.AmountCollected!.Value, fulfillment.CashHeld));
        var collected = DomainAssert.Raised<DeliveryPaymentCollected>(fulfillment);
        Assert.Equal((Xof(11000), CollectionMethod.Cash), (collected.Amount, collected.Method));
    }

    [Fact]
    public void A_paid_order_has_nothing_to_collect()
    {
        var fulfillment = Delivery();
        fulfillment.AssignCourier("Koffi", null, _clock);
        fulfillment.Depart(_clock, Xof(0));

        DomainAssert.Throws(FulfillmentErrors.NothingToCollect, () => fulfillment.ConfirmDelivery(_clock, 100, CollectionMethod.Cash));
        fulfillment.ConfirmDelivery(_clock);

        Assert.Null(fulfillment.AmountToCollect);
        DomainAssert.NotRaised<DeliveryPaymentCollected>(fulfillment);
        Assert.False(fulfillment.AwaitsRemittance);
    }

    [Fact]
    public void The_collection_needs_a_method_and_an_amount_up_to_what_is_due()
    {
        var fulfillment = Delivery();
        fulfillment.AssignCourier("Koffi", null, _clock);
        fulfillment.Depart(_clock, Xof(11000));

        DomainAssert.Throws(FulfillmentErrors.CollectionRequired, () => fulfillment.ConfirmDelivery(_clock));
        DomainAssert.Throws(FulfillmentErrors.CollectedAmountInvalid, () => fulfillment.ConfirmDelivery(_clock, 12000, CollectionMethod.Cash));
        DomainAssert.Throws(FulfillmentErrors.CollectedAmountInvalid, () => fulfillment.ConfirmDelivery(_clock, 0, CollectionMethod.Cash));
        DomainAssert.Throws(FulfillmentErrors.CollectionMethodInvalid, () => fulfillment.ConfirmDelivery(_clock, 100, (CollectionMethod)9));
        Assert.Equal(FulfillmentStatus.OutForDelivery, fulfillment.Status);
    }

    [Fact]
    public void Mobile_money_is_not_cash_held_by_the_courier()
    {
        var delivered = Delivered(Zemidjan(fee: null), 8000, method: CollectionMethod.MobileMoney);

        Assert.Equal((8000m, 0m), (delivered.AmountCollected!.Value, delivered.CashHeld));
        Assert.False(delivered.AwaitsRemittance);
    }

    [Fact]
    public void A_retry_forgets_the_courier_and_the_amount_to_collect()
    {
        var fulfillment = Delivery();
        fulfillment.AssignCourier(Zemidjan(), _clock);
        fulfillment.Depart(_clock, Xof(5000));
        fulfillment.MarkFailed("Client absent", _clock);

        fulfillment.Retry(_clock);

        Assert.Equal((null, null, null), (fulfillment.CourierId, fulfillment.CourierFeeAmount, fulfillment.AmountToCollect));
    }

    // ---- Remise du livreur

    [Fact]
    public void A_remittance_settles_cash_minus_fees_and_shows_the_gap()
    {
        var courier = Zemidjan();
        var first = Delivered(courier, 11000);
        var second = Delivered(courier, 6000, collected: 5000);
        var paid = Delivered(courier, null);

        var remittance = CourierRemittance.Record(Tenant, [first, second, paid], 14000, " Il manque 500 ", Guid.NewGuid(), _clock);

        Assert.Equal((16000m, 1500m, 14500m), (remittance.CashCollected, remittance.CourierFees, remittance.Expected));
        Assert.Equal((14000m, -500m, "Il manque 500"), (remittance.Counted, remittance.Difference, remittance.Note));
        Assert.Equal(3, remittance.Items.Count);
        Assert.All(new[] { first, second, paid }, delivery => Assert.Equal(remittance.Id, delivery.RemittanceId));
        Assert.All(new[] { first, second, paid }, delivery => Assert.False(delivery.AwaitsRemittance));
        var recorded = DomainAssert.Raised<CourierRemittanceRecorded>(remittance);
        Assert.Equal((Xof(14000), Xof(14500)), (recorded.Counted, recorded.Expected));
    }

    [Fact]
    public void The_restaurant_pays_a_courier_who_collected_nothing()
    {
        var courier = Zemidjan();
        var deliveries = new[] { Delivered(courier, null), Delivered(courier, null) };

        var remittance = CourierRemittance.Record(Tenant, deliveries, -1000, null, null, _clock);

        Assert.Equal((-1000m, -1000m, 0m), (remittance.Expected, remittance.Counted, remittance.Difference));
    }

    [Fact]
    public void A_delivery_is_settled_once_and_by_its_own_courier()
    {
        var koffi = Zemidjan();
        var ali = Courier.Create(Tenant, "Ali", null, CourierKind.Staff, Xof(300), _clock);
        var delivery = Delivered(koffi, 11000);
        CourierRemittance.Record(Tenant, [delivery], 10500, null, null, _clock);

        DomainAssert.Throws(RemittanceErrors.NotAwaitingRemittance, () => CourierRemittance.Record(Tenant, [delivery], 10500, null, null, _clock));
        DomainAssert.Throws(
            RemittanceErrors.MixedCouriers, () => CourierRemittance.Record(Tenant, [Delivered(koffi, 1000), Delivered(ali, 1000)], 0, null, null, _clock));
        DomainAssert.Throws(RemittanceErrors.NoDelivery, () => CourierRemittance.Record(Tenant, [], 0, null, null, _clock));
    }

    [Fact]
    public void An_unfinished_delivery_cannot_be_settled()
    {
        var fulfillment = Delivery();
        fulfillment.AssignCourier(Zemidjan(), _clock);
        fulfillment.Depart(_clock, Xof(2000));

        DomainAssert.Throws(RemittanceErrors.NotAwaitingRemittance, () => CourierRemittance.Record(Tenant, [fulfillment], 1500, null, null, _clock));
    }

    [Fact]
    public void Couriers_typed_by_hand_are_matched_by_name()
    {
        var first = Delivery();
        first.AssignCourier("Koffi", null, _clock);
        var second = Delivery();
        second.AssignCourier("KOFFI", null, _clock);
        var other = Delivery();
        other.AssignCourier("Ali", null, _clock);

        Assert.True(CourierRemittance.SameCourier(first, second));
        Assert.False(CourierRemittance.SameCourier(first, other));
    }
}
