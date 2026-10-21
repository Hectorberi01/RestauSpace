using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;
using Storefront.Application.Commands;
using Storefront.Application.Customers;
using Storefront.Domain;

namespace Storefront.Application;

public sealed class CustomerAccountTests
{
    private const string Phone = "+22990000001";
    private readonly StorefrontFixture _fixture = new();
    private readonly Guid _burger = Guid.NewGuid();
    private readonly Guid _reward = Guid.NewGuid();
    private IssuedSalesAppKey _website = null!;

    public CustomerAccountTests()
    {
        _fixture.Prices[_burger] = 4000m;
        _fixture.Rewards.Add((_reward, "Boisson offerte", 300, 1000m));
    }

    private IReadOnlyList<CartLine> Cart() => [new CartLine(_burger, 2)];

    private async Task<IssuedSalesAppKey> WebsiteAsync(string name = "Site")
    {
        _fixture.CurrentTenantId = _fixture.TenantA;
        _fixture.Options.Add((_fixture.TenantA, SalesAppKind.Web));
        _fixture.Options.Add((_fixture.TenantA, SalesAppKind.MobileApp));
        await _fixture.SaveSettings.HandleAsync(new SaveOnlineOrderingSettingsCommand(
            _fixture.Cotonou.Value, false, [ServiceMode.Takeaway, ServiceMode.Delivery], 20, null, 10, 15,
            [new ChannelPolicyInput(SalesAppKind.Web, true, true, null, OrderAcceptance.Automatic)]));
        var issued = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand(name, SalesAppKind.Web));
        await _fixture.SignInAsync(issued.Key);
        return issued;
    }

    /// <summary>Connexion complète par SMS : renvoie le jeton de session.</summary>
    private async Task<string> SignedInAsync(string name = "Awa", string address = Phone)
    {
        _website ??= await WebsiteAsync();
        await _fixture.SignInAsync(_website.Key);
        var sent = await _fixture.RequestCode.HandleAsync(new RequestCustomerCodeCommand(CustomerSignInChannel.Sms, address));
        var result = await _fixture.VerifyCode.HandleAsync(new VerifyCustomerCodeCommand(sent.Ticket, StorefrontFixture.GoodCode, name));
        await _fixture.SignInAsync(_website.Key, customerToken: result.Token);
        return result.Token!;
    }

    [Fact]
    public async Task A_new_customer_proves_the_code_then_gives_a_name_and_gets_a_session()
    {
        _website = await WebsiteAsync();

        var sent = await _fixture.RequestCode.HandleAsync(new RequestCustomerCodeCommand(CustomerSignInChannel.Sms, " +22990000001 "));
        var wrong = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.VerifyCode.HandleAsync(new VerifyCustomerCodeCommand(sent.Ticket, "000000")));
        var nameStep = await _fixture.VerifyCode.HandleAsync(new VerifyCustomerCodeCommand(sent.Ticket, StorefrontFixture.GoodCode));
        var replay = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.VerifyCode.HandleAsync(new VerifyCustomerCodeCommand(sent.Ticket, StorefrontFixture.GoodCode, "Awa")));
        var signedIn = await _fixture.VerifyCode.HandleAsync(new VerifyCustomerCodeCommand(nameStep.Ticket, null, " Awa ", MarketingConsent: true));
        await _fixture.SignInAsync(_website.Key, customerToken: signedIn.Token);
        var account = await _fixture.Account.HandleAsync(new GetCustomerAccountQuery());

        Assert.Equal(("••• 01", CustomerSignInChannel.Sms), (sent.Destination, sent.Channel));
        Assert.DoesNotContain(Phone, sent.Ticket, StringComparison.Ordinal);
        Assert.Equal(Phone, Assert.Single(_fixture.SentCodes).Address);
        Assert.Equal("notifications.otp.invalid", wrong.Code);
        Assert.Equal((CustomerSignInStatus.NameRequired, (string?)null), (nameStep.Status, nameStep.Token));
        Assert.Equal("notifications.otp.already_used", replay.Code);
        Assert.Equal(CustomerSignInStatus.SignedIn, signedIn.Status);
        Assert.StartsWith(CustomerSecrets.TokenPrefix, signedIn.Token, StringComparison.Ordinal);
        Assert.Equal(_fixture.Clock.UtcNow + CustomerSession.Lifetime, signedIn.ExpiresAtUtc);
        Assert.NotNull(_fixture.CustomerId);
        Assert.Equal(("Awa", Phone, (string?)null), (account.Name, account.Phone, account.Email));
        Assert.True(Assert.Single(_fixture.CustomerRequests).MarketingConsent);
        var session = Assert.Single(_fixture.Sessions);
        Assert.NotEqual(signedIn.Token, session.TokenHash.Value);
        Assert.DoesNotContain(signedIn.Token!, signedIn.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_known_guest_signs_in_straight_to_their_record_and_keeps_their_orders()
    {
        _website = await WebsiteAsync();
        var guestOrder = await _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), PaymentMode.Counter, new GuestContactInput("Awa", Phone: Phone)));

        var sent = await _fixture.RequestCode.HandleAsync(new RequestCustomerCodeCommand(CustomerSignInChannel.WhatsApp, Phone));
        var signedIn = await _fixture.VerifyCode.HandleAsync(new VerifyCustomerCodeCommand(sent.Ticket, StorefrontFixture.GoodCode, MarketingConsent: true));
        await _fixture.SignInAsync(_website.Key, customerToken: signedIn.Token);
        var history = await _fixture.CustomerOrders.HandleAsync(new ListCustomerOrdersQuery());

        Assert.Equal(CustomerSignInStatus.SignedIn, signedIn.Status);
        Assert.Equal(_fixture.Customers[(_fixture.TenantA, Phone)], _fixture.CustomerId!.Value.Value);
        Assert.Equal(_fixture.CustomerId.Value.Value, Assert.Single(_fixture.ConsentsGranted));
        var order = Assert.Single(history);
        Assert.Equal((guestOrder.OrderId, "Cotonou"), (order.OrderId, order.LocationName));
        Assert.Equal(guestOrder.OrderId, _fixture.Tokens.Read(order.TrackingToken)!.OrderId.Value);
    }

    [Fact]
    public async Task A_ticket_only_works_where_it_was_issued_and_until_it_expires()
    {
        _website = await WebsiteAsync();
        var other = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Autre site", SalesAppKind.Web));
        await _fixture.SignInAsync(_website.Key);
        var sent = await _fixture.RequestCode.HandleAsync(new RequestCustomerCodeCommand(CustomerSignInChannel.Email, "awa@example.com"));
        var badAddress = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.RequestCode.HandleAsync(new RequestCustomerCodeCommand(CustomerSignInChannel.Email, Phone)));
        var garbage = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.VerifyCode.HandleAsync(new VerifyCustomerCodeCommand(sent.Ticket[..^4] + "AAAA", StorefrontFixture.GoodCode)));

        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.SignInAsync(other.Key);
        var otherApp = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.VerifyCode.HandleAsync(new VerifyCustomerCodeCommand(sent.Ticket, StorefrontFixture.GoodCode, "Awa")));
        await _fixture.SignInAsync(_website.Key);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(11));
        var expired = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.VerifyCode.HandleAsync(new VerifyCustomerCodeCommand(sent.Ticket, StorefrontFixture.GoodCode, "Awa")));

        Assert.Equal("a•••@example.com", sent.Destination);
        Assert.Equal(CustomerAccountErrors.AddressInvalid, badAddress.Error);
        Assert.Equal(CustomerAccountErrors.TicketInvalid, garbage.Error);
        Assert.Equal(CustomerAccountErrors.TicketInvalid, otherApp.Error);
        Assert.Equal(CustomerAccountErrors.TicketInvalid, expired.Error);
        Assert.Empty(_fixture.Sessions);
    }

    [Fact]
    public async Task A_session_ends_on_sign_out_after_its_lifetime_and_never_crosses_applications()
    {
        var token = await SignedInAsync();
        var app = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Application", SalesAppKind.MobileApp));

        await _fixture.SignInAsync(app.Key, customerToken: token);
        var onOtherApp = _fixture.CustomerId;
        await _fixture.SignInAsync(_website.Key, customerToken: token);
        await _fixture.SignOut.HandleAsync(new SignOutCustomerCommand());
        await _fixture.SignInAsync(_website.Key, customerToken: token);
        var afterSignOut = _fixture.CustomerId;
        var account = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Account.HandleAsync(new GetCustomerAccountQuery()));

        var second = await SignedInAsync();
        _fixture.Clock.Advance(CustomerSession.Lifetime);
        await _fixture.SignInAsync(_website.Key, customerToken: second);

        Assert.Null(onOtherApp);
        Assert.Null(afterSignOut);
        Assert.Equal(CustomerAccountErrors.SignInRequired, account.Error);
        Assert.Null(_fixture.CustomerId);
    }

    [Fact]
    public async Task A_signed_in_customer_orders_without_retyping_and_pays_part_with_a_reward()
    {
        await SignedInAsync();
        var customer = _fixture.CustomerId!.Value.Value;
        _fixture.Balances[customer] = 500;

        var quote = await _fixture.Quote.HandleAsync(new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), RewardId: _reward));
        var account = await _fixture.Account.HandleAsync(new GetCustomerAccountQuery());
        var placed = await _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), PaymentMode.Counter, ExpectedTotal: 7000m, RewardId: _reward));
        var tooExpensive = await Assert.ThrowsAsync<DomainException>(() => _fixture.Quote.HandleAsync(
            new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), RewardId: _reward)));

        Assert.Equal((7000m, 1000m), (quote.Total, quote.DiscountTotal));
        Assert.Equal(new CartRewardDto(_reward, "Boisson offerte", 300, 1000m), quote.Reward);
        Assert.Equal((500L, true), (account.Loyalty.Balance, Assert.Single(account.Loyalty.Rewards).Affordable));
        var (_, order, _, total, _) = _fixture.Orders[OrderId.From(placed.OrderId)];
        Assert.Equal((customer, (Guid?)_reward, 7000m), (order.CustomerId!.Value, order.RewardId, total));
        Assert.Empty(_fixture.CustomerRequests.Skip(1));
        Assert.Equal(200, _fixture.Balances[customer]);
        Assert.Equal(CustomerAccountErrors.RewardNotAffordable, tooExpensive.Error);
    }

    [Fact]
    public async Task A_guest_cannot_use_a_reward_and_a_signed_in_delivery_uses_the_phone_of_the_account()
    {
        _website = await WebsiteAsync();
        _fixture.DeliveryOffers[_fixture.Cotonou] = new StorefrontDeliveryOffer(15, [new StorefrontDeliveryZone(Guid.NewGuid(), "Cadjèhoun", 1000m, 0m, 20)]);

        var guest = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Quote.HandleAsync(
            new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), RewardId: _reward)));
        await SignedInAsync();
        var zone = _fixture.DeliveryOffers[_fixture.Cotonou].Zones[0].ZoneId;
        var placed = await _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, ServiceMode.Delivery, Cart(), PaymentMode.Counter, Delivery: new DeliveryAddressInput(zone, "Rue 12")));

        Assert.Equal(CustomerAccountErrors.SignInRequired, guest.Error);
        Assert.Equal(("Awa", Phone), (_fixture.Deliveries[OrderId.From(placed.OrderId)].CustomerName, _fixture.Deliveries[OrderId.From(placed.OrderId)].CustomerPhone));
    }

    [Fact]
    public async Task A_kiosk_never_signs_customers_in()
    {
        _fixture.CurrentTenantId = _fixture.TenantA;
        _fixture.Options.Add((_fixture.TenantA, SalesAppKind.Kiosk));
        var kiosk = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Borne", SalesAppKind.Kiosk));
        await _fixture.SignInAsync(kiosk.Key);
        var started = await _fixture.StartPairing.HandleAsync(new Kiosks.StartKioskPairingCommand());
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.ApprovePairing.HandleAsync(new Kiosks.ApproveKioskPairingCommand(started.Code, _fixture.Cotonou.Value, "Borne"));
        await _fixture.SignInAsync(kiosk.Key);
        var device = (await _fixture.ClaimPairing.HandleAsync(new Kiosks.ClaimKioskPairingCommand(started.PairingId, started.Secret))).DeviceToken;
        await _fixture.SignInAsync(kiosk.Key, deviceToken: device);

        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.RequestCode.HandleAsync(new RequestCustomerCodeCommand(CustomerSignInChannel.Sms, Phone)));

        Assert.Equal(CustomerSessionErrors.KioskNotAllowed, refused.Error);
        Assert.Empty(_fixture.SentCodes);
    }
}
