using Inventory.Application;
using Inventory.Application.Commands;
using Inventory.Domain;

namespace Inventory.Api;

public sealed record CreatedResponse(Guid Id);

public sealed record CreateIngredientRequest(string? Name, UnitOfMeasure BaseUnit);

public sealed record NameRequest(string? Name);

/// <param name="Unit">Facultatif : l'unité de base de l'ingrédient.</param>
/// <param name="ReorderThreshold">Facultatif : zéro.</param>
public sealed record CreateItemRequest(
    Guid LocationId,
    Guid IngredientId,
    UnitOfMeasure? Unit = null,
    QuantityInput? ReorderThreshold = null,
    NegativeStockPolicy NegativeStockPolicy = NegativeStockPolicy.Forbid);

/// <param name="Quantity">{ amount, unit? } ; sans unité : celle de l'article.</param>
/// <param name="TotalCost">Facultatif, réception seulement : le prix payé pour toute la quantité.</param>
public sealed record MovementRequest(QuantityInput? Quantity, string? Reference = null, decimal? TotalCost = null);

/// <param name="UnitCost">Coût d'une unité de l'article ; null l'efface.</param>
public sealed record UnitCostRequest(decimal? UnitCost);

public sealed record SupplierRequest(string? Name, string? Phone = null, string? Email = null, string? Notes = null);

public sealed record ActiveRequest(bool IsActive);

public sealed record CreatePurchaseOrderRequest(Guid LocationId, Guid SupplierId, IReadOnlyList<PurchaseLineInput>? Lines = null, string? Notes = null);

public sealed record ComposePurchaseOrderRequest(IReadOnlyList<PurchaseLineInput>? Lines, string? Notes = null);

/// <param name="Close">Le reste du bon ne sera pas livré.</param>
public sealed record ReceivePurchaseOrderRequest(IReadOnlyList<ReceivedLineInput>? Lines, bool Close = false);

public sealed record WasteRequest(QuantityInput? Quantity, string? Reason, string? Reference = null);

public sealed record CountRequest(QuantityInput? CountedQuantity, string? Reason, string? Reference = null);

/// <param name="Reference">Facultatif : générée si absente.</param>
public sealed record TransferRequest(Guid SourceItemId, Guid DestinationItemId, QuantityInput? Quantity, string? Reference = null);

public sealed record ThresholdRequest(QuantityInput? Threshold);

public sealed record PolicyRequest(NegativeStockPolicy Policy);

public sealed record CreateRecipeRequest(Guid ProductId, Guid? VariantId = null);

public sealed record RecipeIngredientRequest(Guid IngredientId, QuantityInput? Quantity);

public sealed record RecipeQuantityRequest(QuantityInput? Quantity);
