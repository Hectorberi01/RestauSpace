using Delivery.Application.Commands;
using Delivery.Application.Queries;
using Delivery.Domain;

namespace Delivery.Application;

public sealed class DeliveryUseCaseTests
{
    private readonly DeliveryFixture _fixture = new();

    [Fact]
    public async Task A_location_without_settings_reads_the_defaults()
    {
        var settings = await _fixture.GetSettings.HandleAsync(new GetDeliverySettingsQuery(_fixture.Location));

        Assert.False(settings.IsConfigured);
        Assert.True(settings.PickupEnabled);
        Assert.False(settings.DeliveryEnabled);
        Assert.Equal("XOF", settings.CurrencyCode);
        Assert.Empty(_fixture.Settings.Items);
    }

    [Fact]
    public async Task Settings_are_created_on_the_first_change_in_the_tenant_of_the_context()
    {
        var zone = await _fixture.GivenDeliveryZoneAsync();

        var settings = await _fixture.GetSettings.HandleAsync(new GetDeliverySettingsQuery(_fixture.Location));

        var stored = Assert.Single(_fixture.Settings.Items);
        Assert.Equal(_fixture.TenantA, stored.TenantId);
        Assert.True(settings.IsConfigured);
        Assert.True(settings.DeliveryEnabled);
        Assert.Equal((zone, "Centre", 1000m), (settings.Zones[0].Id, settings.Zones[0].Name, settings.Zones[0].Fee));
    }

    [Fact]
    public async Task A_location_of_another_restaurant_is_not_found()
    {
        var other = _fixture.GivenLocation(_fixture.TenantB);

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetSettings.HandleAsync(new GetDeliverySettingsQuery(other)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ConfigurePickup.HandleAsync(new ConfigurePickupCommand(other, false, 10)));
        Assert.Empty(_fixture.Settings.Items);
    }

    [Fact]
    public async Task A_suspended_restaurant_cannot_write()
    {
        _fixture.WritableTenants.Remove(_fixture.TenantA);

        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ConfigurePickup.HandleAsync(new ConfigurePickupCommand(_fixture.Location, false, 10)));

        Assert.Equal(DeliveryApplicationErrors.TenantNotWritable, refused.Error);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_delivery_is_created_followed_and_found_by_its_order()
    {
        var zone = await _fixture.GivenDeliveryZoneAsync();
        var order = _fixture.GivenOrder(OrderKind.Delivery);

        var id = await _fixture.CreateDelivery.HandleAsync(new CreateDeliveryCommand(order, zone, "Ada", "+229 90 00 00 01", "Rue 12"));
        await _fixture.AssignCourier.HandleAsync(new AssignCourierCommand(id, "Koffi"));
        await _fixture.Advance.HandleAsync(new AdvanceFulfillmentCommand(id, FulfillmentStep.Depart));

        var found = await _fixture.GetOrderFulfillment.HandleAsync(new GetOrderFulfillmentQuery(order));
        Assert.Equal((id, FulfillmentStatus.OutForDelivery, "Koffi", 1000m), (found.Id, found.Status, found.CourierName, found.DeliveryFee));
        Assert.Equal("+22990000001", found.CustomerPhone);
        Assert.Equal(3, _fixture.SaveCount);
    }

    [Fact]
    public async Task An_order_has_one_active_fulfillment()
    {
        var order = _fixture.GivenOrder(OrderKind.Takeaway);
        var first = await _fixture.CreatePickup.HandleAsync(new CreatePickupCommand(order, "Ada"));

        var twice = await Assert.ThrowsAsync<DomainException>(() => _fixture.CreatePickup.HandleAsync(new CreatePickupCommand(order, "Ada")));
        Assert.Equal(DeliveryApplicationErrors.OrderAlreadyHasFulfillment, twice.Error);

        await _fixture.Advance.HandleAsync(new AdvanceFulfillmentCommand(first, FulfillmentStep.Cancel));
        var second = await _fixture.CreatePickup.HandleAsync(new CreatePickupCommand(order, "Ada"));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Orders_and_fulfillments_of_another_restaurant_are_not_found()
    {
        var otherOrder = _fixture.GivenOrder(OrderKind.Takeaway, tenantId: _fixture.TenantB);
        var mine = await _fixture.CreatePickup.HandleAsync(new CreatePickupCommand(_fixture.GivenOrder(OrderKind.Takeaway), "Ada"));

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.CreatePickup.HandleAsync(new CreatePickupCommand(otherOrder, "Ada")));
        _fixture.CurrentTenantId = _fixture.TenantB;
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetFulfillment.HandleAsync(new GetFulfillmentQuery(mine)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Advance.HandleAsync(new AdvanceFulfillmentCommand(mine, FulfillmentStep.PickedUp)));
        Assert.Empty(await _fixture.List.HandleAsync(new ListFulfillmentsQuery(null, null, null, false, null, null, 0, 20)));
    }

    [Fact]
    public async Task Order_events_move_the_pickup_and_are_idempotent()
    {
        var order = _fixture.GivenOrder(OrderKind.Takeaway);
        var id = await _fixture.CreatePickup.HandleAsync(new CreatePickupCommand(order, "Ada"));

        Assert.True(await _fixture.OrderReady.HandleAsync(new MarkOrderReadyForPickupCommand(order)));
        Assert.False(await _fixture.OrderReady.HandleAsync(new MarkOrderReadyForPickupCommand(order)));
        Assert.Equal(FulfillmentStatus.ReadyForPickup, _fixture.Fulfillments.Stored(id).Status);

        Assert.True(await _fixture.OrderCancelled.HandleAsync(new CancelOrderFulfillmentCommand(order, null)));
        Assert.False(await _fixture.OrderCancelled.HandleAsync(new CancelOrderFulfillmentCommand(order, null)));
        Assert.Equal("Commande annulée", _fixture.Fulfillments.Stored(id).CancellationReason);
    }

    [Fact]
    public async Task The_board_lists_open_fulfillments_by_promised_time()
    {
        var zone = await _fixture.GivenDeliveryZoneAsync();
        var delivery = await _fixture.CreateDelivery.HandleAsync(
            new CreateDeliveryCommand(_fixture.GivenOrder(OrderKind.Delivery), zone, "Ada", "+22990000001", "Rue 12"));
        var pickup = await _fixture.CreatePickup.HandleAsync(new CreatePickupCommand(_fixture.GivenOrder(OrderKind.Takeaway), "Bio"));
        var done = await _fixture.CreatePickup.HandleAsync(new CreatePickupCommand(_fixture.GivenOrder(OrderKind.Takeaway), "Chidi"));
        await _fixture.Advance.HandleAsync(new AdvanceFulfillmentCommand(done, FulfillmentStep.PickedUp));

        var open = await _fixture.List.HandleAsync(new ListFulfillmentsQuery(_fixture.Location, null, null, true, null, null, 0, 20));

        Assert.Equal(new[] { pickup, delivery }, open.Select(fulfillment => fulfillment.Id));
    }
}
