using Catalog.Application.Commands;
using Catalog.Application.Queries;
using Catalog.Domain;

namespace Catalog.Application;

/// <summary>Ce que le Catalog fige pour une ligne de commande (module Orders) : vente autorisée, prix, taxe, options.</summary>
public sealed class SaleQueryTests
{
    private readonly CatalogFixture _fixture = new();

    private GetSellableItemHandler Sellable => new(_fixture.Workspace);

    [Fact]
    public async Task An_active_product_is_described_with_its_variant_price_tax_and_chosen_options()
    {
        var product = await _fixture.GivenProductAsync(basePrice: 3500m);
        var large = await _fixture.AddVariant.HandleAsync(new AddProductVariantCommand(product, "Grande", 1500m));
        var cooking = await _fixture.GivenModifierGroupAsync(min: 1, max: 1);
        var rare = await _fixture.AddModifier.HandleAsync(new AddModifierCommand(cooking, "Saignant", 0m));
        await _fixture.AddModifier.HandleAsync(new AddModifierCommand(cooking, "À point", 0m));
        var extras = await _fixture.GivenModifierGroupAsync(min: 0, max: 2);
        var fries = await _fixture.AddModifier.HandleAsync(new AddModifierCommand(extras, "Frites", 500m));
        await _fixture.AttachModifierGroup.HandleAsync(new AttachModifierGroupCommand(product, cooking));
        await _fixture.AttachModifierGroup.HandleAsync(new AttachModifierGroupCommand(product, extras));
        await _fixture.ActivateProduct.HandleAsync(new ActivateProductCommand(product));

        var sale = await Sellable.HandleAsync(new GetSellableItemQuery(product, large, [fries, rare]));

        Assert.Equal(("Poulet DG", "Grande", 5000m, "XOF"), (sale.ProductName, sale.VariantName, sale.UnitPrice, sale.CurrencyCode));
        Assert.Equal(0.18m, sale.TaxRate);
        Assert.Equal(new[] { ("Frites", 500m), ("Saignant", 0m) }, sale.Modifiers.Select(m => (m.Name, m.PriceDelta)).ToArray());
    }

    [Fact]
    public async Task A_sale_is_refused_when_the_product_or_the_choice_of_options_does_not_allow_it()
    {
        var product = await _fixture.GivenProductAsync();
        var cooking = await _fixture.GivenModifierGroupAsync(min: 1, max: 1);
        var rare = await _fixture.AddModifier.HandleAsync(new AddModifierCommand(cooking, "Saignant", 0m));
        var other = await _fixture.GivenModifierGroupAsync();
        var foreign = await _fixture.AddModifier.HandleAsync(new AddModifierCommand(other, "Piment", 0m));
        await _fixture.AttachModifierGroup.HandleAsync(new AttachModifierGroupCommand(product, cooking));

        var draft = await Assert.ThrowsAsync<DomainException>(() => Sellable.HandleAsync(new GetSellableItemQuery(product, null, [rare])));
        await _fixture.ActivateProduct.HandleAsync(new ActivateProductCommand(product));
        var missing = await Assert.ThrowsAsync<DomainException>(() => Sellable.HandleAsync(new GetSellableItemQuery(product)));
        var notOffered = await Assert.ThrowsAsync<DomainException>(
            () => Sellable.HandleAsync(new GetSellableItemQuery(product, null, [rare, foreign])));
        var ofB = await _fixture.GivenProductAsync(_fixture.TenantB, basePrice: 9m);
        var otherTenant = await Assert.ThrowsAsync<NotFoundException>(() => Sellable.HandleAsync(new GetSellableItemQuery(ofB)));

        Assert.Equal(ProductErrors.NotSellable, draft.Error);
        Assert.Equal(ModifierGroupErrors.SelectionBelowMin, missing.Error);
        Assert.Equal(CatalogApplicationErrors.ModifierNotOffered, notOffered.Error);
        Assert.Equal(CatalogApplicationErrors.ProductNotFound, otherTenant.Error);
    }
}
