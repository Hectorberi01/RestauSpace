namespace Catalog.Domain;

public class ProductTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly TaxCategoryReference Tax = TaxRef();
    private readonly FakeClock _clock = new();

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.EUR);

    private static CatalogName Name(string value) => CatalogName.Create(value);

    private Product NewProduct(decimal basePrice = 10m) =>
        Product.Create(Tenant, Name("Burger"), "Beef burger", Eur(basePrice), Tax, _clock);

    private Product ActiveProduct(decimal basePrice = 10m)
    {
        var product = NewProduct(basePrice);
        product.Activate(_clock);
        product.ClearDomainEvents();
        return product;
    }

    private Product ArchivedProduct()
    {
        var product = NewProduct();
        product.AddVariant(Name("Large"), Eur(2m));
        product.Archive(_clock);
        product.ClearDomainEvents();
        return product;
    }

    private static TaxCategoryReference TaxRef(TenantId? tenant = null, bool isArchived = false) =>
        CatalogRefs.Tax(tenant ?? Tenant, isArchived);

    private static ModifierGroupReference GroupRef(TenantId? tenant = null, Currency? currency = null) =>
        CatalogRefs.Group(tenant ?? Tenant, currency);

    // ---- Création ----

    [Fact]
    public void Create_starts_as_draft_and_raises_ProductCreated()
    {
        var product = NewProduct();

        Assert.Equal(Tenant, product.TenantId);
        Assert.Equal("Burger", product.Name.Value);
        Assert.Equal("Beef burger", product.Description);
        Assert.Equal(Eur(10m), product.BasePrice);
        Assert.Equal(Currency.EUR, product.Currency);
        Assert.Equal(Tax.Id, product.TaxCategoryId);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.False(product.IsSellable);
        Assert.Empty(product.Variants);
        Assert.Empty(product.ModifierGroupIds);
        Assert.Empty(product.Allergens);
        Assert.Empty(product.HiddenChannels);

        var created = DomainAssert.Raised<ProductCreated>(product);
        Assert.Equal(Tenant, created.TenantId);
        Assert.Equal(product.Id, created.ProductId);
        Assert.Equal("Burger", created.Name);
        Assert.Equal(Eur(10m), created.BasePrice);
        Assert.Equal(_clock.UtcNow, created.OccurredAtUtc);
    }

    [Fact]
    public void Create_rejects_a_negative_price() =>
        DomainAssert.Throws(
            ProductErrors.NegativePrice,
            () => Product.Create(Tenant, Name("Burger"), null, Eur(-0.01m), Tax, _clock));

    [Fact]
    public void Create_accepts_a_free_product() =>
        Assert.True(NewProduct(0m).BasePrice.IsZero);

    [Fact]
    public void Create_rejects_an_empty_tenant() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => Product.Create(default, Name("Burger"), null, Eur(1m), Tax, _clock));

    [Fact]
    public void Create_accepts_the_reference_of_a_real_tax_category()
    {
        var category = TaxCategory.Create(
            Tenant, TaxCategoryCode.Create("VAT_STD"), TaxCategoryName.Create("Standard VAT"), TaxRate.Create(0.10m), true, _clock);

        var product = Product.Create(Tenant, Name("Burger"), null, Eur(1m), category.ToReference(), _clock);

        Assert.Equal(category.Id, product.TaxCategoryId);
    }

    [Fact]
    public void Create_rejects_a_tax_category_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Product.Create(Tenant, Name("Burger"), null, Eur(1m), TaxRef(tenant: TenantId.New()), _clock));

    [Fact]
    public void Create_rejects_an_archived_tax_category() =>
        DomainAssert.Throws(
            ProductErrors.TaxCategoryArchived,
            () => Product.Create(Tenant, Name("Burger"), null, Eur(1m), TaxRef(isArchived: true), _clock));

    [Fact]
    public void Create_normalizes_a_blank_description_to_null_and_rejects_a_too_long_one()
    {
        Assert.Null(Product.Create(Tenant, Name("Burger"), "   ", Eur(1m), Tax, _clock).Description);
        DomainAssert.Throws(
            ProductErrors.DescriptionTooLong,
            () => Product.Create(Tenant, Name("Burger"), new string('x', Product.MaxDescriptionLength + 1), Eur(1m), Tax, _clock));
    }

    // ---- Modifications simples ----

    [Fact]
    public void Rename_changes_the_name()
    {
        var product = NewProduct();

        product.Rename(Name("Cheeseburger"));

        Assert.Equal("Cheeseburger", product.Name.Value);
    }

    [Fact]
    public void ChangeDescription_trims_accepts_the_limit_and_rejects_beyond()
    {
        var product = NewProduct();

        product.ChangeDescription("  With cheese ");
        Assert.Equal("With cheese", product.Description);

        product.ChangeDescription(new string('x', Product.MaxDescriptionLength));
        Assert.Equal(Product.MaxDescriptionLength, product.Description!.Length);

        DomainAssert.Throws(
            ProductErrors.DescriptionTooLong,
            () => product.ChangeDescription(new string('x', Product.MaxDescriptionLength + 1)));

        product.ChangeDescription(null);
        Assert.Null(product.Description);
    }

    [Fact]
    public void ChangeTaxCategory_replaces_it()
    {
        var product = NewProduct();
        var other = TaxRef();

        product.ChangeTaxCategory(other);

        Assert.Equal(other.Id, product.TaxCategoryId);
    }

    [Fact]
    public void ChangeTaxCategory_rejects_a_category_of_another_tenant()
    {
        var product = NewProduct();

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => product.ChangeTaxCategory(TaxRef(tenant: TenantId.New())));

        Assert.Equal(Tax.Id, product.TaxCategoryId);
    }

    [Fact]
    public void ChangeTaxCategory_rejects_an_archived_category()
    {
        var product = NewProduct();

        DomainAssert.Throws(ProductErrors.TaxCategoryArchived, () => product.ChangeTaxCategory(TaxRef(isArchived: true)));

        Assert.Equal(Tax.Id, product.TaxCategoryId);
    }

    [Fact]
    public void ChangeTaxCategory_to_the_category_already_held_is_a_no_op_even_once_it_is_archived()
    {
        var category = CatalogRefs.TaxCategory(Tenant);
        var product = Product.Create(Tenant, Name("Burger"), null, Eur(10m), category.ToReference(), _clock);
        product.ClearDomainEvents();
        category.Archive(_clock);

        product.ChangeTaxCategory(category.ToReference());

        Assert.Equal(category.Id, product.TaxCategoryId);
        DomainAssert.NoEvents(product);
    }

    // ---- Prix ----

    [Fact]
    public void ChangeBasePrice_updates_the_price_and_raises_ProductPriceChanged()
    {
        var product = NewProduct();
        product.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromHours(1));

        product.ChangeBasePrice(Eur(12.5m), _clock);

        Assert.Equal(Eur(12.5m), product.BasePrice);
        var changed = DomainAssert.Raised<ProductPriceChanged>(product);
        Assert.Equal(Tenant, changed.TenantId);
        Assert.Equal(product.Id, changed.ProductId);
        Assert.Equal(Eur(10m), changed.PreviousBasePrice);
        Assert.Equal(Eur(12.5m), changed.NewBasePrice);
        Assert.Equal(_clock.UtcNow, changed.OccurredAtUtc);
    }

    [Fact]
    public void ChangeBasePrice_rejects_a_negative_price()
    {
        var product = NewProduct();
        product.ClearDomainEvents();

        DomainAssert.Throws(ProductErrors.NegativePrice, () => product.ChangeBasePrice(Eur(-1m), _clock));

        Assert.Equal(Eur(10m), product.BasePrice);
        DomainAssert.NoEvents(product);
    }

    [Fact]
    public void ChangeBasePrice_rejects_another_currency()
    {
        var product = NewProduct();

        DomainAssert.Throws(ProductErrors.CurrencyMismatch, () => product.ChangeBasePrice(Money.Of(10m, Currency.USD), _clock));
    }

    [Fact]
    public void ChangeBasePrice_to_the_same_price_raises_nothing()
    {
        var product = NewProduct();
        product.ClearDomainEvents();

        product.ChangeBasePrice(Eur(10m), _clock);

        DomainAssert.NoEvents(product);
    }

    [Fact]
    public void ChangeBasePrice_revalidates_existing_variants()
    {
        var product = NewProduct(10m);
        product.AddVariant(Name("Small"), Eur(-4m));
        product.ClearDomainEvents();

        DomainAssert.Throws(ProductErrors.VariantPriceNegative, () => product.ChangeBasePrice(Eur(3.99m), _clock));
        Assert.Equal(Eur(10m), product.BasePrice);
        DomainAssert.NoEvents(product);

        product.ChangeBasePrice(Eur(4m), _clock);
        Assert.Equal(Eur(4m), product.BasePrice);
    }

    // ---- Transitions de statut ----

    [Fact]
    public void Activate_moves_a_draft_to_active_and_raises_ProductActivated()
    {
        var product = NewProduct();
        product.ClearDomainEvents();

        product.Activate(_clock);

        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.True(product.IsSellable);
        var activated = DomainAssert.Raised<ProductActivated>(product);
        Assert.Equal(Tenant, activated.TenantId);
        Assert.Equal(product.Id, activated.ProductId);
        Assert.Equal(_clock.UtcNow, activated.OccurredAtUtc);
    }

    [Fact]
    public void Activate_rejects_an_already_active_product()
    {
        var product = ActiveProduct();

        DomainAssert.Throws(ProductErrors.AlreadyActive, () => product.Activate(_clock));

        DomainAssert.NoEvents(product);
    }

    [Fact]
    public void MarkUnavailable_moves_an_active_product_to_unavailable_and_raises_ProductUnavailable()
    {
        var product = ActiveProduct();

        product.MarkUnavailable(_clock);

        Assert.Equal(ProductStatus.Unavailable, product.Status);
        Assert.False(product.IsSellable);
        var unavailable = DomainAssert.Raised<ProductUnavailable>(product);
        Assert.Equal(Tenant, unavailable.TenantId);
        Assert.Equal(product.Id, unavailable.ProductId);
    }

    [Fact]
    public void MarkUnavailable_rejects_a_draft_product()
    {
        var product = NewProduct();
        product.ClearDomainEvents();

        DomainAssert.Throws(ProductErrors.NotActive, () => product.MarkUnavailable(_clock));

        Assert.Equal(ProductStatus.Draft, product.Status);
        DomainAssert.NoEvents(product);
    }

    [Fact]
    public void MarkUnavailable_rejects_an_already_unavailable_product()
    {
        var product = ActiveProduct();
        product.MarkUnavailable(_clock);

        DomainAssert.Throws(ProductErrors.NotActive, () => product.MarkUnavailable(_clock));

        DomainAssert.Raised<ProductUnavailable>(product);
    }

    [Fact]
    public void Activate_makes_an_unavailable_product_sellable_again()
    {
        var product = ActiveProduct();
        product.MarkUnavailable(_clock);

        product.Activate(_clock);

        Assert.Equal(ProductStatus.Active, product.Status);
        DomainAssert.Raised<ProductActivated>(product);
    }

    [Theory]
    [InlineData(ProductStatus.Draft)]
    [InlineData(ProductStatus.Active)]
    [InlineData(ProductStatus.Unavailable)]
    public void Archive_is_allowed_from_every_non_archived_state(ProductStatus from)
    {
        var product = NewProduct();
        if (from != ProductStatus.Draft)
        {
            product.Activate(_clock);
        }

        if (from == ProductStatus.Unavailable)
        {
            product.MarkUnavailable(_clock);
        }

        product.ClearDomainEvents();

        product.Archive(_clock);

        Assert.Equal(ProductStatus.Archived, product.Status);
        Assert.False(product.IsSellable);
        var archived = DomainAssert.Raised<ProductArchived>(product);
        Assert.Equal(product.Id, archived.ProductId);
        Assert.Equal(Tenant, archived.TenantId);
    }

    [Fact]
    public void Archived_product_is_terminal_and_rejects_every_mutation()
    {
        var product = ArchivedProduct();
        var variantId = product.Variants.Single().Id;

        DomainAssert.Throws(ProductErrors.Archived, () => product.Rename(Name("Other")));
        DomainAssert.Throws(ProductErrors.Archived, () => product.ChangeDescription("x"));
        DomainAssert.Throws(ProductErrors.Archived, () => product.ChangeTaxCategory(TaxRef()));
        DomainAssert.Throws(ProductErrors.Archived, () => product.ChangeBasePrice(Eur(1m), _clock));
        DomainAssert.Throws(ProductErrors.Archived, () => product.Activate(_clock));
        DomainAssert.Throws(ProductErrors.Archived, () => product.MarkUnavailable(_clock));
        DomainAssert.Throws(ProductErrors.Archived, () => product.Archive(_clock));
        DomainAssert.Throws(ProductErrors.Archived, () => product.AddVariant(Name("XL"), Eur(3m)));
        DomainAssert.Throws(ProductErrors.Archived, () => product.RemoveVariant(variantId));
        DomainAssert.Throws(ProductErrors.Archived, () => product.ChangeVariantPriceDelta(variantId, Eur(1m), _clock));
        DomainAssert.Throws(ProductErrors.Archived, () => product.SetVariantAvailability(variantId, false, _clock));
        DomainAssert.Throws(ProductErrors.Archived, () => product.AttachModifierGroup(GroupRef()));
        DomainAssert.Throws(ProductErrors.Archived, () => product.DetachModifierGroup(ModifierGroupId.New()));
        DomainAssert.Throws(ProductErrors.Archived, () => product.SetAllergens([AllergenCodes.Milk]));
        DomainAssert.Throws(ProductErrors.Archived, () => product.SetHiddenChannels([OnlineChannel.Web]));

        Assert.Equal(ProductStatus.Archived, product.Status);
        DomainAssert.NoEvents(product);
    }

    [Fact]
    public void Archived_product_is_not_sellable()
    {
        var product = ActiveProduct();
        var variantId = product.AddVariant(Name("Large"), Eur(2m));
        Assert.True(product.CanBeSold(variantId));

        product.Archive(_clock);

        Assert.False(product.IsSellable);
        Assert.False(product.CanBeSold(variantId));
        Assert.True(product.ToReference().IsArchived);
    }

    // ---- Vendabilité ----

    [Fact]
    public void CanBeSold_requires_an_active_product_and_a_known_available_variant()
    {
        var product = NewProduct();
        var variantId = product.AddVariant(Name("Large"), Eur(2m));

        Assert.False(product.CanBeSold(variantId));

        product.Activate(_clock);
        Assert.True(product.CanBeSold(variantId));
        Assert.False(product.CanBeSold(VariantId.New()));

        product.SetVariantAvailability(variantId, false, _clock);
        Assert.False(product.CanBeSold(variantId));
        Assert.True(product.IsSellable);

        product.SetVariantAvailability(variantId, true, _clock);
        product.MarkUnavailable(_clock);
        Assert.False(product.CanBeSold(variantId));
    }

    [Fact]
    public void PriceOf_adds_the_variant_delta_to_the_base_price()
    {
        var product = NewProduct(10m);
        var large = product.AddVariant(Name("Large"), Eur(2.5m));
        var small = product.AddVariant(Name("Small"), Eur(-3m));

        Assert.Equal(Eur(12.5m), product.PriceOf(large));
        Assert.Equal(Eur(7m), product.PriceOf(small));
        DomainAssert.Throws(ProductErrors.VariantNotFound, () => product.PriceOf(VariantId.New()));
    }

    [Fact]
    public void PriceOf_rejects_an_archived_product()
    {
        var product = ArchivedProduct();

        DomainAssert.Throws(ProductErrors.Archived, () => product.PriceOf(product.Variants.Single().Id));
    }

    // ---- Contrat avec la ligne de commande ----

    [Fact]
    public void DescribeForSale_returns_what_an_order_line_snapshots_for_a_variant()
    {
        var product = ActiveProduct(10m);
        var large = product.AddVariant(Name("Large"), Eur(2.5m));

        var item = product.DescribeForSale(large);

        Assert.Equal(Tenant, item.TenantId);
        Assert.Equal(product.Id, item.ProductId);
        Assert.Equal("Burger", item.ProductName);
        Assert.Equal(large, item.VariantId);
        Assert.Equal("Large", item.VariantName);
        Assert.Equal(Eur(12.5m), item.UnitPrice);
        Assert.Equal(product.TaxCategoryId, item.TaxCategoryId);
    }

    [Fact]
    public void DescribeForSale_without_variant_sells_a_product_without_variants_at_its_base_price()
    {
        var product = ActiveProduct(10m);

        var item = product.DescribeForSale(null);

        Assert.Null(item.VariantId);
        Assert.Null(item.VariantName);
        Assert.Equal(Eur(10m), item.UnitPrice);
    }

    [Fact]
    public void DescribeForSale_requires_a_variant_when_the_product_has_some()
    {
        var product = ActiveProduct();
        product.AddVariant(Name("Large"), Eur(2m));

        DomainAssert.Throws(ProductErrors.VariantRequired, () => product.DescribeForSale(null));
    }

    [Fact]
    public void DescribeForSale_rejects_an_unknown_or_unavailable_variant()
    {
        var product = ActiveProduct();
        var large = product.AddVariant(Name("Large"), Eur(2m));
        product.SetVariantAvailability(large, false, _clock);

        DomainAssert.Throws(ProductErrors.VariantUnavailable, () => product.DescribeForSale(large));
        DomainAssert.Throws(ProductErrors.VariantNotFound, () => product.DescribeForSale(VariantId.New()));
    }

    [Fact]
    public void DescribeForSale_rejects_a_product_that_is_not_sellable()
    {
        var draft = NewProduct();
        DomainAssert.Throws(ProductErrors.NotSellable, () => draft.DescribeForSale(null));

        var unavailable = ActiveProduct();
        unavailable.MarkUnavailable(_clock);
        DomainAssert.Throws(ProductErrors.NotSellable, () => unavailable.DescribeForSale(null));

        var archived = ArchivedProduct();
        DomainAssert.Throws(ProductErrors.Archived, () => archived.DescribeForSale(archived.Variants.Single().Id));
    }

    [Fact]
    public void Names_handed_to_an_order_line_never_exceed_120_characters()
    {
        var longest = new string('n', 120);
        var product = Product.Create(Tenant, Name(longest), null, Eur(1m), Tax, _clock);
        product.Activate(_clock);
        var variantId = product.AddVariant(Name(longest), Eur(0m));

        var item = product.DescribeForSale(variantId);

        Assert.Equal(120, CatalogName.MaxLength);
        Assert.Equal(120, item.ProductName.Length);
        Assert.Equal(120, item.VariantName!.Length);
        DomainAssert.Throws(CatalogErrors.NameTooLong, () => Name(longest + "n"));
    }

    // ---- Variantes ----

    [Fact]
    public void AddVariant_adds_an_available_variant()
    {
        var product = NewProduct();

        var variantId = product.AddVariant(Name("Large"), Eur(2m), Sku.Create("brg-l"));

        var variant = Assert.Single(product.Variants);
        Assert.Equal(variantId, variant.Id);
        Assert.Equal("Large", variant.Name.Value);
        Assert.Equal(Eur(2m), variant.PriceDelta);
        Assert.Equal("BRG-L", variant.Sku!.Value);
        Assert.True(variant.IsAvailable);
    }

    [Fact]
    public void AddVariant_rejects_a_duplicate_name_ignoring_case()
    {
        var product = NewProduct();
        product.AddVariant(Name("Large"), Eur(2m));

        DomainAssert.Throws(ProductErrors.VariantNameDuplicate, () => product.AddVariant(Name("LARGE"), Eur(3m)));

        Assert.Single(product.Variants);
    }

    [Fact]
    public void AddVariant_rejects_a_duplicate_sku_but_allows_several_variants_without_sku()
    {
        var product = NewProduct();
        product.AddVariant(Name("Large"), Eur(2m), Sku.Create("BRG-1"));
        product.AddVariant(Name("Medium"), Eur(1m));
        product.AddVariant(Name("Small"), Eur(0m));

        DomainAssert.Throws(
            ProductErrors.VariantSkuDuplicate,
            () => product.AddVariant(Name("XL"), Eur(3m), Sku.Create("brg-1")));

        Assert.Equal(3, product.Variants.Count);
    }

    [Fact]
    public void AddVariant_rejects_a_delta_in_another_currency()
    {
        var product = NewProduct();

        DomainAssert.Throws(
            ProductErrors.VariantCurrencyMismatch,
            () => product.AddVariant(Name("Large"), Money.Of(2m, Currency.USD)));

        Assert.Empty(product.Variants);
    }

    [Fact]
    public void AddVariant_accepts_a_negative_delta_down_to_a_zero_price_and_rejects_below()
    {
        var product = NewProduct(10m);

        product.AddVariant(Name("Free"), Eur(-10m));

        DomainAssert.Throws(ProductErrors.VariantPriceNegative, () => product.AddVariant(Name("Too cheap"), Eur(-10.01m)));
        Assert.Single(product.Variants);
    }

    [Fact]
    public void RemoveVariant_removes_it_and_rejects_an_unknown_variant()
    {
        var product = NewProduct();
        var variantId = product.AddVariant(Name("Large"), Eur(2m));

        product.RemoveVariant(variantId);

        Assert.Empty(product.Variants);
        DomainAssert.Throws(ProductErrors.VariantNotFound, () => product.RemoveVariant(variantId));
    }

    [Fact]
    public void RemoveVariant_frees_its_name_and_sku()
    {
        var product = NewProduct();
        var variantId = product.AddVariant(Name("Large"), Eur(2m), Sku.Create("L"));
        product.RemoveVariant(variantId);

        product.AddVariant(Name("Large"), Eur(2m), Sku.Create("L"));

        Assert.Single(product.Variants);
    }

    [Fact]
    public void ChangeVariantPriceDelta_updates_the_delta()
    {
        var product = NewProduct(10m);
        var variantId = product.AddVariant(Name("Large"), Eur(2m));

        product.ChangeVariantPriceDelta(variantId, Eur(-10m), _clock);

        Assert.Equal(Eur(-10m), product.Variants.Single().PriceDelta);
        Assert.Equal(Eur(0m), product.PriceOf(variantId));
    }

    [Fact]
    public void ChangeVariantPriceDelta_raises_ProductVariantPriceChanged_only_on_a_real_change()
    {
        var product = ActiveProduct(10m);
        var variantId = product.AddVariant(Name("Large"), Eur(2m));
        _clock.Advance(TimeSpan.FromHours(1));

        product.ChangeVariantPriceDelta(variantId, Eur(2m), _clock);
        DomainAssert.NoEvents(product);

        product.ChangeVariantPriceDelta(variantId, Eur(3m), _clock);

        var changed = DomainAssert.Raised<ProductVariantPriceChanged>(product);
        Assert.Equal(Tenant, changed.TenantId);
        Assert.Equal(product.Id, changed.ProductId);
        Assert.Equal(variantId, changed.VariantId);
        Assert.Equal(Eur(2m), changed.PreviousPriceDelta);
        Assert.Equal(Eur(3m), changed.NewPriceDelta);
        Assert.Equal(Eur(13m), changed.NewPrice);
        Assert.Equal(_clock.UtcNow, changed.OccurredAtUtc);
    }

    [Fact]
    public void SetVariantAvailability_raises_ProductVariantAvailabilityChanged_only_on_a_real_change()
    {
        var product = ActiveProduct();
        var variantId = product.AddVariant(Name("Large"), Eur(2m));

        product.SetVariantAvailability(variantId, true, _clock);
        DomainAssert.NoEvents(product);

        product.SetVariantAvailability(variantId, false, _clock);
        product.SetVariantAvailability(variantId, false, _clock);

        var changed = DomainAssert.Raised<ProductVariantAvailabilityChanged>(product);
        Assert.Equal(Tenant, changed.TenantId);
        Assert.Equal(product.Id, changed.ProductId);
        Assert.Equal(variantId, changed.VariantId);
        Assert.False(changed.IsAvailable);
        Assert.Equal(_clock.UtcNow, changed.OccurredAtUtc);
    }

    [Fact]
    public void Variants_modifier_groups_and_allergens_are_bounded()
    {
        var product = NewProduct();
        for (var i = 0; i < Product.MaxVariants; i++)
        {
            product.AddVariant(Name($"Variant {i}"), Eur(0m));
        }

        DomainAssert.Throws(ProductErrors.TooManyVariants, () => product.AddVariant(Name("One too many"), Eur(0m)));
        Assert.Equal(Product.MaxVariants, product.Variants.Count);

        for (var i = 0; i < Product.MaxModifierGroups; i++)
        {
            product.AttachModifierGroup(GroupRef());
        }

        DomainAssert.Throws(ProductErrors.TooManyModifierGroups, () => product.AttachModifierGroup(GroupRef()));
        Assert.Equal(Product.MaxModifierGroups, product.ModifierGroupIds.Count);

        product.SetAllergens(AllergenCodes.All.Concat(AllergenCodes.All));
        Assert.Equal(AllergenCodes.All, product.Allergens);
    }

    [Fact]
    public void ChangeVariantPriceDelta_rejects_another_currency_a_negative_price_and_an_unknown_variant()
    {
        var product = NewProduct(10m);
        var variantId = product.AddVariant(Name("Large"), Eur(2m));

        DomainAssert.Throws(
            ProductErrors.VariantCurrencyMismatch,
            () => product.ChangeVariantPriceDelta(variantId, Money.Of(1m, Currency.USD), _clock));
        DomainAssert.Throws(
            ProductErrors.VariantPriceNegative,
            () => product.ChangeVariantPriceDelta(variantId, Eur(-10.01m), _clock));
        DomainAssert.Throws(
            ProductErrors.VariantNotFound,
            () => product.ChangeVariantPriceDelta(VariantId.New(), Eur(1m), _clock));

        Assert.Equal(Eur(2m), product.Variants.Single().PriceDelta);
    }

    [Fact]
    public void SetVariantAvailability_toggles_and_is_idempotent()
    {
        var product = NewProduct();
        var variantId = product.AddVariant(Name("Large"), Eur(2m));

        product.SetVariantAvailability(variantId, false, _clock);
        product.SetVariantAvailability(variantId, false, _clock);
        Assert.False(product.Variants.Single().IsAvailable);

        product.SetVariantAvailability(variantId, true, _clock);
        Assert.True(product.Variants.Single().IsAvailable);

        DomainAssert.Throws(ProductErrors.VariantNotFound, () => product.SetVariantAvailability(VariantId.New(), true, _clock));
    }

    // ---- Groupes de modificateurs ----

    [Fact]
    public void AttachModifierGroup_references_the_group_by_id()
    {
        var product = NewProduct();
        var group = GroupRef();

        product.AttachModifierGroup(group);

        Assert.Equal([group.Id], product.ModifierGroupIds);
    }

    [Fact]
    public void AttachModifierGroup_accepts_the_reference_of_a_real_group()
    {
        var product = NewProduct();
        var group = ModifierGroup.Create(Tenant, Name("Sauces"), Currency.EUR, SelectionRule.OptionalSingle);

        product.AttachModifierGroup(group.ToReference());

        Assert.Equal([group.Id], product.ModifierGroupIds);
    }

    [Fact]
    public void AttachModifierGroup_rejects_a_duplicate()
    {
        var product = NewProduct();
        var group = GroupRef();
        product.AttachModifierGroup(group);

        DomainAssert.Throws(ProductErrors.ModifierGroupAlreadyAttached, () => product.AttachModifierGroup(group));

        Assert.Single(product.ModifierGroupIds);
    }

    [Fact]
    public void AttachModifierGroup_rejects_a_group_of_another_tenant()
    {
        var product = NewProduct();

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => product.AttachModifierGroup(GroupRef(tenant: TenantId.New())));

        Assert.Empty(product.ModifierGroupIds);
    }

    [Fact]
    public void AttachModifierGroup_rejects_a_group_in_another_currency()
    {
        var product = NewProduct();

        DomainAssert.Throws(
            ProductErrors.ModifierGroupCurrencyMismatch,
            () => product.AttachModifierGroup(GroupRef(currency: Currency.XOF)));

        Assert.Empty(product.ModifierGroupIds);
    }

    [Fact]
    public void DetachModifierGroup_removes_it_and_rejects_a_group_that_is_not_attached()
    {
        var product = NewProduct();
        var group = GroupRef();
        product.AttachModifierGroup(group);

        product.DetachModifierGroup(group.Id);

        Assert.Empty(product.ModifierGroupIds);
        DomainAssert.Throws(ProductErrors.ModifierGroupNotAttached, () => product.DetachModifierGroup(group.Id));
    }

    // ---- Allergènes ----

    [Fact]
    public void SetAllergens_keeps_reference_codes_in_their_order_and_collapses_duplicates()
    {
        var product = NewProduct();
        product.SetAllergens([AllergenCodes.Milk]);

        product.SetAllergens([" Sesame ", AllergenCodes.Gluten, "GLUTEN", AllergenCodes.Eggs]);

        Assert.Equal([AllergenCodes.Gluten, AllergenCodes.Eggs, AllergenCodes.Sesame], product.Allergens);

        product.SetAllergens([]);
        Assert.Empty(product.Allergens);
    }

    [Fact]
    public void SetAllergens_rejects_an_unknown_code_and_keeps_the_previous_list()
    {
        var product = NewProduct();
        product.SetAllergens([AllergenCodes.Gluten]);

        DomainAssert.Throws(ProductErrors.AllergenUnknown, () => product.SetAllergens([AllergenCodes.Milk, "peanut-butter"]));
        DomainAssert.Throws(ProductErrors.AllergenUnknown, () => product.SetAllergens([null]));

        Assert.Equal([AllergenCodes.Gluten], product.Allergens);
    }

    // ---- Visibilité par canal ----

    [Fact]
    public void A_product_is_visible_everywhere_until_hidden_on_a_channel()
    {
        var product = NewProduct();

        product.SetHiddenChannels([OnlineChannel.Kiosk, OnlineChannel.Web, OnlineChannel.Kiosk]);

        Assert.Equal([OnlineChannel.Web, OnlineChannel.Kiosk], product.HiddenChannels);
        Assert.False(product.IsVisibleOn(OnlineChannel.Web));
        Assert.True(product.IsVisibleOn(OnlineChannel.MobileApp));
        DomainAssert.Throws(ProductErrors.ChannelInvalid, () => product.SetHiddenChannels([(OnlineChannel)9]));
        Assert.Equal([OnlineChannel.Web, OnlineChannel.Kiosk], product.HiddenChannels);

        product.SetHiddenChannels([]);
        Assert.True(product.IsVisibleOn(OnlineChannel.Kiosk));
    }

    [Fact]
    public void ToReference_carries_id_tenant_and_archived_flag()
    {
        var product = NewProduct();

        var reference = product.ToReference();

        Assert.Equal(product.Id, reference.Id);
        Assert.Equal(Tenant, reference.TenantId);
        Assert.False(reference.IsArchived);
    }

    [Fact]
    public void The_most_specific_price_wins_and_the_sale_uses_it()
    {
        var product = ActiveProduct(10m);
        var variant = product.AddVariant(Name("Grand"), Eur(2m));
        var here = LocationId.New();
        var location = new LocationReference(Tenant, here, true);

        product.SetPriceOverride(null, PriceChannel.Web, Eur(12m), _clock);
        product.SetPriceOverride(location, null, Eur(9m), _clock);
        product.SetPriceOverride(location, PriceChannel.Web, Eur(11m), _clock);
        product.SetPriceOverride(location, PriceChannel.Web, Eur(11m), _clock);

        Assert.Equal(Eur(11m), product.BasePriceFor(here, PriceChannel.Web));
        Assert.Equal(Eur(9m), product.BasePriceFor(here, PriceChannel.Kiosk));
        Assert.Equal(Eur(12m), product.BasePriceFor(LocationId.New(), PriceChannel.Web));
        Assert.Equal(Eur(10m), product.BasePriceFor(null, PriceChannel.InHouse));
        Assert.Equal(Eur(13m), product.DescribeForSale(variant, here, PriceChannel.Web).UnitPrice);
        Assert.Equal(3, product.DomainEvents.OfType<ProductPriceOverrideChanged>().Count());
        product.SetPriceOverride(null, PriceChannel.Kiosk, Eur(0.5m), _clock);
        DomainAssert.Throws(ProductErrors.VariantPriceNegative, () => { product.AddVariant(Name("Moins"), Eur(-1m)); });
        DomainAssert.Throws(CommonErrors.TenantMismatch, () => product.SetPriceOverride(new LocationReference(TenantId.New(), here, true), null, Eur(9m), _clock));

        product.RemovePriceOverride(here, PriceChannel.Web, _clock);
        Assert.Equal(Eur(9m), product.BasePriceFor(here, PriceChannel.Web));
        DomainAssert.Throws(ProductErrors.PriceOverrideNotFound, () => product.RemovePriceOverride(here, PriceChannel.Web, _clock));
    }
}
