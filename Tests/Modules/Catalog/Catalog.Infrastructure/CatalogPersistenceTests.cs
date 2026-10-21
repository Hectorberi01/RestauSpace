using Catalog.Application.Abstractions;
using Catalog.Domain;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Infrastructure;

public sealed class CatalogPersistenceTests(CatalogDatabase database) : IClassFixture<CatalogDatabase>
{
    private IClock Clock => database.Clock;

    private TaxCategory NewTaxCategory(TenantId tenantId, string code = "TVA_18") =>
        TaxCategory.Create(tenantId, TaxCategoryCode.Create(code), TaxCategoryName.Create("TVA 18 %"), TaxRate.Create(0.18m), true, Clock);

    private Product NewProduct(TenantId tenantId, TaxCategory taxCategory, string name = "Poulet DG", decimal price = 3500m) =>
        Product.Create(tenantId, CatalogName.Create(name), "Plantain frit", Money.Of(price, Currency.XOF), taxCategory.ToReference(), Clock);

    private async Task SaveAsync(TenantId tenantId, Action<IServiceProvider> add)
    {
        await using var scope = database.NewScope(tenantId);
        add(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>Une catégorie fiscale et un produit enregistrés pour <paramref name="tenantId"/>.</summary>
    private async Task<(TaxCategory TaxCategory, Product Product)> GivenProductAsync(TenantId tenantId, string name = "Poulet DG")
    {
        var taxCategory = NewTaxCategory(tenantId, $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant());
        var product = NewProduct(tenantId, taxCategory, name);
        await SaveAsync(tenantId, services =>
        {
            services.GetRequiredService<ITaxCategoryRepository>().Add(taxCategory);
            services.GetRequiredService<IProductRepository>().Add(product);
        });
        return (taxCategory, product);
    }

    private async Task<T> InScopeAsync<T>(TenantId? tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = database.NewScope(tenantId);
        return await read(scope.ServiceProvider);
    }

    [Fact]
    public async Task A_product_comes_back_whole_with_its_prices_variants_in_order_and_references()
    {
        var tenant = TenantId.New();
        var taxCategory = NewTaxCategory(tenant);
        var product = NewProduct(tenant, taxCategory);
        var group = ModifierGroup.Create(tenant, CatalogName.Create("Cuisson"), Currency.XOF, SelectionRule.Create(1, 2));
        group.AddModifier(CatalogName.Create("Saignant"), Money.Of(0m, Currency.XOF), Clock);
        group.AddModifier(CatalogName.Create("À point"), Money.Of(250m, Currency.XOF), Clock);
        // Plusieurs variantes dans la même milliseconde : seul l'ordre de la liste fait foi, pas celui des ids.
        var names = new[] { "Moyenne", "Grande", "Petite", "Familiale" };
        foreach (var name in names)
        {
            product.AddVariant(CatalogName.Create(name), Money.Of(name == "Petite" ? -500m : 1000m, Currency.XOF), name == "Grande" ? Sku.Create("dg-l") : null);
        }

        product.AttachModifierGroup(group.ToReference());
        product.SetAllergens([AllergenCodes.Peanuts, AllergenCodes.Gluten]);
        product.SetHiddenChannels([OnlineChannel.Kiosk]);
        product.Activate(Clock);
        await SaveAsync(tenant, services =>
        {
            services.GetRequiredService<ITaxCategoryRepository>().Add(taxCategory);
            services.GetRequiredService<IModifierGroupRepository>().Add(group);
            services.GetRequiredService<IProductRepository>().Add(product);
        });

        var loaded = await InScopeAsync(tenant, services => services.GetRequiredService<IProductRepository>().FindAsync(tenant, product.Id));
        var loadedGroup = await InScopeAsync(
            tenant, services => services.GetRequiredService<IModifierGroupRepository>().FindAsync(tenant, group.Id));
        var loadedTax = await InScopeAsync(
            tenant, services => services.GetRequiredService<ITaxCategoryRepository>().FindAsync(tenant, taxCategory.Id));

        Assert.NotNull(loaded);
        Assert.Equal(product.Name, loaded.Name);
        Assert.Equal("Plantain frit", loaded.Description);
        Assert.Equal(Money.Of(3500m, Currency.XOF), loaded.BasePrice);
        Assert.Equal(ProductStatus.Active, loaded.Status);
        Assert.Equal(taxCategory.Id, loaded.TaxCategoryId);
        Assert.Equal(names, loaded.Variants.Select(variant => variant.Name.Value).ToArray());
        Assert.Equal("DG-L", loaded.Variants.Single(variant => variant.Name.Value == "Grande").Sku!.Value);
        Assert.Equal(Money.Of(3000m, Currency.XOF), loaded.PriceOf(loaded.Variants.Single(variant => variant.Name.Value == "Petite").Id));
        Assert.Equal(group.Id, Assert.Single(loaded.ModifierGroupIds));
        Assert.Equal([AllergenCodes.Gluten, AllergenCodes.Peanuts], loaded.Allergens);
        Assert.Equal(OnlineChannel.Kiosk, Assert.Single(loaded.HiddenChannels));

        Assert.NotNull(loadedGroup);
        Assert.Equal(SelectionRule.Create(1, 2), loadedGroup.SelectionRule);
        Assert.Equal(Currency.XOF, loadedGroup.Currency);
        Assert.Equal(new[] { "Saignant", "À point" }, loadedGroup.Modifiers.Select(modifier => modifier.Name.Value).ToArray());
        Assert.Equal(Money.Of(250m, Currency.XOF), loadedGroup.Modifiers.Last().PriceDelta);

        Assert.NotNull(loadedTax);
        Assert.Equal(TaxRate.Create(0.18m), loadedTax.Rate);
        Assert.Equal("TVA_18", loadedTax.Code.Value);
    }

    [Fact]
    public async Task Prices_of_a_location_or_a_channel_come_back_with_the_product()
    {
        var tenant = TenantId.New();
        var (_, product) = await GivenProductAsync(tenant);
        var location = LocationId.New();
        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IProductRepository>().FindAsync(tenant, product.Id);
            loaded!.SetPriceOverride(new LocationReference(tenant, location, true), null, Money.Of(3000m, Currency.XOF), Clock);
            loaded.SetPriceOverride(null, PriceChannel.Kiosk, Money.Of(3800m, Currency.XOF), Clock);
            await scope.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>().SaveChangesAsync();
        }

        var reloaded = await InScopeAsync(tenant, services => services.GetRequiredService<IProductRepository>().FindAsync(tenant, product.Id));

        Assert.Equal(2, reloaded!.PriceOverrides.Count);
        Assert.Equal(Money.Of(3000m, Currency.XOF), reloaded.BasePriceFor(location, PriceChannel.Kiosk));
        Assert.Equal(Money.Of(3800m, Currency.XOF), reloaded.BasePriceFor(null, PriceChannel.Kiosk));
        Assert.Equal(Money.Of(3500m, Currency.XOF), reloaded.BasePriceFor(null, PriceChannel.InHouse));
    }

    [Fact]
    public async Task Amounts_keep_three_decimals_for_currencies_that_have_them()
    {
        var tenant = TenantId.New();
        var taxCategory = NewTaxCategory(tenant);
        var product = Product.Create(
            tenant, CatalogName.Create("Mezzé"), null, Money.Of(12.345m, Currency.FromCode("TND")), taxCategory.ToReference(), Clock);
        await SaveAsync(tenant, services =>
        {
            services.GetRequiredService<ITaxCategoryRepository>().Add(taxCategory);
            services.GetRequiredService<IProductRepository>().Add(product);
        });

        var loaded = await InScopeAsync(tenant, services => services.GetRequiredService<IProductRepository>().FindAsync(tenant, product.Id));

        Assert.Equal(Money.Of(12.345m, Currency.FromCode("TND")), loaded!.BasePrice);
    }

    [Fact]
    public async Task Catalog_rows_of_another_tenant_cannot_be_loaded_even_with_their_id()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var (taxCategory, product) = await GivenProductAsync(tenantA);

        // Le filtre du repository ET le query filter refusent.
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IProductRepository>().FindAsync(tenantB, product.Id)));
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IProductRepository>().FindAsync(tenantA, product.Id)));
        Assert.Null(await InScopeAsync(
            tenantB, services => services.GetRequiredService<ITaxCategoryRepository>().FindAsync(tenantA, taxCategory.Id)));
        Assert.Empty(await InScopeAsync(
            tenantB, services => services.GetRequiredService<IProductRepository>().FindManyAsync(tenantA, [product.Id])));
        // Sans restaurant sélectionné, rien n'est visible.
        Assert.Empty(await InScopeAsync(null, services => services.GetRequiredService<CatalogDbContext>().Products.ToListAsync()));

        Assert.NotNull(await InScopeAsync(tenantA, services => services.GetRequiredService<IProductRepository>().FindAsync(tenantA, product.Id)));
    }

    [Fact]
    public async Task The_priced_catalog_check_works_outside_a_tenant_request_for_the_given_tenant_only()
    {
        var withProduct = TenantId.New();
        var withGroup = TenantId.New();
        await GivenProductAsync(withProduct);
        await SaveAsync(withGroup, services => services.GetRequiredService<IModifierGroupRepository>().Add(
            ModifierGroup.Create(withGroup, CatalogName.Create("Sauces"), Currency.EUR, SelectionRule.OptionalSingle)));

        // Route plateforme : aucun tenant dans la requête.
        await using var scope = database.NewScope(tenantId: null);
        var lookup = scope.ServiceProvider.GetRequiredService<IPricedCatalogLookup>();

        Assert.True(await lookup.HasPricedCatalogAsync(withProduct));
        Assert.True(await lookup.HasPricedCatalogAsync(withGroup));
        Assert.False(await lookup.HasPricedCatalogAsync(TenantId.New()));
    }

    [Fact]
    public async Task Products_are_listed_by_name_within_the_tenant_with_their_status_filter()
    {
        var tenant = TenantId.New();
        await GivenProductAsync(tenant, "Poisson braisé");
        var (_, archived) = await GivenProductAsync(tenant, "Atassi");
        await GivenProductAsync(tenant, "Ablo");
        await GivenProductAsync(TenantId.New(), "Ailleurs");
        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IProductRepository>().FindAsync(tenant, archived.Id);
            loaded!.Archive(Clock);
            await scope.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>().SaveChangesAsync();
        }

        var first = await InScopeAsync(tenant, services => services.GetRequiredService<IProductRepository>().ListAsync(tenant, null, 0, 1));
        var second = await InScopeAsync(tenant, services => services.GetRequiredService<IProductRepository>().ListAsync(tenant, null, 1, 5));
        var onlyArchived = await InScopeAsync(
            tenant, services => services.GetRequiredService<IProductRepository>().ListAsync(tenant, ProductStatus.Archived, 0, 5));

        Assert.Equal(2, first.TotalCount);
        Assert.Equal("Ablo", Assert.Single(first.Items).Name.Value);
        Assert.Equal("Poisson braisé", Assert.Single(second.Items).Name.Value);
        Assert.Equal(archived.Id, Assert.Single(onlyArchived.Items).Id);
    }

    [Fact]
    public async Task Moved_sections_and_products_keep_their_order_and_events_reach_the_outbox()
    {
        var tenant = TenantId.New();
        var (_, poulet) = await GivenProductAsync(tenant, "Poulet");
        var (_, poisson) = await GivenProductAsync(tenant, "Poisson");
        var menu = Menu.Create(tenant, new LocationReference(tenant, LocationId.New(), IsOperational: true), CatalogName.Create("Carte"));
        var starters = menu.AddSection(CatalogName.Create("Entrées"));
        var mains = menu.AddSection(CatalogName.Create("Plats"));
        menu.AddProductToSection(mains, poulet.ToReference());
        menu.AddProductToSection(mains, poisson.ToReference());
        menu.AddProductToSection(starters, poisson.ToReference());
        await SaveAsync(tenant, services => services.GetRequiredService<IMenuRepository>().Add(menu));

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IMenuRepository>().FindAsync(tenant, menu.Id);
            loaded!.MoveSection(mains, 0);
            loaded.MoveProductInSection(mains, poisson.Id, 0);
            loaded.Publish([poulet.ToReference(), poisson.ToReference()], Clock);
            loaded.SetSchedule([new MenuSlot(DayOfWeek.Friday, new TimeOnly(18, 0), new TimeOnly(1, 30))], Clock);
            await scope.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>().SaveChangesAsync();
        }

        var reloaded = await InScopeAsync(tenant, services => services.GetRequiredService<IMenuRepository>().FindAsync(tenant, menu.Id));
        Assert.Equal(new[] { mains, starters }, reloaded!.Sections.Select(section => section.Id).ToArray());
        Assert.Equal(new[] { poisson.Id, poulet.Id }, reloaded.Sections.First().ProductIds.ToArray());
        Assert.Equal(MenuStatus.Published, reloaded.Status);
        Assert.Equal(Clock.UtcNow, reloaded.PublishedAtUtc);
        Assert.Equal(new MenuSlot(DayOfWeek.Friday, new TimeOnly(18, 0), new TimeOnly(1, 30)), Assert.Single(reloaded.Slots));

        var types = await InScopeAsync(tenant, services => services.GetRequiredService<CatalogDbContext>().OutboxMessages
            .Select(message => message.Type)
            .ToListAsync());
        Assert.Contains(nameof(MenuPublished), types);
        Assert.Contains(nameof(ProductCreated), types);
        Assert.Contains(nameof(CatalogPriced), types);
    }

    [Fact]
    public async Task A_removed_variant_is_deleted_and_a_removed_modifier_too()
    {
        var tenant = TenantId.New();
        var (_, product) = await GivenProductAsync(tenant);
        var group = ModifierGroup.Create(tenant, CatalogName.Create("Sauces"), Currency.XOF, SelectionRule.OptionalSingle);
        var spicy = group.AddModifier(CatalogName.Create("Piment"), Money.Of(0m, Currency.XOF), Clock);
        group.AddModifier(CatalogName.Create("Moyo"), Money.Of(100m, Currency.XOF), Clock);
        await SaveAsync(tenant, services => services.GetRequiredService<IModifierGroupRepository>().Add(group));
        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IProductRepository>().FindAsync(tenant, product.Id);
            loaded!.AddVariant(CatalogName.Create("Grande"), Money.Of(500m, Currency.XOF));
            await scope.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>().SaveChangesAsync();
        }

        await using (var scope = database.NewScope(tenant))
        {
            var services = scope.ServiceProvider;
            var loaded = await services.GetRequiredService<IProductRepository>().FindAsync(tenant, product.Id);
            loaded!.RemoveVariant(Assert.Single(loaded.Variants).Id);
            var loadedGroup = await services.GetRequiredService<IModifierGroupRepository>().FindAsync(tenant, group.Id);
            loadedGroup!.RemoveModifier(spicy, Clock);
            await services.GetRequiredService<ICatalogUnitOfWork>().SaveChangesAsync();
        }

        var reloaded = await InScopeAsync(tenant, services => services.GetRequiredService<IProductRepository>().FindAsync(tenant, product.Id));
        var reloadedGroup = await InScopeAsync(
            tenant, services => services.GetRequiredService<IModifierGroupRepository>().FindAsync(tenant, group.Id));
        Assert.Empty(reloaded!.Variants);
        Assert.Equal("Moyo", Assert.Single(reloadedGroup!.Modifiers).Name.Value);
    }

    [Fact]
    public async Task Two_requests_adding_a_variant_with_the_same_name_cannot_both_win()
    {
        var tenant = TenantId.New();
        var (_, product) = await GivenProductAsync(tenant);

        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var mine = await first.ServiceProvider.GetRequiredService<IProductRepository>().FindAsync(tenant, product.Id);
        var theirs = await second.ServiceProvider.GetRequiredService<IProductRepository>().FindAsync(tenant, product.Id);
        mine!.AddVariant(CatalogName.Create("Grande"), Money.Of(500m, Currency.XOF));
        theirs!.AddVariant(CatalogName.Create("grande"), Money.Of(700m, Currency.XOF));

        await first.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>().SaveChangesAsync();
        // Seules des lignes de variantes changent : c'est la version du produit, réécrite, qui arrête la seconde.
        await Assert.ThrowsAsync<ConcurrencyException>(
            () => second.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>().SaveChangesAsync());

        var stored = await InScopeAsync(tenant, services => services.GetRequiredService<IProductRepository>().FindAsync(tenant, product.Id));
        Assert.Equal(Money.Of(500m, Currency.XOF), Assert.Single(stored!.Variants).PriceDelta);
    }

    [Fact]
    public async Task A_tax_code_taken_concurrently_is_refused_by_the_unique_index()
    {
        var tenant = TenantId.New();
        await SaveAsync(tenant, services => services.GetRequiredService<ITaxCategoryRepository>().Add(NewTaxCategory(tenant, "TVA_10")));

        // Le contrôle préalable a répondu « libre » aux deux requêtes : l'index unique tranche.
        var refused = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(
            tenant, services => services.GetRequiredService<ITaxCategoryRepository>().Add(NewTaxCategory(tenant, "TVA_10"))));
        // Le même code reste libre dans un autre tenant.
        var other = TenantId.New();
        await SaveAsync(other, services => services.GetRequiredService<ITaxCategoryRepository>().Add(NewTaxCategory(other, "TVA_10")));

        Assert.Equal(TaxCategoryErrors.CodeNotUnique, refused.Error);
        await using var scope = database.NewScope(tenant);
        Assert.False(await scope.ServiceProvider.GetRequiredService<ITaxCategoryCodeUniquenessChecker>()
            .IsUniqueAsync(tenant, TaxCategoryCode.Create("tva_10")));
        Assert.True(await scope.ServiceProvider.GetRequiredService<ITaxCategoryCodeUniquenessChecker>()
            .IsUniqueAsync(tenant, TaxCategoryCode.Create("TVA_20")));
    }
}
