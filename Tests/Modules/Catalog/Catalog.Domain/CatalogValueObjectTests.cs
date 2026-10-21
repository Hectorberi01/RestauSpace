namespace Catalog.Domain;

public class CatalogValueObjectTests
{
    [Fact]
    public void CatalogName_create_trims_the_value()
    {
        Assert.Equal("Burger", CatalogName.Create("  Burger ").Value);
        Assert.Equal("Burger", CatalogName.Create("Burger").ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CatalogName_create_rejects_an_empty_value(string? value) =>
        DomainAssert.Throws(CatalogErrors.NameEmpty, () => CatalogName.Create(value));

    [Fact]
    public void CatalogName_create_accepts_the_maximum_length_and_rejects_one_more()
    {
        Assert.Equal(CatalogName.MaxLength, CatalogName.Create(new string('a', CatalogName.MaxLength)).Value.Length);
        DomainAssert.Throws(CatalogErrors.NameTooLong, () => CatalogName.Create(new string('a', CatalogName.MaxLength + 1)));
    }

    [Fact]
    public void CatalogName_equality_is_case_sensitive_but_matches_is_not()
    {
        Assert.Equal(CatalogName.Create("Large"), CatalogName.Create("Large"));
        Assert.NotEqual(CatalogName.Create("Large"), CatalogName.Create("LARGE"));
        Assert.True(CatalogName.Create("Large").Matches(CatalogName.Create("LARGE")));
        Assert.False(CatalogName.Create("Large").Matches(CatalogName.Create("Small")));
    }

    [Fact]
    public void Sku_create_trims_and_upper_cases()
    {
        Assert.Equal("BRG-01_X.2", Sku.Create(" brg-01_x.2 ").Value);
        Assert.Equal(Sku.Create("abc"), Sku.Create("ABC"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("AB CD")]
    [InlineData("AB/CD")]
    [InlineData("é1")]
    public void Sku_create_rejects_an_invalid_value(string? value) =>
        DomainAssert.Throws(CatalogErrors.SkuInvalid, () => Sku.Create(value));

    [Fact]
    public void Sku_create_accepts_the_maximum_length_and_rejects_one_more()
    {
        Assert.Equal(Sku.MaxLength, Sku.Create(new string('A', Sku.MaxLength)).Value.Length);
        DomainAssert.Throws(CatalogErrors.SkuInvalid, () => Sku.Create(new string('A', Sku.MaxLength + 1)));
    }

    [Fact]
    public void Module_ids_reject_an_empty_guid_and_round_trip()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => ModifierGroupId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => VariantId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => MenuId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => TaxCategoryId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => MenuSectionId.From(Guid.Empty));

        var id = MenuId.New();
        Assert.Equal(id, MenuId.From(id.Value));
        Assert.NotEqual(MenuId.New(), MenuId.New());
    }
}
