using Catalog.Application.Commands;
using Catalog.Application.Queries;
using Catalog.Domain;

namespace Catalog.Application;

public sealed class ProductUseCaseTests
{
    private readonly CatalogFixture _fixture = new();

    [Fact]
    public async Task Create_stores_a_draft_product_of_the_current_tenant_in_its_default_currency()
    {
        var taxCategory = await _fixture.GivenTaxCategoryAsync();

        var id = await _fixture.CreateProduct.HandleAsync(new CreateProductCommand("  Poulet DG ", "Plantain frit", 3500m, taxCategory));

        var product = _fixture.Products.Stored(id);
        Assert.Equal(_fixture.TenantA, product.TenantId);
        Assert.Equal("Poulet DG", product.Name.Value);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Equal(Money.Of(3500m, Currency.XOF), product.BasePrice);
        Assert.Equal(taxCategory, product.TaxCategoryId.Value);
        Assert.Equal(1, _fixture.SaveCount);
        DomainAssert.Raised<ProductCreated>(product);
    }

    [Fact]
    public async Task Prices_follow_the_precision_of_the_product_currency()
    {
        var id = await _fixture.GivenProductAsync();

        // Le franc CFA n'a pas de décimales : la requête ne peut pas en inventer.
        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ChangeBasePrice.HandleAsync(new ChangeProductBasePriceCommand(id, 3500.5m)));
        await _fixture.ChangeBasePrice.HandleAsync(new ChangeProductBasePriceCommand(id, 4000m));

        Assert.Equal(CommonErrors.MoneyPrecision, refused.Error);
        Assert.Equal(Money.Of(4000m, Currency.XOF), _fixture.Products.Stored(id).BasePrice);
        Assert.Equal(1, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_tax_category_of_another_tenant_is_not_found_and_an_archived_one_is_refused()
    {
        var ofB = await _fixture.GivenTaxCategoryAsync(_fixture.TenantB);
        var archived = await _fixture.GivenTaxCategoryAsync(code: "OLD");
        await _fixture.ArchiveTaxCategory.HandleAsync(new ArchiveTaxCategoryCommand(archived));

        var foreign = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.CreateProduct.HandleAsync(new CreateProductCommand("Alloco", null, 1000m, ofB)));
        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.CreateProduct.HandleAsync(new CreateProductCommand("Alloco", null, 1000m, archived)));

        Assert.Equal(CatalogApplicationErrors.TaxCategoryNotFound, foreign.Error);
        Assert.Equal(ProductErrors.TaxCategoryArchived, refused.Error);
        Assert.Empty(_fixture.Products.Items);
    }

    [Fact]
    public async Task Without_a_selected_restaurant_nothing_can_be_read_or_written()
    {
        var id = await _fixture.GivenProductAsync();
        _fixture.CurrentTenantId = null;

        var read = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.GetProduct.HandleAsync(new GetProductByIdQuery(id)));
        var list = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.ListProducts.HandleAsync(new ListProductsQuery()));
        var write = await Assert.ThrowsAsync<ForbiddenException>(
            () => _fixture.ActivateProduct.HandleAsync(new ActivateProductCommand(id)));

        Assert.Equal(TenancyErrors.TenantRequired, read.Error);
        Assert.Equal(TenancyErrors.TenantRequired, list.Error);
        Assert.Equal(TenancyErrors.TenantRequired, write.Error);
    }

    [Fact]
    public async Task A_product_of_another_tenant_is_not_found_neither_to_read_nor_to_change()
    {
        var ofB = await _fixture.GivenProductAsync(_fixture.TenantB, basePrice: 12.50m);

        var read = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetProduct.HandleAsync(new GetProductByIdQuery(ofB)));
        var archive = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.ArchiveProduct.HandleAsync(new ArchiveProductCommand(ofB)));

        Assert.Equal(CatalogApplicationErrors.ProductNotFound, read.Error);
        Assert.Equal(CatalogApplicationErrors.ProductNotFound, archive.Error);
        Assert.Equal(ProductStatus.Draft, _fixture.Products.Stored(ofB).Status);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_suspended_restaurant_keeps_reading_its_catalog_but_cannot_change_it()
    {
        var id = await _fixture.GivenProductAsync();
        _fixture.Tenants[_fixture.TenantA] = new Abstractions.CatalogTenant(Currency.XOF, AcceptsBusinessWrites: false);

        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ActivateProduct.HandleAsync(new ActivateProductCommand(id)));
        var product = await _fixture.GetProduct.HandleAsync(new GetProductByIdQuery(id));

        Assert.Equal(CatalogApplicationErrors.TenantNotWritable, refused.Error);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task Variants_and_modifier_groups_are_read_back_with_their_sale_prices()
    {
        var id = await _fixture.GivenProductAsync(basePrice: 3500m);
        var group = await _fixture.GivenModifierGroupAsync();
        var large = await _fixture.AddVariant.HandleAsync(new AddProductVariantCommand(id, "Grande", 1500m, "dg-l"));
        await _fixture.AddVariant.HandleAsync(new AddProductVariantCommand(id, "Petite", -1000m));
        await _fixture.AttachModifierGroup.HandleAsync(new AttachModifierGroupCommand(id, group));

        var product = await _fixture.GetProduct.HandleAsync(new GetProductByIdQuery(id));

        Assert.Equal("XOF", product.CurrencyCode);
        Assert.Equal(
            new[] { ("Grande", 5000m, "DG-L"), ("Petite", 2500m, (string?)null) },
            product.Variants.Select(variant => (variant.Name, variant.Price, variant.Sku)).ToArray());
        Assert.Equal(large, product.Variants[0].Id);
        Assert.Equal(group, Assert.Single(product.ModifierGroupIds));
        Assert.Empty(product.Allergens);
        Assert.Empty(product.HiddenChannels);
    }

    [Fact]
    public async Task A_modifier_group_of_another_tenant_cannot_be_attached()
    {
        var id = await _fixture.GivenProductAsync();
        var ofB = await _fixture.GivenModifierGroupAsync(_fixture.TenantB);

        var refused = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.AttachModifierGroup.HandleAsync(new AttachModifierGroupCommand(id, ofB)));

        Assert.Equal(CatalogApplicationErrors.ModifierGroupNotFound, refused.Error);
        Assert.Empty(_fixture.Products.Stored(id).ModifierGroupIds);
    }

    [Fact]
    public async Task A_new_tax_category_is_attached_to_the_product_for_future_sales_only_within_the_tenant()
    {
        var id = await _fixture.GivenProductAsync();
        var reduced = await _fixture.GivenTaxCategoryAsync(code: "TVA_10", rate: 0.10m);
        var ofB = await _fixture.GivenTaxCategoryAsync(_fixture.TenantB, code: "TVA_10");

        await _fixture.ChangeProductTaxCategory.HandleAsync(new ChangeProductTaxCategoryCommand(id, reduced));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.ChangeProductTaxCategory.HandleAsync(new ChangeProductTaxCategoryCommand(id, ofB)));

        Assert.Equal(reduced, _fixture.Products.Stored(id).TaxCategoryId.Value);
        Assert.Equal(CatalogApplicationErrors.TaxCategoryNotFound, foreign.Error);
    }

    [Fact]
    public async Task List_shows_the_current_tenant_products_by_name_hiding_archived_ones_unless_asked()
    {
        await _fixture.GivenProductAsync(name: "Poisson braisé");
        var archived = await _fixture.GivenProductAsync(name: "Atassi");
        await _fixture.GivenProductAsync(name: "Ablo");
        await _fixture.GivenProductAsync(_fixture.TenantB, name: "Croissant");
        await _fixture.ArchiveProduct.HandleAsync(new ArchiveProductCommand(archived));

        var page = await _fixture.ListProducts.HandleAsync(new ListProductsQuery());
        var onlyArchived = await _fixture.ListProducts.HandleAsync(new ListProductsQuery(Status: ProductStatus.Archived));
        var second = await _fixture.ListProducts.HandleAsync(new ListProductsQuery(Page: 2, PageSize: 1));

        Assert.Equal(new[] { "Ablo", "Poisson braisé" }, page.Items.Select(item => item.Name).ToArray());
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(archived, Assert.Single(onlyArchived.Items).Id);
        Assert.Equal("Poisson braisé", Assert.Single(second.Items).Name);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task List_refuses_an_invalid_page(int page, int pageSize)
    {
        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ListProducts.HandleAsync(new ListProductsQuery(page, pageSize)));

        Assert.Equal(CatalogApplicationErrors.PagingInvalid, refused.Error);
    }

    [Fact]
    public async Task The_priced_catalog_check_answers_for_the_given_tenant_only()
    {
        Assert.False(await _fixture.HasPricedCatalog.HandleAsync(new HasPricedCatalogQuery(_fixture.TenantA.Value)));

        await _fixture.GivenProductAsync(_fixture.TenantB, basePrice: 9m);
        await _fixture.GivenModifierGroupAsync();

        Assert.True(await _fixture.HasPricedCatalog.HandleAsync(new HasPricedCatalogQuery(_fixture.TenantA.Value)));
        Assert.True(await _fixture.HasPricedCatalog.HandleAsync(new HasPricedCatalogQuery(_fixture.TenantB.Value)));
        Assert.False(await _fixture.HasPricedCatalog.HandleAsync(new HasPricedCatalogQuery(TenantId.New().Value)));
        Assert.False(await _fixture.HasPricedCatalog.HandleAsync(new HasPricedCatalogQuery(Guid.Empty)));
    }
}
