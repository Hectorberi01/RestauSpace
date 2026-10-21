using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;
using Storefront.Application.Commands;
using Storefront.Domain;

namespace Storefront.Application;

/// <summary>Commande à table par QR code : sans coordonnées, rattachée à la session de la table, sur place seulement.</summary>
public sealed class TableOrderingTests
{
    private readonly StorefrontFixture _fixture = new();
    private readonly Guid _burger = Guid.NewGuid();
    private const string Token = "Qm9uam91ci1sYS10YWJsZQ";

    public TableOrderingTests()
    {
        _fixture.Prices[_burger] = 2500m;
    }

    private async Task GivenSiteAsync()
    {
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.SaveSettings.HandleAsync(new SaveOnlineOrderingSettingsCommand(
            _fixture.Cotonou.Value, false, [ServiceMode.Takeaway, ServiceMode.DineIn], 20, null, 10, 15,
            [new ChannelPolicyInput(SalesAppKind.Web, true, true, null, OrderAcceptance.Automatic)]));
        _fixture.Options.Add((_fixture.TenantA, SalesAppKind.Web));
        var issued = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Site", SalesAppKind.Web));
        await _fixture.SignInAsync(issued.Key);
        _fixture.Tables[Token] = (_fixture.TenantA, new StorefrontTable(Guid.NewGuid(), _fixture.Cotonou, "T4", "Terrasse", true));
    }

    private Task<GuestOrderPlacedDto> OrderAsync(ServiceMode mode = ServiceMode.DineIn, string token = Token, GuestContactInput? customer = null) =>
        _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, mode, [new CartLine(_burger, 2)], PaymentMode.Counter, customer, TableToken: token));

    [Fact]
    public async Task A_guest_orders_from_the_table_without_contact_and_the_order_joins_the_table_session()
    {
        await GivenSiteAsync();

        var table = await new GetStorefrontTableHandler(_fixture.Workspace, _fixture, _fixture).HandleAsync(new GetStorefrontTableQuery(Token));
        var first = await OrderAsync();
        var second = await OrderAsync();

        Assert.Equal(("T4", "Terrasse"), (table.Label, table.AreaName));
        var placed = _fixture.Orders.Values.Select(order => order.Order).ToList();
        Assert.Equal(2, placed.Count);
        Assert.All(placed, order => Assert.Equal(_fixture.SeatedTables.Values.Single(), order.TableSessionId!.Value));
        Assert.All(placed, order => Assert.Null(order.CustomerId));
        Assert.NotEqual(first.OrderId, second.OrderId);
    }

    [Fact]
    public async Task A_table_qr_only_works_on_site_now_and_an_unknown_token_finds_nothing()
    {
        await GivenSiteAsync();

        var takeaway = await Assert.ThrowsAsync<DomainException>(() => OrderAsync(ServiceMode.Takeaway));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => OrderAsync(token: "Zm9yZ2VkLXRva2VuLXZhbHVl"));
        var lookup = await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetStorefrontTableHandler(_fixture.Workspace, _fixture, _fixture).HandleAsync(new GetStorefrontTableQuery("nope")));

        Assert.Equal(TableCheckoutErrors.DineInOnly, takeaway.Error);
        Assert.Equal(TableCheckoutErrors.TableNotFound, unknown.Error);
        Assert.Equal(TableCheckoutErrors.TableNotFound, lookup.Error);
        Assert.Empty(_fixture.Orders);
    }

    [Fact]
    public async Task A_table_of_a_location_the_app_does_not_serve_finds_nothing()
    {
        await GivenSiteAsync();
        var other = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Site Calavi", SalesAppKind.Web, LocationIds: [_fixture.Calavi.Value]));
        await _fixture.SignInAsync(other.Key);

        var lookup = await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetStorefrontTableHandler(_fixture.Workspace, _fixture, _fixture).HandleAsync(new GetStorefrontTableQuery(Token)));

        Assert.Equal(TableCheckoutErrors.TableNotFound, lookup.Error);
    }
}
