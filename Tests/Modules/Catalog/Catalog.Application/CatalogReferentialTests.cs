using Catalog.Application.Commands;
using Catalog.Application.Queries;
using Catalog.Domain;

namespace Catalog.Application;

/// <summary>Catégories fiscales et groupes d'options : les référentiels partagés par les produits.</summary>
public sealed class CatalogReferentialTests
{
    private readonly CatalogFixture _fixture = new();

    [Fact]
    public async Task A_tax_code_is_unique_within_a_tenant_archived_ones_included_but_free_in_another()
    {
        var first = await _fixture.GivenTaxCategoryAsync(code: "tva_18");
        await _fixture.ArchiveTaxCategory.HandleAsync(new ArchiveTaxCategoryCommand(first));

        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.CreateTaxCategory.HandleAsync(new CreateTaxCategoryCommand("TVA_18", "Encore", 0.18m)));
        await _fixture.GivenTaxCategoryAsync(_fixture.TenantB, code: "TVA_18");

        Assert.Equal(TaxCategoryErrors.CodeNotUnique, refused.Error);
        Assert.Equal("TVA_18", _fixture.TaxCategories.Stored(first).Code.Value);
        Assert.Equal(2, _fixture.TaxCategories.Items.Count);
    }

    [Fact]
    public async Task Tax_categories_are_listed_by_code_and_archived_ones_only_on_request()
    {
        await _fixture.GivenTaxCategoryAsync(code: "TVA_18");
        var archived = await _fixture.GivenTaxCategoryAsync(code: "EXEMPT", rate: 0m);
        var reduced = await _fixture.GivenTaxCategoryAsync(code: "TVA_10", rate: 0.10m);
        await _fixture.GivenTaxCategoryAsync(_fixture.TenantB, code: "AAA");
        await _fixture.ArchiveTaxCategory.HandleAsync(new ArchiveTaxCategoryCommand(archived));
        await _fixture.ChangeTaxRate.HandleAsync(new ChangeTaxRateCommand(reduced, 0.105m));

        var active = await _fixture.ListTaxCategories.HandleAsync(new ListTaxCategoriesQuery());
        var all = await _fixture.ListTaxCategories.HandleAsync(new ListTaxCategoriesQuery(IncludeArchived: true));

        Assert.Equal(new[] { ("TVA_10", 0.105m), ("TVA_18", 0.18m) }, active.Select(c => (c.Code, c.Rate)).ToArray());
        Assert.Equal(new[] { "EXEMPT", "TVA_10", "TVA_18" }, all.Select(c => c.Code).ToArray());
        Assert.Equal(TaxCategoryStatus.Archived, all[0].Status);
    }

    [Fact]
    public async Task A_modifier_group_takes_the_tenant_currency_and_its_prices_follow_it()
    {
        var group = await _fixture.GivenModifierGroupAsync(_fixture.TenantB, min: 1, max: 2);
        _fixture.CurrentTenantId = _fixture.TenantB;

        var fries = await _fixture.AddModifier.HandleAsync(new AddModifierCommand(group, "Frites", 2.50m));
        await _fixture.AddModifier.HandleAsync(new AddModifierCommand(group, "Salade", 0m));
        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ChangeModifierPrice.HandleAsync(new ChangeModifierPriceCommand(group, fries, 2.555m)));
        await _fixture.SetModifierAvailability.HandleAsync(new SetModifierAvailabilityCommand(group, fries, false));

        var read = await _fixture.GetModifierGroup.HandleAsync(new GetModifierGroupByIdQuery(group));

        Assert.Equal(CommonErrors.MoneyPrecision, refused.Error);
        Assert.Equal("EUR", read.CurrencyCode);
        Assert.Equal((1, 2), (read.MinSelections, read.MaxSelections));
        Assert.Equal(
            new[] { ("Frites", 2.50m, false), ("Salade", 0m, true) },
            read.Modifiers.Select(m => (m.Name, m.PriceDelta, m.IsAvailable)).ToArray());
        Assert.True(read.IsSatisfiable);
    }

    [Fact]
    public async Task A_modifier_group_of_another_tenant_is_not_found()
    {
        var ofB = await _fixture.GivenModifierGroupAsync(_fixture.TenantB);

        var read = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.GetModifierGroup.HandleAsync(new GetModifierGroupByIdQuery(ofB)));
        var write = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.AddModifier.HandleAsync(new AddModifierCommand(ofB, "Piment", 0m)));

        Assert.Equal(CatalogApplicationErrors.ModifierGroupNotFound, read.Error);
        Assert.Equal(CatalogApplicationErrors.ModifierGroupNotFound, write.Error);
        Assert.Empty(_fixture.ModifierGroups.Stored(ofB).Modifiers);
    }
}
