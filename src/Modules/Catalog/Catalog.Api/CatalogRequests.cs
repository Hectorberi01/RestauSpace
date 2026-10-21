using Catalog.Application.Commands;
using Catalog.Domain;

namespace Catalog.Api;

// Montants : nombres dans la devise du produit ou du groupe (celle du tenant à leur création), jamais choisie ici.

public sealed record CreatedResponse(Guid Id);

public sealed record NameRequest(string? Name);

public sealed record PositionRequest(int Position);

public sealed record AvailabilityRequest(bool IsAvailable);

public sealed record PriceDeltaRequest(decimal PriceDelta);

public sealed record CreateTaxCategoryRequest(string? Code, string? Name, decimal Rate);

public sealed record TaxRateRequest(decimal Rate);

public sealed record CreateModifierGroupRequest(string? Name, int MinSelections, int MaxSelections);

public sealed record SelectionRuleRequest(int MinSelections, int MaxSelections);

public sealed record AddModifierRequest(string? Name, decimal PriceDelta);

public sealed record CreateProductRequest(string? Name, decimal BasePrice, Guid TaxCategoryId, string? Description = null);

public sealed record DescriptionRequest(string? Description);

public sealed record TaxCategoryRequest(Guid TaxCategoryId);

public sealed record BasePriceRequest(decimal BasePrice);

public sealed record AddVariantRequest(string? Name, decimal PriceDelta, string? Sku = null);

public sealed record ModifierGroupRequest(Guid ModifierGroupId);

public sealed record CreateMenuRequest(Guid LocationId, string? Name);

public sealed record ProductRequest(Guid ProductId);

public sealed record AllergensRequest(IReadOnlyList<string?>? Allergens);

public sealed record MenuScheduleRequest(IReadOnlyList<MenuSlotInput>? Slots);

public sealed record HiddenChannelsRequest(IReadOnlyList<OnlineChannel>? Channels);

/// <summary>Les étapes de la formule, dans l'ordre ; vide pour en refaire un produit ordinaire.</summary>
public sealed record FormulaRequest(IReadOnlyList<Catalog.Application.Commands.FormulaStepInput>? Steps);

/// <summary>Prix propre : établissement (null = tous), canal (null = tous) et prix de base.</summary>
public sealed record PriceOverrideRequest(Guid? LocationId, PriceChannel? Channel, decimal BasePrice);

public sealed record ProductLocationsRequest(IReadOnlyList<Guid>? LocationIds);

public sealed record LocationAvailabilityRequest(bool IsAvailable);
