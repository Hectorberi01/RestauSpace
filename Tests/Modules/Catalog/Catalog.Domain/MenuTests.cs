namespace Catalog.Domain;

public class MenuTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly LocationId Location = LocationId.New();
    private readonly FakeClock _clock = new();

    private static CatalogName Name(string value) => CatalogName.Create(value);

    private static ProductReference ProductRef(TenantId? tenant = null, bool isArchived = false) =>
        CatalogRefs.ProductRef(tenant ?? Tenant, isArchived);

    private static LocationReference LocationRef(TenantId? tenant = null) => CatalogRefs.Location(tenant ?? Tenant, Location);

    private static Menu NewMenu() => Menu.Create(Tenant, LocationRef(), Name("Lunch"));

    private static (Menu Menu, MenuSectionId SectionId, ProductReference Product) MenuWithOneProduct()
    {
        var menu = NewMenu();
        var sectionId = menu.AddSection(Name("Mains"));
        var product = ProductRef();
        menu.AddProductToSection(sectionId, product);
        return (menu, sectionId, product);
    }

    private Menu PublishedMenu()
    {
        var (menu, _, product) = MenuWithOneProduct();
        menu.Publish([product], _clock);
        menu.ClearDomainEvents();
        return menu;
    }

    private static MenuSection Section(Menu menu, MenuSectionId id) => Assert.Single(menu.Sections, s => s.Id == id);

    // ---- Create ----

    [Fact]
    public void Create_starts_as_an_empty_draft()
    {
        var menu = NewMenu();

        Assert.Equal(Tenant, menu.TenantId);
        Assert.Equal(Location, menu.LocationId);
        Assert.Equal("Lunch", menu.Name.Value);
        Assert.Equal(MenuStatus.Draft, menu.Status);
        Assert.Null(menu.PublishedAtUtc);
        Assert.Empty(menu.Sections);
        Assert.False(menu.CanBePublished);
        DomainAssert.NoEvents(menu);
    }

    [Fact]
    public void Create_rejects_empty_tenant_or_location()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => Menu.Create(default, LocationRef(), Name("Lunch")));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(Tenant, default, true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(default, Location, true));
    }

    [Fact]
    public void Create_rejects_a_location_of_another_tenant() =>
        DomainAssert.Throws(CommonErrors.TenantMismatch, () => Menu.Create(Tenant, LocationRef(TenantId.New()), Name("Lunch")));

    [Fact]
    public void Create_accepts_a_location_that_is_not_operational()
    {
        var menu = Menu.Create(Tenant, new LocationReference(Tenant, Location, IsOperational: false), Name("Lunch"));

        Assert.Equal(Location, menu.LocationId);
    }

    // ---- Sections ----

    [Fact]
    public void AddSection_appends_sections_in_order()
    {
        var menu = NewMenu();

        var starters = menu.AddSection(Name("Starters"));
        var mains = menu.AddSection(Name("Mains"));

        Assert.Equal([starters, mains], menu.Sections.Select(s => s.Id));
        Assert.All(menu.Sections, s => Assert.True(s.IsEmpty));
    }

    [Fact]
    public void AddSection_rejects_a_duplicate_name_ignoring_case()
    {
        var menu = NewMenu();
        menu.AddSection(Name("Mains"));

        DomainAssert.Throws(MenuErrors.SectionNameDuplicate, () => menu.AddSection(Name("MAINS")));

        Assert.Single(menu.Sections);
    }

    [Fact]
    public void RenameSection_keeps_names_unique_but_allows_recasing_itself()
    {
        var menu = NewMenu();
        var starters = menu.AddSection(Name("Starters"));
        menu.AddSection(Name("Mains"));

        DomainAssert.Throws(MenuErrors.SectionNameDuplicate, () => menu.RenameSection(starters, Name("mains")));
        DomainAssert.Throws(MenuErrors.SectionNotFound, () => menu.RenameSection(MenuSectionId.New(), Name("Other")));

        menu.RenameSection(starters, Name("STARTERS"));
        Assert.Equal("STARTERS", Section(menu, starters).Name.Value);
    }

    [Fact]
    public void RemoveSection_removes_it_and_rejects_an_unknown_section()
    {
        var menu = NewMenu();
        var starters = menu.AddSection(Name("Starters"));

        menu.RemoveSection(starters);

        Assert.Empty(menu.Sections);
        DomainAssert.Throws(MenuErrors.SectionNotFound, () => menu.RemoveSection(starters));
    }

    [Fact]
    public void MoveSection_reorders_sections_and_rejects_an_out_of_range_position()
    {
        var menu = NewMenu();
        var a = menu.AddSection(Name("A"));
        var b = menu.AddSection(Name("B"));
        var c = menu.AddSection(Name("C"));

        menu.MoveSection(c, 0);
        Assert.Equal([c, a, b], menu.Sections.Select(s => s.Id));

        menu.MoveSection(c, 2);
        Assert.Equal([a, b, c], menu.Sections.Select(s => s.Id));

        DomainAssert.Throws(MenuErrors.PositionOutOfRange, () => menu.MoveSection(a, 3));
        DomainAssert.Throws(MenuErrors.PositionOutOfRange, () => menu.MoveSection(a, -1));
        DomainAssert.Throws(MenuErrors.SectionNotFound, () => menu.MoveSection(MenuSectionId.New(), 0));
        Assert.Equal([a, b, c], menu.Sections.Select(s => s.Id));
    }

    // ---- Products ----

    [Fact]
    public void AddProductToSection_appends_products_in_order()
    {
        var menu = NewMenu();
        var sectionId = menu.AddSection(Name("Mains"));
        var first = ProductRef();
        var second = ProductRef();

        menu.AddProductToSection(sectionId, first);
        menu.AddProductToSection(sectionId, second);

        Assert.Equal([first.Id, second.Id], Section(menu, sectionId).ProductIds);
    }

    [Fact]
    public void AddProductToSection_rejects_a_product_already_in_the_section()
    {
        var (menu, sectionId, product) = MenuWithOneProduct();

        DomainAssert.Throws(MenuErrors.ProductAlreadyInSection, () => menu.AddProductToSection(sectionId, product));

        Assert.Single(Section(menu, sectionId).ProductIds);
    }

    [Fact]
    public void AddProductToSection_allows_the_same_product_in_two_sections()
    {
        var (menu, _, product) = MenuWithOneProduct();
        var specials = menu.AddSection(Name("Specials"));

        menu.AddProductToSection(specials, product);

        Assert.Equal([product.Id], Section(menu, specials).ProductIds);
    }

    [Fact]
    public void AddProductToSection_rejects_a_product_of_another_tenant()
    {
        var menu = NewMenu();
        var sectionId = menu.AddSection(Name("Mains"));

        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => menu.AddProductToSection(sectionId, ProductRef(tenant: TenantId.New())));

        Assert.True(Section(menu, sectionId).IsEmpty);
    }

    [Fact]
    public void AddProductToSection_rejects_an_archived_product_and_an_unknown_section()
    {
        var menu = NewMenu();
        var sectionId = menu.AddSection(Name("Mains"));

        DomainAssert.Throws(MenuErrors.ProductArchived, () => menu.AddProductToSection(sectionId, ProductRef(isArchived: true)));
        DomainAssert.Throws(MenuErrors.SectionNotFound, () => menu.AddProductToSection(MenuSectionId.New(), ProductRef()));

        Assert.True(Section(menu, sectionId).IsEmpty);
    }

    [Fact]
    public void RemoveProductFromSection_removes_it_and_rejects_a_product_that_is_not_listed()
    {
        var (menu, sectionId, product) = MenuWithOneProduct();

        menu.RemoveProductFromSection(sectionId, product.Id);

        Assert.True(Section(menu, sectionId).IsEmpty);
        DomainAssert.Throws(MenuErrors.ProductNotInSection, () => menu.RemoveProductFromSection(sectionId, product.Id));
        DomainAssert.Throws(MenuErrors.SectionNotFound, () => menu.RemoveProductFromSection(MenuSectionId.New(), product.Id));
    }

    [Fact]
    public void MoveProductInSection_reorders_products_and_rejects_invalid_moves()
    {
        var (menu, sectionId, first) = MenuWithOneProduct();
        var second = ProductRef();
        var third = ProductRef();
        menu.AddProductToSection(sectionId, second);
        menu.AddProductToSection(sectionId, third);

        menu.MoveProductInSection(sectionId, third.Id, 0);
        Assert.Equal([third.Id, first.Id, second.Id], Section(menu, sectionId).ProductIds);

        DomainAssert.Throws(MenuErrors.PositionOutOfRange, () => menu.MoveProductInSection(sectionId, first.Id, 3));
        DomainAssert.Throws(MenuErrors.PositionOutOfRange, () => menu.MoveProductInSection(sectionId, first.Id, -1));
        DomainAssert.Throws(MenuErrors.ProductNotInSection, () => menu.MoveProductInSection(sectionId, ProductId.New(), 0));
        DomainAssert.Throws(MenuErrors.SectionNotFound, () => menu.MoveProductInSection(MenuSectionId.New(), first.Id, 0));
        Assert.Equal([third.Id, first.Id, second.Id], Section(menu, sectionId).ProductIds);
    }

    // ---- Publish ----

    [Fact]
    public void Publish_rejects_a_menu_without_section()
    {
        var menu = NewMenu();

        DomainAssert.Throws(MenuErrors.NoSection, () => menu.Publish([], _clock));

        Assert.Equal(MenuStatus.Draft, menu.Status);
        DomainAssert.NoEvents(menu);
    }

    [Fact]
    public void Publish_rejects_a_menu_with_an_empty_section()
    {
        var (menu, _, product) = MenuWithOneProduct();
        menu.AddSection(Name("Desserts"));

        Assert.False(menu.CanBePublished);
        DomainAssert.Throws(MenuErrors.EmptySection, () => menu.Publish([product], _clock));

        Assert.Equal(MenuStatus.Draft, menu.Status);
        DomainAssert.NotRaised<MenuPublished>(menu);
    }

    [Fact]
    public void Publish_moves_a_valid_draft_to_published_and_raises_MenuPublished()
    {
        var (menu, _, product) = MenuWithOneProduct();
        Assert.True(menu.CanBePublished);

        menu.Publish([product], _clock);

        Assert.Equal(MenuStatus.Published, menu.Status);
        Assert.Equal(_clock.UtcNow, menu.PublishedAtUtc);
        Assert.False(menu.CanBePublished);
        var published = DomainAssert.Raised<MenuPublished>(menu);
        Assert.Equal(Tenant, published.TenantId);
        Assert.Equal(menu.Id, published.MenuId);
        Assert.Equal(Location, published.LocationId);
        Assert.Equal(_clock.UtcNow, published.OccurredAtUtc);
    }

    [Fact]
    public void Publish_rejects_an_already_published_menu()
    {
        var menu = PublishedMenu();

        DomainAssert.Throws(MenuErrors.NotDraft, () => menu.Publish([], _clock));

        DomainAssert.NoEvents(menu);
    }

    [Fact]
    public void Published_menu_content_is_read_only()
    {
        var (menu, sectionId, product) = MenuWithOneProduct();
        menu.Publish([product], _clock);
        menu.ClearDomainEvents();

        DomainAssert.Throws(MenuErrors.PublishedIsReadOnly, () => menu.Rename(Name("Dinner")));
        DomainAssert.Throws(MenuErrors.PublishedIsReadOnly, () => menu.AddSection(Name("Desserts")));
        DomainAssert.Throws(MenuErrors.PublishedIsReadOnly, () => menu.RenameSection(sectionId, Name("Plats")));
        DomainAssert.Throws(MenuErrors.PublishedIsReadOnly, () => menu.RemoveSection(sectionId));
        DomainAssert.Throws(MenuErrors.PublishedIsReadOnly, () => menu.MoveSection(sectionId, 0));
        DomainAssert.Throws(MenuErrors.PublishedIsReadOnly, () => menu.AddProductToSection(sectionId, ProductRef()));
        DomainAssert.Throws(MenuErrors.PublishedIsReadOnly, () => menu.RemoveProductFromSection(sectionId, product.Id));
        DomainAssert.Throws(MenuErrors.PublishedIsReadOnly, () => menu.MoveProductInSection(sectionId, product.Id, 0));

        Assert.Equal("Lunch", menu.Name.Value);
        Assert.Equal([product.Id], Section(menu, sectionId).ProductIds);
        DomainAssert.NoEvents(menu);
    }

    [Fact]
    public void Publish_rejects_a_listed_product_archived_since_it_was_added()
    {
        var menu = NewMenu();
        var sectionId = menu.AddSection(Name("Mains"));
        var kept = CatalogRefs.Product(Tenant);
        var retired = CatalogRefs.Product(Tenant, "Old burger");
        menu.AddProductToSection(sectionId, kept.ToReference());
        menu.AddProductToSection(sectionId, retired.ToReference());
        retired.Archive(_clock);

        Assert.True(menu.CanBePublished);
        DomainAssert.Throws(MenuErrors.ProductArchived, () => menu.Publish([kept.ToReference(), retired.ToReference()], _clock));

        Assert.Equal(MenuStatus.Draft, menu.Status);
        Assert.Null(menu.PublishedAtUtc);
        DomainAssert.NoEvents(menu);

        menu.RemoveProductFromSection(sectionId, retired.Id);
        menu.Publish([kept.ToReference()], _clock);
        Assert.Equal(MenuStatus.Published, menu.Status);
    }

    [Fact]
    public void Publish_rejects_a_listed_product_without_current_reference()
    {
        var (menu, sectionId, first) = MenuWithOneProduct();
        var second = ProductRef();
        menu.AddProductToSection(sectionId, second);

        DomainAssert.Throws(MenuErrors.ProductReferenceMissing, () => menu.Publish([first], _clock));
        DomainAssert.Throws(MenuErrors.ProductReferenceMissing, () => menu.Publish([], _clock));

        Assert.Equal(MenuStatus.Draft, menu.Status);
        DomainAssert.NoEvents(menu);
    }

    [Fact]
    public void Publish_ignores_references_of_products_the_menu_does_not_list_and_rejects_a_null_one()
    {
        var (menu, _, product) = MenuWithOneProduct();

        Assert.Throws<ArgumentNullException>(() => menu.Publish([product, null!], _clock));
        Assert.Throws<ArgumentNullException>(() => menu.Publish(null!, _clock));

        menu.Publish([product, ProductRef(isArchived: true), ProductRef(tenant: TenantId.New())], _clock);

        Assert.Equal(MenuStatus.Published, menu.Status);
    }

    [Fact]
    public void Sections_and_products_per_section_are_bounded()
    {
        var menu = NewMenu();
        var sectionId = menu.AddSection(Name("Section 1"));
        for (var i = 2; i <= Menu.MaxSections; i++)
        {
            menu.AddSection(Name($"Section {i}"));
        }

        DomainAssert.Throws(MenuErrors.TooManySections, () => menu.AddSection(Name("One too many")));
        Assert.Equal(Menu.MaxSections, menu.Sections.Count);

        for (var i = 0; i < Menu.MaxProductsPerSection; i++)
        {
            menu.AddProductToSection(sectionId, ProductRef());
        }

        DomainAssert.Throws(MenuErrors.TooManyProductsInSection, () => menu.AddProductToSection(sectionId, ProductRef()));
        Assert.Equal(Menu.MaxProductsPerSection, Section(menu, sectionId).ProductIds.Count);
    }

    // ---- Unpublish ----

    [Fact]
    public void Unpublish_moves_back_to_draft_and_raises_MenuUnpublished()
    {
        var menu = PublishedMenu();

        menu.Unpublish(_clock);

        Assert.Equal(MenuStatus.Draft, menu.Status);
        Assert.Null(menu.PublishedAtUtc);
        var unpublished = DomainAssert.Raised<MenuUnpublished>(menu);
        Assert.Equal(menu.Id, unpublished.MenuId);
        Assert.Equal(Tenant, unpublished.TenantId);
    }

    [Fact]
    public void Unpublish_rejects_a_draft_menu()
    {
        var menu = NewMenu();

        DomainAssert.Throws(MenuErrors.NotPublished, () => menu.Unpublish(_clock));

        DomainAssert.NoEvents(menu);
    }

    [Fact]
    public void Unpublished_menu_can_be_edited_and_published_again()
    {
        var (menu, _, main) = MenuWithOneProduct();
        menu.Publish([main], _clock);
        menu.Unpublish(_clock);
        menu.ClearDomainEvents();

        menu.Rename(Name("Dinner"));
        var desserts = menu.AddSection(Name("Desserts"));
        DomainAssert.Throws(MenuErrors.EmptySection, () => menu.Publish([main], _clock));
        var dessert = ProductRef();
        menu.AddProductToSection(desserts, dessert);
        _clock.Advance(TimeSpan.FromMinutes(5));
        menu.Publish([main, dessert], _clock);

        Assert.Equal("Dinner", menu.Name.Value);
        Assert.Equal(MenuStatus.Published, menu.Status);
        Assert.Equal(_clock.UtcNow, menu.PublishedAtUtc);
        DomainAssert.Raised<MenuPublished>(menu);
    }

    // ---- Archive ----

    [Fact]
    public void Archive_is_allowed_from_draft_and_raises_MenuArchived()
    {
        var menu = NewMenu();

        menu.Archive(_clock);

        Assert.Equal(MenuStatus.Archived, menu.Status);
        var archived = DomainAssert.Raised<MenuArchived>(menu);
        Assert.Equal(menu.Id, archived.MenuId);
        Assert.Equal(Tenant, archived.TenantId);
        Assert.Equal(Location, archived.LocationId);
    }

    [Fact]
    public void Archive_is_allowed_from_published()
    {
        var menu = PublishedMenu();

        menu.Archive(_clock);

        Assert.Equal(MenuStatus.Archived, menu.Status);
        Assert.Null(menu.PublishedAtUtc);
        DomainAssert.Raised<MenuArchived>(menu);
    }

    [Fact]
    public void Archived_menu_is_terminal_and_rejects_every_mutation()
    {
        var (menu, sectionId, product) = MenuWithOneProduct();
        menu.Archive(_clock);
        menu.ClearDomainEvents();

        DomainAssert.Throws(MenuErrors.Archived, () => menu.Rename(Name("Dinner")));
        DomainAssert.Throws(MenuErrors.Archived, () => menu.AddSection(Name("Desserts")));
        DomainAssert.Throws(MenuErrors.Archived, () => menu.RenameSection(sectionId, Name("Plats")));
        DomainAssert.Throws(MenuErrors.Archived, () => menu.RemoveSection(sectionId));
        DomainAssert.Throws(MenuErrors.Archived, () => menu.MoveSection(sectionId, 0));
        DomainAssert.Throws(MenuErrors.Archived, () => menu.AddProductToSection(sectionId, ProductRef()));
        DomainAssert.Throws(MenuErrors.Archived, () => menu.RemoveProductFromSection(sectionId, product.Id));
        DomainAssert.Throws(MenuErrors.Archived, () => menu.MoveProductInSection(sectionId, product.Id, 0));
        DomainAssert.Throws(MenuErrors.Archived, () => menu.Publish([product], _clock));
        DomainAssert.Throws(MenuErrors.Archived, () => menu.Unpublish(_clock));
        DomainAssert.Throws(MenuErrors.Archived, () => menu.Archive(_clock));

        Assert.Equal(MenuStatus.Archived, menu.Status);
        Assert.False(menu.CanBePublished);
        DomainAssert.NoEvents(menu);
    }

    [Fact]
    public void Publish_stamps_the_menu_with_the_event_timestamp()
    {
        var (menu, _, product) = MenuWithOneProduct();

        menu.Publish([product], new TickingClock());

        Assert.Equal(menu.PublishedAtUtc, DomainAssert.Raised<MenuPublished>(menu).OccurredAtUtc);
    }

    /// <summary>Avance à chaque lecture : prouve qu'une opération ne lit l'horloge qu'une fois.</summary>
    private sealed class TickingClock : IClock
    {
        private DateTimeOffset _now = FakeClock.DefaultNow;

        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
    }

    [Fact]
    public void Slots_say_when_the_menu_is_offered()
    {
        var always = NewMenu();
        var happyHour = new MenuSlot(DayOfWeek.Friday, new TimeOnly(18, 0), new TimeOnly(20, 0));

        Assert.True(always.IsOfferedAt(new DateTime(2026, 1, 16, 3, 0, 0)));
        Assert.False(happyHour.CrossesMidnight);
        Assert.True(new MenuSlot(DayOfWeek.Saturday, new TimeOnly(23, 0), new TimeOnly(2, 0)).CrossesMidnight);
        DomainAssert.Throws(MenuErrors.ScheduleInvalid, () => new MenuSlot(DayOfWeek.Friday, new TimeOnly(18, 0), new TimeOnly(18, 0)));
        DomainAssert.Throws(MenuErrors.ScheduleInvalid, () => new MenuSlot((DayOfWeek)9, new TimeOnly(18, 0), new TimeOnly(19, 0)));
    }
}
