using Delivery.Application.Commands;
using Delivery.Application.Queries;
using Delivery.Domain;

namespace Delivery.Application;

public sealed class CourierUseCaseTests
{
    private readonly DeliveryFixture _fixture = new();
    private Guid? _zone;

    private async Task<Guid> DeliveryAsync(decimal due, Guid? courierId = null, string? courierName = "Koffi")
    {
        var zone = _zone ??= await _fixture.GivenDeliveryZoneAsync();
        var order = _fixture.GivenOrder(OrderKind.Delivery);
        _fixture.AmountsDue[order] = due;
        var id = await _fixture.CreateDelivery.HandleAsync(new CreateDeliveryCommand(order, zone, "Ada", "+229 90 00 00 01", "Rue 12"));
        await _fixture.AssignCourier.HandleAsync(new AssignCourierCommand(id, courierId is null ? courierName : null, null, courierId));
        await _fixture.Advance.HandleAsync(new AdvanceFulfillmentCommand(id, FulfillmentStep.Depart));
        return id;
    }

    [Fact]
    public async Task Couriers_are_registered_updated_and_deactivated_in_the_restaurant_of_the_context()
    {
        var id = await _fixture.CreateCourier.HandleAsync(new CreateCourierCommand("Koffi", "+229 97 00 00 02", CourierKind.Independent, 500));
        await _fixture.UpdateCourier.HandleAsync(new UpdateCourierCommand(id, "Koffi A.", "+229 97 00 00 02", CourierKind.Independent, 600));
        await _fixture.CreateCourier.HandleAsync(new CreateCourierCommand("Ali", null, CourierKind.Staff, null));
        await _fixture.SetCourierActive.HandleAsync(new SetCourierActiveCommand(id, false));

        var active = await _fixture.ListCouriers.HandleAsync(new ListCouriersQuery());
        var all = await _fixture.ListCouriers.HandleAsync(new ListCouriersQuery(IncludeInactive: true));

        Assert.Equal(["Ali"], active.Select(courier => courier.Name));
        var koffi = Assert.Single(all, courier => courier.Id == id);
        Assert.Equal(("Koffi A.", "+22997000002", 600m, "XOF", false), (koffi.Name, koffi.Phone, koffi.FeePerDelivery, koffi.FeeCurrencyCode, koffi.IsActive));

        _fixture.CurrentTenantId = _fixture.TenantB;
        Assert.Empty(await _fixture.ListCouriers.HandleAsync(new ListCouriersQuery(IncludeInactive: true)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.SetCourierActive.HandleAsync(new SetCourierActiveCommand(id, true)));
    }

    [Fact]
    public async Task A_fee_with_more_decimals_than_the_currency_is_refused()
    {
        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.CreateCourier.HandleAsync(new CreateCourierCommand("Koffi", null, CourierKind.Independent, 500.5m)));

        Assert.Equal(CourierErrors.FeeInvalid.Code, refused.Code);
    }

    [Fact]
    public async Task The_courier_leaves_with_what_remains_to_pay_and_reports_what_he_collected()
    {
        var courier = await _fixture.CreateCourier.HandleAsync(new CreateCourierCommand("Koffi", null, CourierKind.Independent, 500));
        var id = await DeliveryAsync(11000, courier);

        await _fixture.ConfirmDelivery.HandleAsync(new ConfirmDeliveryCommand(id, null, CollectionMethod.Cash));

        var found = await _fixture.GetFulfillment.HandleAsync(new GetFulfillmentQuery(id));
        Assert.Equal((courier, 500m, 11000m, 11000m), (found.CourierId, found.CourierFee, found.AmountToCollect, found.AmountCollected));
        Assert.Equal((FulfillmentStatus.Completed, CollectionMethod.Cash), (found.Status, found.CollectionMethod));
    }

    [Fact]
    public async Task A_location_that_does_not_accept_payment_to_the_courier_cannot_send_an_unpaid_order()
    {
        _fixture.AcceptedCollections.Clear();

        var refused = await Assert.ThrowsAsync<DomainException>(() => DeliveryAsync(11000));

        Assert.Equal(DeliveryApplicationErrors.CollectionNotAccepted.Code, refused.Code);
    }

    [Fact]
    public async Task A_collection_method_the_location_does_not_accept_is_refused()
    {
        _fixture.AcceptedCollections.Remove(CollectionMethod.MobileMoney);
        var id = await DeliveryAsync(11000);

        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ConfirmDelivery.HandleAsync(new ConfirmDeliveryCommand(id, null, CollectionMethod.MobileMoney)));

        Assert.Equal(DeliveryApplicationErrors.CollectionNotAccepted.Code, refused.Code);
        Assert.Equal(FulfillmentStatus.OutForDelivery, _fixture.Fulfillments.Stored(id).Status);
    }

    [Fact]
    public async Task A_paid_order_leaves_with_nothing_to_collect()
    {
        _fixture.AcceptedCollections.Clear();
        var id = await DeliveryAsync(0);

        await _fixture.ConfirmDelivery.HandleAsync(new ConfirmDeliveryCommand(id));

        Assert.Null(_fixture.Fulfillments.Stored(id).AmountToCollect);
    }

    [Fact]
    public async Task Balances_group_the_deliveries_of_each_courier_and_a_remittance_settles_them()
    {
        var koffi = await _fixture.CreateCourier.HandleAsync(new CreateCourierCommand("Koffi", null, CourierKind.Independent, 500));
        var first = await DeliveryAsync(11000, koffi);
        await _fixture.ConfirmDelivery.HandleAsync(new ConfirmDeliveryCommand(first, null, CollectionMethod.Cash));
        var second = await DeliveryAsync(6000, koffi);
        await _fixture.ConfirmDelivery.HandleAsync(new ConfirmDeliveryCommand(second, 6000, CollectionMethod.MobileMoney));
        var byHand = await DeliveryAsync(3000, courierName: "Ali");
        await _fixture.ConfirmDelivery.HandleAsync(new ConfirmDeliveryCommand(byHand, null, CollectionMethod.Cash));

        var balances = await _fixture.Balances.HandleAsync(new GetCourierBalancesQuery(_fixture.Location));

        var balance = balances[0];
        Assert.Equal(("Koffi", 2, 11000m, 6000m, 1000m, 10000m), (balance.CourierName, balance.Deliveries, balance.CashHeld, balance.MobileMoneyCollected, balance.CourierFees, balance.Expected));
        Assert.Equal(("Ali", 3000m), (balances[1].CourierName, balances[1].Expected));

        var remittance = await _fixture.RecordRemittance.HandleAsync(new RecordRemittanceCommand([first, second], 9500, "Il manque 500"));

        var recorded = await _fixture.GetRemittance.HandleAsync(new GetRemittanceQuery(remittance));
        Assert.Equal((10000m, 9500m, -500m, _fixture.CurrentUserId), (recorded.Expected, recorded.Counted, recorded.Difference, recorded.RecordedBy));
        Assert.Equal(["Ali"], (await _fixture.Balances.HandleAsync(new GetCourierBalancesQuery(_fixture.Location))).Select(item => item.CourierName));
        Assert.Single(await _fixture.ListRemittances.HandleAsync(new ListRemittancesQuery(_fixture.Location, null, null, 0, 20)));
    }

    [Fact]
    public async Task A_remittance_covers_only_deliveries_of_the_restaurant_of_the_context()
    {
        var id = await DeliveryAsync(3000);
        await _fixture.ConfirmDelivery.HandleAsync(new ConfirmDeliveryCommand(id, null, CollectionMethod.Cash));

        _fixture.CurrentTenantId = _fixture.TenantB;

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.RecordRemittance.HandleAsync(new RecordRemittanceCommand([id], 3000)));
        Assert.Empty(_fixture.Remittances.Items);
    }

    [Fact]
    public async Task A_remittance_amount_must_fit_the_currency()
    {
        var id = await DeliveryAsync(3000);
        await _fixture.ConfirmDelivery.HandleAsync(new ConfirmDeliveryCommand(id, null, CollectionMethod.Cash));

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.RecordRemittance.HandleAsync(new RecordRemittanceCommand([id], 2999.5m)));

        Assert.Equal(DeliveryApplicationErrors.RemittanceAmountInvalid.Code, refused.Code);
    }
}
