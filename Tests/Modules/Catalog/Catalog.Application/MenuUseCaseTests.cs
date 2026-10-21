using Catalog.Application.Commands;
using Catalog.Application.Queries;
using Catalog.Domain;

namespace Catalog.Application;

public sealed class MenuUseCaseTests
{
    private readonly CatalogFixture _fixture = new();

    /// <summary>Menu à une section listant un produit actif.</summary>
    private async Task<(Guid Menu, Guid Section, Guid Product)> GivenReadyMenuAsync()
    {
        var menu = await _fixture.GivenMenuAsync();
        var product = await _fixture.GivenProductAsync();
        await _fixture.ActivateProduct.HandleAsync(new ActivateProductCommand(product));
        var section = await _fixture.AddSection.HandleAsync(new AddMenuSectionCommand(menu, "Plats"));
        await _fixture.AddProductToSection.HandleAsync(new AddProductToMenuSectionCommand(menu, section, product));
        return (menu, section, product);
    }

    [Fact]
    public async Task A_menu_is_created_in_draft_for_a_location_of_the_current_tenant()
    {
        var location = _fixture.GivenLocation();

        var id = await _fixture.CreateMenu.HandleAsync(new CreateMenuCommand(location, "Carte du midi"));

        var menu = _fixture.Menus.Stored(id);
        Assert.Equal(_fixture.TenantA, menu.TenantId);
        Assert.Equal(location, menu.LocationId.Value);
        Assert.Equal(MenuStatus.Draft, menu.Status);
        Assert.Equal(1, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_location_of_another_tenant_or_unknown_is_not_found()
    {
        var ofB = _fixture.GivenLocation(_fixture.TenantB);

        var foreign = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.CreateMenu.HandleAsync(new CreateMenuCommand(ofB, "Carte")));
        var empty = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.CreateMenu.HandleAsync(new CreateMenuCommand(Guid.Empty, "Carte")));

        Assert.Equal(CatalogApplicationErrors.LocationNotFound, foreign.Error);
        Assert.Equal(CatalogApplicationErrors.LocationNotFound, empty.Error);
        Assert.Empty(_fixture.Menus.Items);
    }

    [Fact]
    public async Task A_product_of_another_tenant_cannot_be_listed()
    {
        var menu = await _fixture.GivenMenuAsync();
        var section = await _fixture.AddSection.HandleAsync(new AddMenuSectionCommand(menu, "Plats"));
        var ofB = await _fixture.GivenProductAsync(_fixture.TenantB, basePrice: 10m);

        var refused = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.AddProductToSection.HandleAsync(new AddProductToMenuSectionCommand(menu, section, ofB)));

        Assert.Equal(CatalogApplicationErrors.ProductNotFound, refused.Error);
        Assert.True(Assert.Single(_fixture.Menus.Stored(menu).Sections).IsEmpty);
    }

    [Fact]
    public async Task A_ready_menu_is_published_and_then_read_only()
    {
        var (menu, _, product) = await GivenReadyMenuAsync();

        await _fixture.PublishMenu.HandleAsync(new PublishMenuCommand(menu));
        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.RenameMenu.HandleAsync(new RenameMenuCommand(menu, "Autre")));

        var read = await _fixture.GetMenu.HandleAsync(new GetMenuByIdQuery(menu));
        Assert.Equal(MenuStatus.Published, read.Status);
        Assert.Equal(_fixture.Clock.UtcNow, read.PublishedAtUtc);
        Assert.Equal(product, Assert.Single(Assert.Single(read.Sections).ProductIds));
        Assert.Equal(MenuErrors.PublishedIsReadOnly, refused.Error);
        DomainAssert.Raised<MenuPublished>(_fixture.Menus.Stored(menu));
    }

    [Fact]
    public async Task Publishing_sees_a_product_archived_since_it_was_listed()
    {
        var (menu, _, product) = await GivenReadyMenuAsync();
        await _fixture.ArchiveProduct.HandleAsync(new ArchiveProductCommand(product));
        var savesBefore = _fixture.SaveCount;

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.PublishMenu.HandleAsync(new PublishMenuCommand(menu)));

        Assert.Equal(MenuErrors.ProductArchived, refused.Error);
        Assert.Equal(savesBefore, _fixture.SaveCount);
    }

    [Fact]
    public async Task Publishing_refuses_a_listed_product_whose_required_options_cannot_be_chosen()
    {
        var (menu, _, product) = await GivenReadyMenuAsync();
        var cooking = await _fixture.GivenModifierGroupAsync(min: 1, max: 1);
        await _fixture.AttachModifierGroup.HandleAsync(new AttachModifierGroupCommand(product, cooking));
        var savesBefore = _fixture.SaveCount;

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.PublishMenu.HandleAsync(new PublishMenuCommand(menu)));

        Assert.Equal(ModifierGroupErrors.RuleNotSatisfiable, refused.Error);
        Assert.Equal(savesBefore, _fixture.SaveCount);

        await _fixture.AddModifier.HandleAsync(new AddModifierCommand(cooking, "À point", 0m));
        await _fixture.PublishMenu.HandleAsync(new PublishMenuCommand(menu));
        Assert.Equal(MenuStatus.Published, _fixture.Menus.Stored(menu).Status);
    }

    [Fact]
    public async Task Sections_keep_the_order_they_are_moved_to()
    {
        var menu = await _fixture.GivenMenuAsync();
        var starters = await _fixture.AddSection.HandleAsync(new AddMenuSectionCommand(menu, "Entrées"));
        var desserts = await _fixture.AddSection.HandleAsync(new AddMenuSectionCommand(menu, "Desserts"));

        await _fixture.MoveSection.HandleAsync(new MoveMenuSectionCommand(menu, desserts, 0));

        var read = await _fixture.GetMenu.HandleAsync(new GetMenuByIdQuery(menu));
        Assert.Equal(new[] { desserts, starters }, read.Sections.Select(section => section.Id).ToArray());
    }

    [Fact]
    public async Task Menus_are_listed_for_the_current_tenant_optionally_by_location()
    {
        var first = await _fixture.GivenMenuAsync();
        await _fixture.GivenMenuAsync();
        await _fixture.GivenMenuAsync(_fixture.TenantB);
        var location = _fixture.Menus.Stored(first).LocationId.Value;

        var all = await _fixture.ListMenus.HandleAsync(new ListMenusQuery());
        var atLocation = await _fixture.ListMenus.HandleAsync(new ListMenusQuery(location));
        var none = await _fixture.ListMenus.HandleAsync(new ListMenusQuery(Guid.Empty));

        Assert.Equal(2, all.Count);
        Assert.Equal(first, Assert.Single(atLocation).Id);
        Assert.Empty(none);
    }

    [Fact]
    public async Task A_menu_of_another_tenant_is_not_found()
    {
        var ofB = await _fixture.GivenMenuAsync(_fixture.TenantB);

        var read = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetMenu.HandleAsync(new GetMenuByIdQuery(ofB)));
        var publish = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.PublishMenu.HandleAsync(new PublishMenuCommand(ofB)));

        Assert.Equal(CatalogApplicationErrors.MenuNotFound, read.Error);
        Assert.Equal(CatalogApplicationErrors.MenuNotFound, publish.Error);
    }
}
