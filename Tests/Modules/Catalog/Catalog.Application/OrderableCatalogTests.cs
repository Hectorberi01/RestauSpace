using Catalog.Application.Commands;
using Catalog.Application.Queries;
using Catalog.Domain;

namespace Catalog.Application;

public sealed class OrderableCatalogTests
{
    private readonly CatalogFixture _fixture = new();

    /// <summary>Un menu publié à deux sections : un burger à variantes et options, une boisson, un dessert masqué en borne.</summary>
    private async Task<(Guid Location, Guid Burger, Guid Drink, Guid Dessert, Guid Group)> GivenPublishedMenuAsync()
    {
        var menu = await _fixture.GivenMenuAsync();
        var location = _fixture.Menus.Stored(menu).LocationId.Value;
        var burger = await _fixture.GivenProductAsync(name: "Burger", basePrice: 4000m);
        var drink = await _fixture.GivenProductAsync(name: "Bissap", basePrice: 500m);
        var dessert = await _fixture.GivenProductAsync(name: "Dégué", basePrice: 800m);
        var group = await _fixture.GivenModifierGroupAsync(min: 1, max: 2);
        await _fixture.AddModifier.HandleAsync(new AddModifierCommand(group, "Frites", 0m));
        await _fixture.AddModifier.HandleAsync(new AddModifierCommand(group, "Alloco", 300m));
        await _fixture.AddVariant.HandleAsync(new AddProductVariantCommand(burger, "Double", 1500m));
        await _fixture.AttachModifierGroup.HandleAsync(new AttachModifierGroupCommand(burger, group));
        await _fixture.SetAllergens.HandleAsync(new SetProductAllergensCommand(burger, ["milk", "gluten"]));
        await _fixture.SetHiddenChannels.HandleAsync(new SetProductHiddenChannelsCommand(dessert, [OnlineChannel.Kiosk]));
        foreach (var product in new[] { burger, drink, dessert })
        {
            await _fixture.ActivateProduct.HandleAsync(new ActivateProductCommand(product));
        }

        var plats = await _fixture.AddSection.HandleAsync(new AddMenuSectionCommand(menu, "Plats"));
        var boissons = await _fixture.AddSection.HandleAsync(new AddMenuSectionCommand(menu, "Boissons"));
        await _fixture.AddProductToSection.HandleAsync(new AddProductToMenuSectionCommand(menu, plats, burger));
        await _fixture.AddProductToSection.HandleAsync(new AddProductToMenuSectionCommand(menu, plats, dessert));
        await _fixture.AddProductToSection.HandleAsync(new AddProductToMenuSectionCommand(menu, boissons, drink));
        await _fixture.PublishMenu.HandleAsync(new PublishMenuCommand(menu));
        return (location, burger, drink, dessert, group);
    }

    [Fact]
    public async Task The_catalog_lists_each_product_and_option_group_once_with_the_ids_an_order_will_send()
    {
        var (location, burger, drink, dessert, group) = await GivenPublishedMenuAsync();

        var web = await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.Web));

        Assert.Equal("XOF", web.CurrencyCode);
        var menu = Assert.Single(web.Menus);
        Assert.Equal(["Plats", "Boissons"], menu.Sections.Select(section => section.Name));
        Assert.Equal([burger, dessert], menu.Sections[0].ProductIds);
        Assert.Equal([burger, dessert, drink], web.Products.Select(product => product.Id));
        var item = web.Products[0];
        Assert.Equal((4000m, true), (item.Price, item.IsAvailable));
        Assert.Equal(("Double", 5500m), (item.Variants[0].Name, item.Variants[0].Price));
        Assert.Equal([group], item.ModifierGroupIds);
        Assert.Equal(["gluten", "milk"], item.Allergens);
        var options = Assert.Single(web.ModifierGroups);
        Assert.Equal((1, 2), (options.MinSelections, options.MaxSelections));
        Assert.Equal(["Frites", "Alloco"], options.Modifiers.Select(modifier => modifier.Name));
    }

    [Fact]
    public async Task A_product_not_sold_here_disappears_and_a_local_shortage_shows_it_unavailable()
    {
        var (location, burger, drink, _, _) = await GivenPublishedMenuAsync();
        var elsewhere = new LocationReference(_fixture.TenantA, LocationId.New(), true);
        _fixture.Locations.Add(elsewhere);

        await _fixture.SetLocations.HandleAsync(new SetProductLocationsCommand(drink, [elsewhere.LocationId.Value]));
        await _fixture.SetAvailabilityAt.HandleAsync(new SetProductAvailabilityAtLocationCommand(burger, location, false));
        var web = await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.Web));

        Assert.DoesNotContain(web.Products, product => product.Id == drink);
        Assert.False(web.Products.Single(product => product.Id == burger).IsAvailable);
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.SetLocations.HandleAsync(new SetProductLocationsCommand(drink, [Guid.NewGuid()])));
    }

    [Fact]
    public async Task A_product_hidden_on_a_channel_disappears_there_only()
    {
        var (location, _, _, dessert, _) = await GivenPublishedMenuAsync();

        var kiosk = await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.Kiosk));
        var app = await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.MobileApp));

        Assert.DoesNotContain(kiosk.Products, product => product.Id == dessert);
        Assert.DoesNotContain(dessert, kiosk.Menus.SelectMany(menu => menu.Sections).SelectMany(section => section.ProductIds));
        Assert.Contains(app.Products, product => product.Id == dessert);
    }

    [Fact]
    public async Task An_unavailable_product_or_an_unsatisfiable_required_group_is_shown_but_not_orderable()
    {
        var (location, burger, drink, _, group) = await GivenPublishedMenuAsync();
        await _fixture.MarkUnavailable.HandleAsync(new MarkProductUnavailableCommand(drink));
        foreach (var modifier in _fixture.ModifierGroups.Stored(group).Modifiers.ToList())
        {
            await _fixture.SetModifierAvailability.HandleAsync(new SetModifierAvailabilityCommand(group, modifier.Id.Value, false));
        }

        var web = await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.Web));

        Assert.False(web.Products.Single(product => product.Id == drink).IsAvailable);
        var item = web.Products.Single(product => product.Id == burger);
        Assert.False(item.IsAvailable);
        Assert.All(item.Variants, variant => Assert.False(variant.IsAvailable));
    }

    [Fact]
    public async Task Another_tenant_or_an_unpublished_location_has_an_empty_catalog()
    {
        var (location, _, _, _, _) = await GivenPublishedMenuAsync();
        var empty = _fixture.GivenLocation();

        _fixture.CurrentTenantId = _fixture.TenantB;
        var foreign = await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.Web));
        _fixture.CurrentTenantId = _fixture.TenantA;
        var unpublished = await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(empty, OnlineChannel.Web));

        Assert.Empty(foreign.Menus);
        Assert.Empty(foreign.Products);
        Assert.Null(foreign.CurrencyCode);
        Assert.Empty(unpublished.Menus);
    }

    [Fact]
    public async Task Allergens_and_channels_are_edited_through_the_product()
    {
        var product = await _fixture.GivenProductAsync();

        await _fixture.SetAllergens.HandleAsync(new SetProductAllergensCommand(product, ["sesame", "EGGS"]));
        await _fixture.SetHiddenChannels.HandleAsync(new SetProductHiddenChannelsCommand(product, [OnlineChannel.Web]));
        var unknown = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.SetAllergens.HandleAsync(new SetProductAllergensCommand(product, ["chocolate"])));
        var dto = await _fixture.GetProduct.HandleAsync(new GetProductByIdQuery(product));

        Assert.Equal(["eggs", "sesame"], dto.Allergens);
        Assert.Equal([OnlineChannel.Web], dto.HiddenChannels);
        Assert.Equal(ProductErrors.AllergenUnknown, unknown.Error);
    }

    [Fact]
    public async Task Each_location_and_channel_sees_its_own_price_and_the_variants_follow()
    {
        var (location, burger, _, _, _) = await GivenPublishedMenuAsync();
        await _fixture.SetPriceOverride.HandleAsync(new SetProductPriceOverrideCommand(burger, null, PriceChannel.Kiosk, 4500m));
        await _fixture.SetPriceOverride.HandleAsync(new SetProductPriceOverrideCommand(burger, location, PriceChannel.Kiosk, 4200m));
        await _fixture.SetPriceOverride.HandleAsync(new SetProductPriceOverrideCommand(burger, location, null, 3800m));
        var unknownLocation = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.SetPriceOverride.HandleAsync(new SetProductPriceOverrideCommand(burger, Guid.NewGuid(), null, 3000m)));
        var noTarget = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.SetPriceOverride.HandleAsync(new SetProductPriceOverrideCommand(burger, null, null, 3000m)));
        var variantBelowZero = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.SetPriceOverride.HandleAsync(new SetProductPriceOverrideCommand(burger, null, PriceChannel.Web, -1m)));

        var kiosk = (await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.Kiosk))).Products.Single(p => p.Id == burger);
        var web = (await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.Web))).Products.Single(p => p.Id == burger);
        await _fixture.RemovePriceOverride.HandleAsync(new RemoveProductPriceOverrideCommand(burger, location, PriceChannel.Kiosk));
        var kioskAfter = (await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.Kiosk))).Products.Single(p => p.Id == burger);
        var product = await _fixture.GetProduct.HandleAsync(new GetProductByIdQuery(burger));

        Assert.Equal((4200m, 5700m), (kiosk.Price, kiosk.Variants.Single().Price));
        Assert.Equal(3800m, web.Price);
        Assert.Equal(3800m, kioskAfter.Price);
        Assert.Equal(4000m, product.BasePrice);
        Assert.Equal(2, product.PriceOverrides!.Count);
        Assert.Equal(CatalogApplicationErrors.LocationNotFound, unknownLocation.Error);
        Assert.Equal(ProductErrors.PriceOverrideTargetRequired, noTarget.Error);
        Assert.Equal(ProductErrors.NegativePrice, variantBelowZero.Error);
    }

    [Fact]
    public async Task A_menu_with_time_slots_is_offered_only_during_them_even_past_midnight()
    {
        var (location, _, _, _, _) = await GivenPublishedMenuAsync();
        var menu = _fixture.Menus.Items.Single().Id.Value;
        // Jeudi 15 janvier 2026, 12:00 UTC (fuseau inconnu de l'établissement : UTC).
        await _fixture.SetMenuSchedule.HandleAsync(new SetMenuScheduleCommand(menu, [
            new MenuSlotInput(DayOfWeek.Thursday, "11:30", "14:30"),
            new MenuSlotInput(DayOfWeek.Thursday, "22:00", "01:00"),
        ]));
        var invalid = await Assert.ThrowsAsync<DomainException>(() => _fixture.SetMenuSchedule.HandleAsync(
            new SetMenuScheduleCommand(menu, [new MenuSlotInput(DayOfWeek.Friday, "25:00", "26:00")])));

        var lunch = await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.Web));
        _fixture.Clock.Advance(TimeSpan.FromHours(4));
        var afternoon = await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.Web));
        _fixture.Clock.Advance(TimeSpan.FromHours(8.5));
        var afterMidnight = await _fixture.OrderableCatalog.HandleAsync(new GetOrderableCatalogQuery(location, OnlineChannel.Web));
        var dto = await _fixture.GetMenu.HandleAsync(new GetMenuByIdQuery(menu));

        Assert.Single(lunch.Menus);
        Assert.Empty(afternoon.Menus);
        Assert.Single(afterMidnight.Menus);
        Assert.Equal(MenuErrors.ScheduleInvalid, invalid.Error);
        Assert.Equal([new MenuSlotDto(DayOfWeek.Thursday, "11:30", "14:30"), new MenuSlotDto(DayOfWeek.Thursday, "22:00", "01:00")], dto.Slots);
    }
}
