using System.Globalization;
using Catalog.Domain;

namespace Catalog.Application.Queries;

// Les montants sont des nombres dans la devise indiquée à côté (CurrencyCode) ; une catégorie fiscale porte son taux en
// fraction (0.18 = 18 %).

public sealed record TaxCategoryDto(Guid Id, string Code, string Name, decimal Rate, TaxCategoryStatus Status)
{
    public static TaxCategoryDto From(TaxCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return new TaxCategoryDto(category.Id.Value, category.Code.Value, category.Name.Value, category.Rate.Value, category.Status);
    }
}

public sealed record ModifierDto(Guid Id, string Name, decimal PriceDelta, bool IsAvailable)
{
    public static ModifierDto From(Modifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        return new ModifierDto(modifier.Id.Value, modifier.Name.Value, modifier.PriceDelta.Amount, modifier.IsAvailable);
    }
}

/// <param name="IsSatisfiable">Assez d'options disponibles pour atteindre le minimum (sinon un menu ne se publie pas).</param>
public sealed record ModifierGroupDto(
    Guid Id,
    string Name,
    string CurrencyCode,
    int MinSelections,
    int MaxSelections,
    bool IsSatisfiable,
    IReadOnlyList<ModifierDto> Modifiers)
{
    public static ModifierGroupDto From(ModifierGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new ModifierGroupDto(
            group.Id.Value,
            group.Name.Value,
            group.Currency.Code,
            group.MinSelections,
            group.MaxSelections,
            group.IsSatisfiable,
            group.Modifiers.Select(ModifierDto.From).ToList());
    }
}

/// <param name="Price">Prix de vente de la variante : prix de base du produit + <paramref name="PriceDelta"/>.</param>
public sealed record ProductVariantDto(Guid Id, string Name, decimal PriceDelta, decimal Price, string? Sku, bool IsAvailable);

public sealed record ProductDto(
    Guid Id,
    string Name,
    string? Description,
    ProductStatus Status,
    bool IsSellable,
    string CurrencyCode,
    decimal BasePrice,
    Guid TaxCategoryId,
    IReadOnlyList<ProductVariantDto> Variants,
    IReadOnlyList<Guid> ModifierGroupIds,
    IReadOnlyList<string> Allergens,
    IReadOnlyList<OnlineChannel> HiddenChannels,
    IReadOnlyList<PriceOverrideDto>? PriceOverrides = null,
    IReadOnlyList<FormulaStepDto>? FormulaSteps = null,
    IReadOnlyList<Guid>? LocationIds = null,
    IReadOnlyList<Guid>? UnavailableAt = null)
{
    public bool IsFormula => FormulaSteps is { Count: > 0 };

    public static ProductDto From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        var basePrice = product.BasePrice.Amount;
        return new ProductDto(
            product.Id.Value,
            product.Name.Value,
            product.Description,
            product.Status,
            product.IsSellable,
            product.Currency.Code,
            basePrice,
            product.TaxCategoryId.Value,
            product.Variants
                .Select(variant => new ProductVariantDto(
                    variant.Id.Value,
                    variant.Name.Value,
                    variant.PriceDelta.Amount,
                    basePrice + variant.PriceDelta.Amount,
                    variant.Sku?.Value,
                    variant.IsAvailable))
                .ToList(),
            product.ModifierGroupIds.Select(id => id.Value).ToList(),
            product.Allergens.ToList(),
            product.HiddenChannels.ToList(),
            product.PriceOverrides
                .OrderBy(item => item.LocationId is null)
                .ThenBy(item => item.Channel)
                .Select(item => new PriceOverrideDto(item.LocationId?.Value, item.Channel, item.BasePrice.Amount))
                .ToList(),
            product.FormulaSteps.Select(FormulaStepDto.From).ToList(),
            product.LocationIds.Select(id => id.Value).ToList(),
            product.UnavailableAt.Select(id => id.Value).ToList());
    }
}

/// <summary>Un produit proposé dans une formule (par id : son nom se lit dans le catalogue) et son supplément.</summary>
public sealed record FormulaChoiceDto(Guid ProductId, Guid? VariantId, decimal Supplement);

/// <param name="MinChoices">0 : étape facultative.</param>
public sealed record FormulaStepDto(Guid Id, string Name, int MinChoices, int MaxChoices, IReadOnlyList<FormulaChoiceDto> Choices)
{
    public static FormulaStepDto From(FormulaStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new FormulaStepDto(
            step.Id,
            step.Name.Value,
            step.MinChoices,
            step.MaxChoices,
            step.Choices.Select(choice => new FormulaChoiceDto(choice.ProductId.Value, choice.VariantId?.Value, choice.Supplement.Amount)).ToList());
    }
}

/// <param name="LocationId">Null : tous les établissements.</param>
/// <param name="Channel">Null : tous les canaux.</param>
public sealed record PriceOverrideDto(Guid? LocationId, PriceChannel? Channel, decimal BasePrice);

public sealed record ProductSummaryDto(
    Guid Id, string Name, ProductStatus Status, bool IsSellable, string CurrencyCode, decimal BasePrice, int VariantCount, bool IsFormula = false,
    IReadOnlyList<Guid>? LocationIds = null, IReadOnlyList<Guid>? UnavailableAt = null)
{
    public static ProductSummaryDto From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new ProductSummaryDto(
            product.Id.Value,
            product.Name.Value,
            product.Status,
            product.IsSellable,
            product.Currency.Code,
            product.BasePrice.Amount,
            product.Variants.Count,
            product.IsFormula,
            product.LocationIds.Select(id => id.Value).ToList(),
            product.UnavailableAt.Select(id => id.Value).ToList());
    }
}


/// <param name="ProductIds">Dans l'ordre d'affichage.</param>
public sealed record MenuSectionDto(Guid Id, string Name, IReadOnlyList<Guid> ProductIds);

/// <param name="Sections">Dans l'ordre d'affichage.</param>
/// <param name="Start">Heure locale, HH:mm.</param>
/// <param name="End">Heure locale, HH:mm ; avant le début, le créneau passe minuit.</param>
public sealed record MenuSlotDto(DayOfWeek Day, string Start, string End);

/// <param name="Slots">Créneaux où le menu est proposé ; vide = à toute heure.</param>
public sealed record MenuDto(
    Guid Id, Guid LocationId, string Name, MenuStatus Status, DateTimeOffset? PublishedAtUtc, IReadOnlyList<MenuSectionDto> Sections,
    IReadOnlyList<MenuSlotDto>? Slots = null)
{
    public static MenuDto From(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return new MenuDto(
            menu.Id.Value,
            menu.LocationId.Value,
            menu.Name.Value,
            menu.Status,
            menu.PublishedAtUtc,
            menu.Sections
                .Select(section => new MenuSectionDto(
                    section.Id.Value, section.Name.Value, section.ProductIds.Select(id => id.Value).ToList()))
                .ToList(),
            menu.Slots.Select(slot => new MenuSlotDto(slot.Day, slot.Start.ToString("HH:mm", CultureInfo.InvariantCulture), slot.End.ToString("HH:mm", CultureInfo.InvariantCulture))).ToList());
    }
}

public sealed record MenuSummaryDto(
    Guid Id, Guid LocationId, string Name, MenuStatus Status, DateTimeOffset? PublishedAtUtc, int SectionCount)
{
    public static MenuSummaryDto From(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return new MenuSummaryDto(
            menu.Id.Value, menu.LocationId.Value, menu.Name.Value, menu.Status, menu.PublishedAtUtc, menu.Sections.Count);
    }
}
