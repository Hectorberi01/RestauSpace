using Storefront.Application.Commands;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Application;

public sealed class StorefrontCatalogTests
{
    private readonly StorefrontFixture _fixture = new();

    private static StorefrontCatalogContent Content(decimal price, bool available = true)
    {
        var product = Guid.NewGuid();
        return new StorefrontCatalogContent(
            [new StorefrontMenuDto(Guid.NewGuid(), "Carte", [new StorefrontSectionDto(Guid.NewGuid(), "Plats", [product])])],
            [new StorefrontProductDto(product, "Poulet DG", null, price, available, [], [], ["gluten"], [new Uri("https://api.test/v1/media/abc")])],
            []);
    }

    private async Task<IssuedSalesAppKey> SignedInWebsiteAsync(params Guid[] locations)
    {
        var issued = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Site", SalesAppKind.Web, [], locations));
        await _fixture.SignInAsync(issued.Key);
        return issued;
    }

    [Fact]
    public async Task The_app_reads_the_catalog_of_its_channel_with_a_version_that_follows_the_content()
    {
        var shared = Content(3500m);
        _fixture.Catalogs[(_fixture.TenantA, _fixture.Cotonou, SalesAppKind.Web)] = shared;
        _fixture.Catalogs[(_fixture.TenantA, _fixture.Cotonou, SalesAppKind.Kiosk)] = Content(1m);
        await SignedInWebsiteAsync();

        var first = await _fixture.Catalog.HandleAsync(new GetStorefrontCatalogQuery(_fixture.Cotonou.Value));
        var same = await _fixture.Catalog.HandleAsync(new GetStorefrontCatalogQuery(_fixture.Cotonou.Value));
        _fixture.Catalogs[(_fixture.TenantA, _fixture.Cotonou, SalesAppKind.Web)] = shared with
        {
            Products = [shared.Products[0] with { IsAvailable = false }],
        };
        var changed = await _fixture.Catalog.HandleAsync(new GetStorefrontCatalogQuery(_fixture.Cotonou.Value));

        Assert.Equal(3500m, Assert.Single(first.Products).Price);
        Assert.Equal(("XOF", _fixture.Cotonou.Value), (first.CurrencyCode, first.LocationId));
        Assert.Matches("^[0-9a-f]{32}$", first.Version);
        Assert.Equal(first.Version, same.Version);
        Assert.NotEqual(first.Version, changed.Version);
    }

    [Fact]
    public async Task A_location_not_served_by_the_app_or_of_another_restaurant_is_not_found()
    {
        await SignedInWebsiteAsync(_fixture.Calavi.Value);

        var notServed = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Catalog.HandleAsync(new GetStorefrontCatalogQuery(_fixture.Cotonou.Value)));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Catalog.HandleAsync(new GetStorefrontCatalogQuery(_fixture.Paris.Value)));
        var served = await _fixture.Catalog.HandleAsync(new GetStorefrontCatalogQuery(_fixture.Calavi.Value));

        Assert.Equal(StorefrontApplicationErrors.LocationNotFound, notServed.Error);
        Assert.Equal(StorefrontApplicationErrors.LocationNotFound, foreign.Error);
        Assert.Empty(served.Products);
    }

    [Fact]
    public async Task The_catalog_needs_an_app_key()
    {
        _fixture.AppId = null;

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Catalog.HandleAsync(new GetStorefrontCatalogQuery(_fixture.Cotonou.Value)));

        Assert.Equal(StorefrontApplicationErrors.ClientRequired, refused.Error);
    }
}
