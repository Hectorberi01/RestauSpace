using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;
using Storefront.Application.Commands;
using Storefront.Application.Kiosks;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Application;

public sealed class KioskTests
{
    private readonly StorefrontFixture _fixture = new();
    private readonly Guid _burger = Guid.NewGuid();

    public KioskTests()
    {
        _fixture.Prices[_burger] = 4000m;
        _fixture.Options.Add((_fixture.TenantA, SalesAppKind.Kiosk));
        _fixture.KioskLimit = 1;
    }

    private async Task<IssuedSalesAppKey> KioskAppAsync()
    {
        _fixture.CurrentTenantId = _fixture.TenantA;
        return await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Borne", SalesAppKind.Kiosk));
    }

    /// <summary>La borne affiche un code, le personnel le saisit, la borne retire son jeton.</summary>
    private async Task<string> PairAsync(IssuedSalesAppKey app, Guid location, string name = "Borne entrée")
    {
        await _fixture.SignInAsync(app.Key);
        var started = await _fixture.StartPairing.HandleAsync(new StartKioskPairingCommand());
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.ApprovePairing.HandleAsync(new ApproveKioskPairingCommand(started.Code, location, name));
        await _fixture.SignInAsync(app.Key);
        var claimed = await _fixture.ClaimPairing.HandleAsync(new ClaimKioskPairingCommand(started.PairingId, started.Secret));
        return claimed.DeviceToken!;
    }

    [Fact]
    public async Task A_kiosk_is_paired_with_a_code_entered_in_the_console_and_then_sees_its_location_only()
    {
        var app = await KioskAppAsync();
        await _fixture.SignInAsync(app.Key);
        var started = await _fixture.StartPairing.HandleAsync(new StartKioskPairingCommand());
        var pending = await _fixture.ClaimPairing.HandleAsync(new ClaimKioskPairingCommand(started.PairingId, started.Secret));
        var wrongSecret = await _fixture.ClaimPairing.HandleAsync(new ClaimKioskPairingCommand(started.PairingId, "nope"));
        var unpaired = await Assert.ThrowsAsync<DomainException>(() => _fixture.Storefront.HandleAsync(new GetStorefrontQuery()));
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.ApprovePairing.HandleAsync(new ApproveKioskPairingCommand(started.Code[..3] + " " + started.Code[3..], _fixture.Cotonou.Value, "Borne entrée"));
        await _fixture.SignInAsync(app.Key);
        var paired = await _fixture.ClaimPairing.HandleAsync(new ClaimKioskPairingCommand(started.PairingId, started.Secret));
        var replay = await _fixture.ClaimPairing.HandleAsync(new ClaimKioskPairingCommand(started.PairingId, started.Secret));
        var principal = await _fixture.SignInAsync(app.Key, deviceToken: paired.DeviceToken);
        var storefront = await _fixture.Storefront.HandleAsync(new GetStorefrontQuery());
        var calavi = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Catalog.HandleAsync(new GetStorefrontCatalogQuery(_fixture.Calavi.Value)));

        Assert.Matches("^[0-9]{6}$", started.Code);
        Assert.DoesNotContain(started.Secret, started.ToString(), StringComparison.Ordinal);
        Assert.Equal(KioskPairingState.Pending, pending.State);
        Assert.Equal(KioskPairingState.Expired, wrongSecret.State);
        Assert.Equal(KioskApplicationErrors.DeviceRequired, unpaired.Error);
        Assert.Equal((KioskPairingState.Paired, _fixture.Cotonou.Value, "Borne entrée"), (paired.State, paired.LocationId!.Value, paired.DeviceName));
        Assert.StartsWith(KioskSecrets.TokenPrefix, paired.DeviceToken, StringComparison.Ordinal);
        Assert.Equal(KioskPairingState.Expired, replay.State);
        Assert.Equal(KioskSecrets.Hash(paired.DeviceToken!), Assert.Single(_fixture.Kiosks).TokenHash);
        Assert.Equal(_fixture.Cotonou.Value, principal!.DeviceLocationId);
        Assert.Equal(["Cotonou"], storefront.Locations.Select(location => location.Name));
        Assert.Equal(StorefrontApplicationErrors.LocationNotFound, calavi.Error);
    }

    [Fact]
    public async Task The_number_of_kiosks_follows_the_option_and_a_revoked_kiosk_is_refused()
    {
        var app = await KioskAppAsync();
        var token = await PairAsync(app, _fixture.Cotonou.Value);
        await _fixture.SignInAsync(app.Key);
        var second = await _fixture.StartPairing.HandleAsync(new StartKioskPairingCommand());
        _fixture.CurrentTenantId = _fixture.TenantA;
        var full = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.ApprovePairing.HandleAsync(new ApproveKioskPairingCommand(second.Code, _fixture.Cotonou.Value, "Borne 2")));
        var listed = await _fixture.ListKiosks.HandleAsync(new ListKiosksQuery());
        await _fixture.RevokeKiosk.HandleAsync(new RevokeKioskCommand(listed.Kiosks[0].Id));
        var revoked = await _fixture.SignInAsync(app.Key, deviceToken: token);
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.ApprovePairing.HandleAsync(new ApproveKioskPairingCommand(second.Code, _fixture.Cotonou.Value, "Borne 2"));

        Assert.Equal(KioskApplicationErrors.LimitReached, full.Error);
        Assert.Equal((1L, 1), (listed.Limit!.Value, listed.Active));
        Assert.Null(revoked);
    }

    [Fact]
    public async Task Beyond_the_kiosk_limit_the_last_paired_kiosk_is_frozen_and_refused()
    {
        _fixture.KioskLimit = 2;
        var app = await KioskAppAsync();
        var first = await PairAsync(app, _fixture.Cotonou.Value, "Borne 1");
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await PairAsync(app, _fixture.Cotonou.Value, "Borne 2");
        _fixture.KioskLimit = 1;

        await _fixture.SignInAsync(app.Key, deviceToken: first);
        await _fixture.EnsureAccess.HandleAsync(new EnsureStorefrontAccessCommand(SalesAppKind.Kiosk));
        await _fixture.SignInAsync(app.Key, deviceToken: second);
        var frozen = await Assert.ThrowsAsync<ForbiddenException>(
            () => _fixture.EnsureAccess.HandleAsync(new EnsureStorefrontAccessCommand(SalesAppKind.Kiosk)));
        _fixture.CurrentTenantId = _fixture.TenantA;
        var listed = await _fixture.ListKiosks.HandleAsync(new ListKiosksQuery());

        Assert.Equal(PlanFeatures.OverLimitCode, frozen.Error.Code);
        Assert.Equal(["Borne 2"], listed.Kiosks.Where(kiosk => kiosk.IsFrozen).Select(kiosk => kiosk.Name));
    }

    [Fact]
    public async Task A_wrong_or_expired_code_a_foreign_location_and_another_app_token_are_refused()
    {
        var app = await KioskAppAsync();
        var other = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Borne 2", SalesAppKind.Kiosk));
        var token = await PairAsync(app, _fixture.Cotonou.Value);
        await _fixture.SignInAsync(app.Key);
        var started = await _fixture.StartPairing.HandleAsync(new StartKioskPairingCommand());
        _fixture.CurrentTenantId = _fixture.TenantA;
        _fixture.KioskLimit = null;

        var wrong = await Assert.ThrowsAsync<DomainException>(() => _fixture.ApprovePairing.HandleAsync(new ApproveKioskPairingCommand("000000", _fixture.Cotonou.Value, "X")));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ApprovePairing.HandleAsync(new ApproveKioskPairingCommand(started.Code, _fixture.Paris.Value, "X")));
        _fixture.Clock.Advance(KioskPairing.Lifetime);
        var expired = await Assert.ThrowsAsync<DomainException>(() => _fixture.ApprovePairing.HandleAsync(new ApproveKioskPairingCommand(started.Code, _fixture.Cotonou.Value, "X")));
        var crossed = await _fixture.SignInAsync(other.Key, deviceToken: token);
        _fixture.CurrentTenantId = _fixture.TenantA;
        var website = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Site", SalesAppKind.Web));
        var onWebsite = await _fixture.SignInAsync(website.Key, deviceToken: token);

        Assert.Equal(KioskErrors.PairingInvalid, wrong.Error);
        Assert.Equal(StorefrontApplicationErrors.LocationNotFound, foreign.Error);
        Assert.Equal(KioskErrors.PairingInvalid, expired.Error);
        Assert.Null(crossed);
        Assert.Null(onWebsite);
    }

    [Fact]
    public async Task A_kiosk_order_paid_at_the_counter_prints_its_ticket_without_any_personal_data()
    {
        var app = await KioskAppAsync();
        await _fixture.SaveSettings.HandleAsync(new SaveOnlineOrderingSettingsCommand(
            _fixture.Cotonou.Value, false, [ServiceMode.DineIn, ServiceMode.Takeaway], 20, null, 10, 15,
            [new ChannelPolicyInput(SalesAppKind.Kiosk, true, true, null, OrderAcceptance.Automatic)]));
        var token = await PairAsync(app, _fixture.Cotonou.Value);
        await _fixture.SignInAsync(app.Key, deviceToken: token);

        var placed = await _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, ServiceMode.DineIn, [new CartLine(_burger, 2, Note: "Sans oignon")], PaymentMode.Counter, new GuestContactInput("Awa", Phone: "+22990000001")));
        _fixture.HasTicketPrinter = false;
        var withoutPrinter = await _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, ServiceMode.Takeaway, [new CartLine(_burger, 1)], PaymentMode.Counter));

        Assert.True(placed.TicketPrinted);
        Assert.False(withoutPrinter.TicketPrinted);
        Assert.Null(_fixture.Orders[OrderId.From(placed.OrderId)].Order.CustomerId);
        Assert.Empty(_fixture.CustomerRequests);
        var (location, reference, ticket) = Assert.Single(_fixture.Tickets);
        Assert.Equal((_fixture.Cotonou, $"order-{placed.OrderId:N}"), (location, reference));
        Assert.Equal(("Chez Awa", "Cotonou", "Sur place", KioskTicketTexts.PayAtCounter, "8 000 F CFA"), (ticket.RestaurantName, ticket.LocationName, ticket.ServiceMode, ticket.PaymentText, ticket.Total));
        Assert.DoesNotContain("Awa ", ticket.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("+229", ticket.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_kiosk_order_paid_by_qr_code_prints_its_ticket_once_paid()
    {
        _fixture.PaymentOptions[_fixture.TenantA] = [new OnlinePaymentOption("fedapay", "FedaPay", ["MobileMoney"])];
        var app = await KioskAppAsync();
        var token = await PairAsync(app, _fixture.Cotonou.Value);
        await _fixture.SignInAsync(app.Key, deviceToken: token);
        var placed = await _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, ServiceMode.Takeaway, [new CartLine(_burger, 1)], PaymentMode.Online));
        await _fixture.StartPayment.HandleAsync(new StartGuestPaymentCommand(placed.TrackingToken, "fedapay"));
        var beforePayment = _fixture.Tickets.Count;
        _fixture.Capture(OrderId.From(placed.OrderId));
        _fixture.CurrentTenantId = _fixture.TenantA;
        _fixture.DeviceId = null;
        _fixture.DeviceLocationId = null;
        await _fixture.RecordPayment.HandleAsync(new RecordOnlinePaymentCommand(placed.OrderId));

        Assert.False(placed.TicketPrinted);
        Assert.Equal(0, beforePayment);
        Assert.Equal(KioskTicketTexts.Paid, Assert.Single(_fixture.Tickets).Ticket.PaymentText);
    }
}
