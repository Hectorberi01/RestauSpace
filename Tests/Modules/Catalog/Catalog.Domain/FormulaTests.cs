namespace Catalog.Domain;

public class FormulaTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private readonly FakeClock _clock = new();

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.EUR);

    private static CatalogName Name(string value) => CatalogName.Create(value);

    private Product NewProduct(string name, decimal price = 10m, TenantId? tenant = null) =>
        Product.Create(tenant ?? Tenant, Name(name), null, Eur(price), CatalogRefs.Tax(tenant ?? Tenant, false), _clock);

    private static DomainError Error(Action action) => Assert.Throws<DomainException>(action).Error;

    private (Product Formula, FormulaStep Main, FormulaStep Drink, Product Chicken, Product Fish, Product Juice) Lunch()
    {
        var formula = NewProduct("Menu midi", 12m);
        var chicken = NewProduct("Poulet");
        var fish = NewProduct("Poisson");
        var juice = NewProduct("Bissap", 2m);
        var main = FormulaStep.Create(null, Name("Plat"), 1, 1, [chicken.OfferInFormula(null, Eur(0m)), fish.OfferInFormula(null, Eur(2m))]);
        var drink = FormulaStep.Create(null, Name("Boisson"), 0, 1, [juice.OfferInFormula(null, Eur(0m))]);
        formula.SetFormulaSteps([main, drink]);
        return (formula, main, drink, chicken, fish, juice);
    }

    [Fact]
    public void Steps_make_a_product_a_formula_and_an_empty_list_reverts_it()
    {
        var (formula, _, _, _, _, _) = Lunch();
        Assert.True(formula.IsFormula);
        Assert.Equal(2, formula.FormulaSteps.Count);

        formula.SetFormulaSteps([]);

        Assert.False(formula.IsFormula);
    }

    [Fact]
    public void A_step_validates_its_choices_and_its_selection_range()
    {
        var chicken = NewProduct("Poulet");
        var offer = chicken.OfferInFormula(null, Eur(0m));

        Assert.Equal(FormulaErrors.ChoicesRequired, Error(() => FormulaStep.Create(null, Name("Plat"), 1, 1, [])));
        Assert.Equal(FormulaErrors.ChoiceDuplicate, Error(() => FormulaStep.Create(null, Name("Plat"), 1, 1, [offer, offer])));
        Assert.Equal(FormulaErrors.SelectionRangeInvalid, Error(() => FormulaStep.Create(null, Name("Plat"), 2, 2, [offer])));
        Assert.Equal(FormulaErrors.SelectionRangeInvalid, Error(() => FormulaStep.Create(null, Name("Plat"), 1, 0, [offer])));
        Assert.Equal(FormulaErrors.SelectionRangeInvalid, Error(() => FormulaStep.Create(null, Name("Plat"), 0, 11, [offer])));
    }

    [Fact]
    public void A_product_offered_must_be_active_or_draft_ordinary_and_priced_with_a_positive_supplement()
    {
        var (formula, _, _, _, _, _) = Lunch();
        var archived = NewProduct("Ancien");
        archived.Archive(_clock);
        var sized = NewProduct("Pizza");
        sized.AddVariant(Name("Grande"), Eur(3m));

        Assert.Equal(FormulaErrors.NestedFormula, Error(() => formula.OfferInFormula(null, Eur(0m))));
        Assert.Equal(FormulaErrors.ChoiceArchived, Error(() => archived.OfferInFormula(null, Eur(0m))));
        Assert.Equal(FormulaErrors.SupplementInvalid, Error(() => NewProduct("Riz").OfferInFormula(null, Eur(-1m))));
        Assert.Equal(ProductErrors.VariantRequired, Error(() => sized.OfferInFormula(null, Eur(0m))));
        Assert.NotNull(sized.OfferInFormula(sized.Variants.Single().Id, Eur(1m)));
    }

    [Fact]
    public void A_formula_refuses_itself_another_tenant_duplicate_step_names_and_variants()
    {
        var formula = NewProduct("Menu");
        var self = FormulaStep.Create(null, Name("Plat"), 1, 1, [formula.OfferInFormula(null, Eur(0m))]);
        Assert.Equal(FormulaErrors.NestedFormula, Error(() => formula.SetFormulaSteps([self])));

        var foreign = NewProduct("Ailleurs", tenant: TenantId.New());
        var other = FormulaStep.Create(null, Name("Plat"), 1, 1, [foreign.OfferInFormula(null, Eur(0m))]);
        Assert.ThrowsAny<Exception>(() => formula.SetFormulaSteps([other]));

        var rice = NewProduct("Riz");
        var a = FormulaStep.Create(null, Name("Plat"), 1, 1, [rice.OfferInFormula(null, Eur(0m))]);
        var b = FormulaStep.Create(null, Name("plat"), 1, 1, [rice.OfferInFormula(null, Eur(0m))]);
        Assert.Equal(FormulaErrors.StepNameDuplicate, Error(() => formula.SetFormulaSteps([a, b])));

        formula.SetFormulaSteps([a]);
        Assert.Equal(FormulaErrors.WithVariants, Error(() => formula.AddVariant(Name("Grande"), Eur(1m))));
    }

    [Fact]
    public void A_composition_respects_each_step_and_its_counts()
    {
        var (formula, main, drink, chicken, fish, juice) = Lunch();

        var chosen = formula.ChooseInFormula([(main.Id, fish.Id, null), (drink.Id, juice.Id, null)]);
        Assert.Equal(2m, chosen[0].Choice.Supplement.Amount);
        Assert.Single(formula.ChooseInFormula([(main.Id, chicken.Id, null)]));

        Assert.Equal(FormulaErrors.SelectionCountInvalid, Error(() => formula.ChooseInFormula([(drink.Id, juice.Id, null)])));
        Assert.Equal(FormulaErrors.SelectionCountInvalid, Error(() => formula.ChooseInFormula([(main.Id, chicken.Id, null), (main.Id, fish.Id, null)])));
        Assert.Equal(FormulaErrors.ChoiceNotOffered, Error(() => formula.ChooseInFormula([(main.Id, juice.Id, null)])));
        Assert.Equal(FormulaErrors.StepNotFound, Error(() => formula.ChooseInFormula([(Guid.NewGuid(), chicken.Id, null)])));
        Assert.Equal(FormulaErrors.NotAFormula, Error(() => chicken.ChooseInFormula([(main.Id, fish.Id, null)])));
    }
}
