namespace Catalog.Domain;

public class TaxCategoryTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private readonly FakeClock _clock = new();

    private static TaxCategoryCode Code(string value = "VAT_STD") => TaxCategoryCode.Create(value);

    private static TaxCategoryName Name(string value = "Standard VAT") => TaxCategoryName.Create(value);

    private static TaxRate Rate(decimal value = 0.10m) => TaxRate.Create(value);

    private TaxCategory NewCategory()
    {
        var category = TaxCategory.Create(Tenant, Code(), Name(), Rate(), isCodeUniqueInTenant: true, _clock);
        category.ClearDomainEvents();
        return category;
    }

    private TaxCategory ArchivedCategory()
    {
        var category = NewCategory();
        category.Archive(_clock);
        category.ClearDomainEvents();
        return category;
    }

    // ---- Value objects ----

    [Fact]
    public void Code_create_trims_upper_cases_and_accepts_the_whole_charset()
    {
        Assert.Equal("VAT_STD-10", Code("  vat_std-10 ").Value);
        Assert.Equal(Code("vat"), Code("VAT"));
        Assert.Equal(TaxCategoryCode.MaxLength, Code(new string('A', TaxCategoryCode.MaxLength)).Value.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("VAT STD")]
    [InlineData("VAT.STD")]
    [InlineData("TVA_É")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Code_create_rejects_an_invalid_value(string? value) =>
        DomainAssert.Throws(TaxCategoryErrors.CodeInvalid, () => TaxCategoryCode.Create(value));

    [Fact]
    public void Name_create_trims_and_accepts_the_maximum_length_and_rejects_one_more()
    {
        Assert.Equal("Standard VAT", Name("  Standard VAT ").Value);
        Assert.Equal(TaxCategoryName.MaxLength, Name(new string('x', TaxCategoryName.MaxLength)).Value.Length);
        DomainAssert.Throws(TaxCategoryErrors.NameTooLong, () => Name(new string('x', TaxCategoryName.MaxLength + 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_create_rejects_an_empty_value(string? value) =>
        DomainAssert.Throws(TaxCategoryErrors.NameEmpty, () => TaxCategoryName.Create(value));

    [Theory]
    [InlineData("0")]
    [InlineData("0.10")]
    [InlineData("0.055")]
    [InlineData("0.123456")]
    [InlineData("1")]
    public void Rate_create_accepts_a_fraction_between_0_and_1_with_at_most_6_decimals(string value)
    {
        var rate = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(rate, TaxRate.Create(rate).Value);
    }

    [Theory]
    [InlineData("-0.000001")]
    [InlineData("1.000001")]
    [InlineData("10")]
    public void Rate_create_rejects_a_value_outside_0_to_1(string value) =>
        DomainAssert.Throws(
            TaxCategoryErrors.RateOutOfRange,
            () => TaxRate.Create(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Rate_create_rejects_more_than_6_decimals() =>
        DomainAssert.Throws(TaxCategoryErrors.RateTooPrecise, () => TaxRate.Create(0.1234567m));

    [Fact]
    public void Rate_ignores_trailing_zeros_when_compared() =>
        Assert.Equal(TaxRate.Create(0.1m), TaxRate.Create(0.100000m));

    // ---- Création (Create) ----

    [Fact]
    public void Create_builds_an_active_category_and_raises_TaxCategoryCreated()
    {
        var category = TaxCategory.Create(Tenant, Code(), Name(), Rate(), isCodeUniqueInTenant: true, _clock);

        Assert.Equal(Tenant, category.TenantId);
        Assert.Equal("VAT_STD", category.Code.Value);
        Assert.Equal("Standard VAT", category.Name.Value);
        Assert.Equal(0.10m, category.Rate.Value);
        Assert.Equal(TaxCategoryStatus.Active, category.Status);
        Assert.False(category.IsArchived);

        var created = DomainAssert.Raised<TaxCategoryCreated>(category);
        Assert.Equal(Tenant, created.TenantId);
        Assert.Equal(category.Id, created.TaxCategoryId);
        Assert.Equal("VAT_STD", created.Code);
        Assert.Equal("Standard VAT", created.Name);
        Assert.Equal(0.10m, created.Rate);
        Assert.Equal(_clock.UtcNow, created.OccurredAtUtc);
    }

    [Fact]
    public void Create_accepts_a_zero_rate() =>
        Assert.Equal(TaxRate.Zero, TaxCategory.Create(Tenant, Code("EXEMPT"), Name("Exempt"), TaxRate.Zero, true, _clock).Rate);

    [Fact]
    public void Create_rejects_a_code_already_used_in_the_tenant() =>
        DomainAssert.Throws(
            TaxCategoryErrors.CodeNotUnique,
            () => TaxCategory.Create(Tenant, Code(), Name(), Rate(), isCodeUniqueInTenant: false, _clock));

    [Fact]
    public void Create_rejects_an_empty_tenant() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => TaxCategory.Create(default, Code(), Name(), Rate(), true, _clock));

    // ---- Renommage (Rename) ----

    [Fact]
    public void Rename_changes_the_name_without_event()
    {
        var category = NewCategory();

        category.Rename(Name("Reduced VAT"));

        Assert.Equal("Reduced VAT", category.Name.Value);
        Assert.Equal("VAT_STD", category.Code.Value);
        DomainAssert.NoEvents(category);
    }

    [Fact]
    public void Rename_to_the_same_name_is_a_no_op()
    {
        var category = NewCategory();
        var name = category.Name;

        category.Rename(Name("Standard VAT"));

        Assert.Same(name, category.Name);
        DomainAssert.NoEvents(category);
    }

    // ---- Changement de taux (ChangeRate) ----

    [Fact]
    public void ChangeRate_updates_the_rate_and_raises_TaxCategoryRateChanged()
    {
        var category = NewCategory();
        _clock.Advance(TimeSpan.FromDays(1));

        category.ChangeRate(Rate(0.2m), _clock);

        Assert.Equal(0.2m, category.Rate.Value);
        var changed = DomainAssert.Raised<TaxCategoryRateChanged>(category);
        Assert.Equal(Tenant, changed.TenantId);
        Assert.Equal(category.Id, changed.TaxCategoryId);
        Assert.Equal("VAT_STD", changed.Code);
        Assert.Equal(0.10m, changed.PreviousRate);
        Assert.Equal(0.2m, changed.NewRate);
        Assert.Equal(_clock.UtcNow, changed.OccurredAtUtc);
    }

    [Fact]
    public void ChangeRate_to_the_same_rate_is_a_no_op()
    {
        var category = NewCategory();

        category.ChangeRate(Rate(0.100m), _clock);

        Assert.Equal(0.10m, category.Rate.Value);
        DomainAssert.NoEvents(category);
    }

    [Fact]
    public void ChangeRate_does_not_alter_a_reference_taken_before()
    {
        var category = NewCategory();
        var soldWith = category.ToReference();

        category.ChangeRate(Rate(0.2m), _clock);

        Assert.Equal(0.10m, soldWith.Rate);
        Assert.Equal(0.2m, category.ToReference().Rate);
    }

    // ---- Archivage (Archive) ----

    [Fact]
    public void Archive_moves_the_category_to_archived_and_raises_TaxCategoryArchived()
    {
        var category = NewCategory();

        category.Archive(_clock);

        Assert.Equal(TaxCategoryStatus.Archived, category.Status);
        Assert.True(category.IsArchived);
        Assert.True(category.ToReference().IsArchived);
        var archived = DomainAssert.Raised<TaxCategoryArchived>(category);
        Assert.Equal(Tenant, archived.TenantId);
        Assert.Equal(category.Id, archived.TaxCategoryId);
        Assert.Equal("VAT_STD", archived.Code);
        Assert.Equal(_clock.UtcNow, archived.OccurredAtUtc);
    }

    [Fact]
    public void Archived_category_is_terminal_and_rejects_every_mutation()
    {
        var category = ArchivedCategory();

        DomainAssert.Throws(TaxCategoryErrors.Archived, () => category.Rename(Name("Other")));
        DomainAssert.Throws(TaxCategoryErrors.Archived, () => category.Rename(Name("Standard VAT")));
        DomainAssert.Throws(TaxCategoryErrors.Archived, () => category.ChangeRate(Rate(0.2m), _clock));
        DomainAssert.Throws(TaxCategoryErrors.Archived, () => category.Archive(_clock));

        Assert.Equal("Standard VAT", category.Name.Value);
        Assert.Equal(0.10m, category.Rate.Value);
        DomainAssert.NoEvents(category);
    }

    [Fact]
    public void Archived_category_cannot_be_assigned_to_a_product()
    {
        var category = ArchivedCategory();

        DomainAssert.Throws(
            ProductErrors.TaxCategoryArchived,
            () => Product.Create(Tenant, CatalogName.Create("Burger"), null, Money.Of(1m, Currency.EUR), category.ToReference(), _clock));
    }

    // ---- Référence (Reference) ----

    [Fact]
    public void ToReference_carries_tenant_id_code_name_rate_and_archived_flag()
    {
        var category = NewCategory();

        var reference = category.ToReference();

        Assert.Equal(category.Id, reference.Id);
        Assert.Equal(Tenant, reference.TenantId);
        Assert.Equal("VAT_STD", reference.Code);
        Assert.Equal("Standard VAT", reference.Name);
        Assert.Equal(0.10m, reference.Rate);
        Assert.False(reference.IsArchived);
    }
}
