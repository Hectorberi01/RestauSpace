using Storefront.Application.Abstractions;
using Storefront.Application.Commands;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Application;

/// <summary>Ce qu'un site affiche autour de la commande : présentation du restaurant, plan d'accès, jours spéciaux, avis.</summary>
public sealed class StorefrontPresentationTests
{
    private readonly StorefrontFixture _fixture = new();
    private readonly FakeReviews _reviews = new();

    private Task<IssuedSalesAppKey> WebsiteAsync(params Guid[] locations) =>
        _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand(
            "Site de Chez Awa", SalesAppKind.Web, ["https://chezawa.bj"], locations, ["https://chezawa.bj/merci"]));

    private GetStorefrontReviewsHandler Reviews => new(_fixture.Workspace, _fixture, _reviews, _fixture.Clock);

    [Fact]
    public async Task The_restaurant_presentation_and_the_location_place_and_special_days_are_public()
    {
        _fixture.Restaurants[_fixture.TenantA] = _fixture.Restaurants[_fixture.TenantA] with
        {
            Profile = new StorefrontProfile("Cuisine de quartier", "Depuis 2012.", "https://cdn.example/cover.webp",
                [new StorefrontLink("Instagram", "https://instagram.com/chezawa")]),
        };
        var index = _fixture.Locations.FindIndex(entry => entry.Location.LocationId == _fixture.Cotonou);
        _fixture.Locations[index] = (_fixture.TenantA, _fixture.Locations[index].Location with
        {
            TemporaryClosureReason = null,
            Place = new StorefrontPlace("01 BP 1", "BJ", 6.3655m, 2.4184m, "+22997001122", null, "contact@chezawa.bj"),
            SpecialDays = [new StorefrontSpecialDay(new DateOnly(2026, 12, 25), true, [], "Noël")],
        });
        var issued = await WebsiteAsync(_fixture.Cotonou.Value);
        await _fixture.SignInAsync(issued.Key, "https://chezawa.bj");

        var storefront = await _fixture.Storefront.HandleAsync(new GetStorefrontQuery());

        Assert.Equal(("Cuisine de quartier", "https://cdn.example/cover.webp"), (storefront.Restaurant.Tagline, storefront.Restaurant.CoverImageUrl));
        Assert.Equal("Instagram", Assert.Single(storefront.Restaurant.Links!).Network);
        var cotonou = Assert.Single(storefront.Locations);
        Assert.Equal(new StorefrontCoordinatesDto(6.3655m, 2.4184m), cotonou.Place!.Coordinates);
        Assert.Equal(("+22997001122", null, "contact@chezawa.bj"), (cotonou.Place.Contact.Phone, cotonou.Place.Contact.WhatsApp, cotonou.Place.Contact.Email));
        Assert.Equal("Noël", Assert.Single(cotonou.SpecialDays!).Reason);
    }

    [Fact]
    public async Task A_location_without_position_has_no_coordinates()
    {
        var index = _fixture.Locations.FindIndex(entry => entry.Location.LocationId == _fixture.Cotonou);
        _fixture.Locations[index] = (_fixture.TenantA, _fixture.Locations[index].Location with
        {
            Place = new StorefrontPlace(null, "BJ", null, null, null, null, null),
        });
        var issued = await WebsiteAsync(_fixture.Cotonou.Value);
        await _fixture.SignInAsync(issued.Key, "https://chezawa.bj");

        var cotonou = await _fixture.StorefrontLocation.HandleAsync(new GetStorefrontLocationQuery(_fixture.Cotonou.Value));

        Assert.Null(cotonou.Place!.Coordinates);
        Assert.Equal("BJ", cotonou.Place.CountryCode);
    }

    [Fact]
    public async Task Reviews_are_read_for_the_served_locations_only()
    {
        var issued = await WebsiteAsync(_fixture.Cotonou.Value);
        await _fixture.SignInAsync(issued.Key, "https://chezawa.bj");

        await Reviews.HandleAsync(new GetStorefrontReviewsQuery(null, 5));
        var all = _reviews.LastLocations;
        await Reviews.HandleAsync(new GetStorefrontReviewsQuery(_fixture.Cotonou.Value));
        var one = _reviews.LastLocations;
        var calavi = await Assert.ThrowsAsync<NotFoundException>(() => Reviews.HandleAsync(new GetStorefrontReviewsQuery(_fixture.Calavi.Value)));
        var paris = await Assert.ThrowsAsync<NotFoundException>(() => Reviews.HandleAsync(new GetStorefrontReviewsQuery(_fixture.Paris.Value)));

        Assert.Equal([_fixture.Cotonou], all);
        Assert.Equal([_fixture.Cotonou], one);
        Assert.Equal(StorefrontApplicationErrors.LocationNotFound, calavi.Error);
        Assert.Equal(StorefrontApplicationErrors.LocationNotFound, paris.Error);
        Assert.Equal(_fixture.TenantA, _reviews.LastTenant);
    }

    [Fact]
    public async Task An_app_serving_every_location_reads_all_reviews_and_take_is_bounded()
    {
        var issued = await WebsiteAsync();
        await _fixture.SignInAsync(issued.Key, "https://chezawa.bj");

        await Reviews.HandleAsync(new GetStorefrontReviewsQuery(null));
        var tooMany = await Assert.ThrowsAsync<DomainException>(() => Reviews.HandleAsync(new GetStorefrontReviewsQuery(null, 51)));

        Assert.Null(_reviews.LastLocations);
        Assert.Equal(GetStorefrontReviewsHandler.TakeInvalid, tooMany.Error);
    }

    private sealed class FakeReviews : IStorefrontReviews
    {
        public TenantId? LastTenant { get; private set; }

        public IReadOnlyList<LocationId>? LastLocations { get; private set; }

        public Task<StorefrontReviews> GetAsync(TenantId tenantId, IReadOnlyList<LocationId>? locations, int take, CancellationToken cancellationToken = default)
        {
            LastTenant = tenantId;
            LastLocations = locations;
            return Task.FromResult(new StorefrontReviews(0, null, [], []));
        }

        public Task<Guid> SubmitAsync(TenantId tenantId, LocationId locationId, OrderId orderId, int rating, string? comment, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.NewGuid());
    }
}
