namespace Catalog.Domain;

public static class FormulaErrors
{
    public static readonly DomainError StepsRequired = new("catalog.formula.steps_required", "A set menu has at least one step.");
    public static readonly DomainError TooManySteps = new("catalog.formula.too_many_steps", "A set menu has at most 10 steps.");
    public static readonly DomainError StepNameDuplicate = new("catalog.formula.step_name_duplicate", "Two steps of a set menu cannot have the same name.");
    public static readonly DomainError ChoicesRequired = new("catalog.formula.choices_required", "Each step offers at least one product.");
    public static readonly DomainError TooManyChoices = new("catalog.formula.too_many_choices", "A step offers at most 40 products.");
    public static readonly DomainError ChoiceDuplicate = new("catalog.formula.choice_duplicate", "A product is offered only once in a step.");
    public static readonly DomainError SelectionRangeInvalid =
        new("catalog.formula.selection_range_invalid", "Each step asks for 0 to 10 products, the minimum at most the maximum and the number of products offered.");
    public static readonly DomainError SupplementInvalid =
        new("catalog.formula.supplement_invalid", "A supplement is zero or more, in the set menu's currency.");
    public static readonly DomainError NestedFormula = new("catalog.formula.nested", "A set menu cannot offer another set menu, nor itself.");
    public static readonly DomainError ChoiceArchived = new("catalog.formula.choice_archived", "An archived product cannot be offered in a set menu.");
    public static readonly DomainError WithVariants = new("catalog.formula.with_variants", "A set menu has no variants: its price is fixed, supplements aside.");
    public static readonly DomainError StepNotFound = new("catalog.formula.step_not_found", "This step is not part of the set menu.");
    public static readonly DomainError ChoiceNotOffered = new("catalog.formula.choice_not_offered", "This product is not offered at this step of the set menu.");
    public static readonly DomainError SelectionCountInvalid =
        new("catalog.formula.selection_count_invalid", "The number of products chosen at a step is outside what it asks for.");
    public static readonly DomainError NotAFormula = new("catalog.formula.not_a_formula", "Only a set menu has products to choose.");
}

/// <summary>
/// Un produit proposé à une étape d'une formule, avec sa variante s'il en a et son supplément éventuel (ajouté au prix
/// de la formule). Obtenu par <see cref="Product.OfferInFormula"/>, qui vérifie le produit.
/// </summary>
public sealed record FormulaChoice
{
    internal FormulaChoice(TenantId tenantId, ProductId productId, VariantId? variantId, Money supplement)
    {
        TenantId = tenantId;
        ProductId = productId;
        VariantId = variantId;
        Supplement = supplement;
    }

    public TenantId TenantId { get; }

    public ProductId ProductId { get; }

    public VariantId? VariantId { get; }

    public Money Supplement { get; }

    internal bool Offers(ProductId productId, VariantId? variantId) => ProductId == productId && VariantId == variantId;

    /// <summary>Recharge une ligne stockée (même tenant que la formule, vérifié à l'écriture).</summary>
    public static FormulaChoice Restore(TenantId tenantId, ProductId productId, VariantId? variantId, Money supplement) =>
        new(tenantId, productId, variantId, supplement);
}

/// <summary>
/// Une étape d'une formule (« Plat », « Boisson ») : de <see cref="MinChoices"/> à <see cref="MaxChoices"/> produits à
/// choisir parmi <see cref="Choices"/>. Une étape à minimum zéro est facultative.
/// </summary>
public sealed record FormulaStep
{
    public const int MaxSelections = 10;
    public const int MaxChoicesPerStep = 40;

    private FormulaStep(Guid id, CatalogName name, int minChoices, int maxChoices, IReadOnlyList<FormulaChoice> choices)
    {
        Id = id;
        Name = name;
        MinChoices = minChoices;
        MaxChoices = maxChoices;
        Choices = choices;
    }

    public Guid Id { get; }

    public CatalogName Name { get; }

    public int MinChoices { get; }

    public int MaxChoices { get; }

    public IReadOnlyList<FormulaChoice> Choices { get; }

    /// <param name="id">Null pour une nouvelle étape ; l'id d'une étape existante pour la garder.</param>
    public static FormulaStep Create(Guid? id, CatalogName name, int minChoices, int maxChoices, IReadOnlyList<FormulaChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(choices);
        DomainException.ThrowIf(choices.Count == 0, FormulaErrors.ChoicesRequired);
        DomainException.ThrowIf(choices.Count > MaxChoicesPerStep, FormulaErrors.TooManyChoices);
        DomainException.ThrowIf(
            choices.Select(choice => (choice.ProductId, choice.VariantId)).Distinct().Count() != choices.Count, FormulaErrors.ChoiceDuplicate);
        DomainException.ThrowIf(
            minChoices < 0 || maxChoices < 1 || maxChoices > MaxSelections || minChoices > maxChoices || minChoices > choices.Count,
            FormulaErrors.SelectionRangeInvalid);
        return new FormulaStep(id is { } existing && existing != Guid.Empty ? existing : Guid.CreateVersion7(), name, minChoices, maxChoices, choices);
    }

    internal FormulaChoice Choice(ProductId productId, VariantId? variantId) =>
        Choices.FirstOrDefault(choice => choice.Offers(productId, variantId)) ?? throw new DomainException(FormulaErrors.ChoiceNotOffered);
}
