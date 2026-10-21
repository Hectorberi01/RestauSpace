using Catalog.Application.Commands;
using Catalog.Application.Queries;
using Catalog.Domain;

namespace Catalog.Application;

public sealed class FormulaUseCaseTests
{
    private readonly CatalogFixture _fixture = new();

    private GetSellableItemHandler Sellable => new(_fixture.Workspace);

    private SetProductFormulaHandler SetFormula => new(_fixture.Workspace);

    private async Task<(Guid Formula, Guid Step, Guid Chicken, Guid Fish, Guid Extra)> GivenLunchAsync()
    {
        var formula = await _fixture.GivenProductAsync(name: "Menu midi", basePrice: 4000m);
        var chicken = await _fixture.GivenProductAsync(name: "Poulet");
        var fish = await _fixture.GivenProductAsync(name: "Poisson");
        var sauces = await _fixture.GivenModifierGroupAsync(min: 0, max: 1);
        var extra = await _fixture.AddModifier.HandleAsync(new AddModifierCommand(sauces, "Sauce arachide", 200m));
        await _fixture.AttachModifierGroup.HandleAsync(new AttachModifierGroupCommand(fish, sauces));
        foreach (var product in new[] { chicken, fish, formula })
        {
            await _fixture.ActivateProduct.HandleAsync(new ActivateProductCommand(product));
        }

        await SetFormula.HandleAsync(new SetProductFormulaCommand(formula,
            [new FormulaStepInput(null, "Plat", 1, 1, [new FormulaChoiceInput(chicken), new FormulaChoiceInput(fish, Supplement: 500m)])]));
        var step = (await _fixture.GetProduct.HandleAsync(new GetProductByIdQuery(formula))).FormulaSteps!.Single().Id;
        return (formula, step, chicken, fish, extra);
    }

    [Fact]
    public async Task A_formula_is_sold_with_its_components_their_supplement_and_their_own_options()
    {
        var (formula, step, _, fish, extra) = await GivenLunchAsync();

        var sale = await Sellable.HandleAsync(new GetSellableItemQuery(formula, Components: [new SaleComponentInput(step, fish, ModifierIds: [extra])]));

        Assert.Equal(4000m, sale.UnitPrice);
        var component = Assert.Single(sale.Components!);
        Assert.Equal(("Plat", "Poisson", 500m), (component.StepName, component.ProductName, component.Supplement));
        Assert.Equal("Sauce arachide", Assert.Single(component.Modifiers).Name);
    }

    [Fact]
    public async Task A_composition_outside_the_formula_is_refused()
    {
        var (formula, step, chicken, _, extra) = await GivenLunchAsync();

        var missing = await Assert.ThrowsAsync<DomainException>(() => Sellable.HandleAsync(new GetSellableItemQuery(formula)));
        var foreignOption = await Assert.ThrowsAsync<DomainException>(
            () => Sellable.HandleAsync(new GetSellableItemQuery(formula, Components: [new SaleComponentInput(step, chicken, ModifierIds: [extra])])));

        Assert.Equal(FormulaErrors.SelectionCountInvalid, missing.Error);
        Assert.Equal(CatalogApplicationErrors.ModifierNotOffered, foreignOption.Error);
    }

    [Fact]
    public async Task A_formula_cannot_offer_another_formula_nor_a_product_of_another_tenant()
    {
        var (formula, _, _, _, _) = await GivenLunchAsync();
        var other = await _fixture.GivenProductAsync(name: "Menu soir");
        var ofB = await _fixture.GivenProductAsync(_fixture.TenantB, name: "Ailleurs");

        var nested = await Assert.ThrowsAsync<DomainException>(() => SetFormula.HandleAsync(
            new SetProductFormulaCommand(other, [new FormulaStepInput(null, "Menu", 1, 1, [new FormulaChoiceInput(formula)])])));
        await Assert.ThrowsAnyAsync<Exception>(() => SetFormula.HandleAsync(
            new SetProductFormulaCommand(other, [new FormulaStepInput(null, "Plat", 1, 1, [new FormulaChoiceInput(ofB)])])));

        Assert.Equal(FormulaErrors.NestedFormula, nested.Error);
    }
}
