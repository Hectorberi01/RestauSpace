namespace Inventory.Domain;

// Quantity = quantité déplacée (positive), QuantityAfter = stock résultant ; toutes deux en Unit (l'unité de l'article).

public sealed record StockReceived(
    TenantId TenantId,
    InventoryItemId InventoryItemId,
    LocationId LocationId,
    IngredientId IngredientId,
    decimal Quantity,
    decimal QuantityAfter,
    UnitOfMeasure Unit,
    string? Reference,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record StockConsumed(
    TenantId TenantId,
    InventoryItemId InventoryItemId,
    LocationId LocationId,
    IngredientId IngredientId,
    decimal Quantity,
    decimal QuantityAfter,
    UnitOfMeasure Unit,
    string? Reference,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>
/// Une consommation de vente a fait passer le stock sous zéro (au franchissement uniquement). La vente a déjà eu lieu :
/// elle est enregistrée quelle que soit la <see cref="NegativeStockPolicy"/> ; cet événement signale qu'un inventaire
/// du stock est nécessaire.
/// </summary>
public sealed record StockWentNegative(
    TenantId TenantId,
    InventoryItemId InventoryItemId,
    LocationId LocationId,
    IngredientId IngredientId,
    decimal QuantityAfter,
    UnitOfMeasure Unit,
    string OrderReference,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record WasteRecorded(
    TenantId TenantId,
    InventoryItemId InventoryItemId,
    LocationId LocationId,
    IngredientId IngredientId,
    decimal Quantity,
    decimal QuantityAfter,
    UnitOfMeasure Unit,
    string Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>QuantityDelta est signé : quantité comptée moins le stock connu avant l'inventaire.</summary>
public sealed record StockAdjusted(
    TenantId TenantId,
    InventoryItemId InventoryItemId,
    LocationId LocationId,
    IngredientId IngredientId,
    decimal QuantityDelta,
    decimal QuantityAfter,
    UnitOfMeasure Unit,
    string Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Un inventaire a trouvé exactement le stock attendu : rien n'a bougé, mais l'inventaire est tracé.</summary>
public sealed record StockCountConfirmed(
    TenantId TenantId,
    InventoryItemId InventoryItemId,
    LocationId LocationId,
    IngredientId IngredientId,
    decimal CountedQuantity,
    UnitOfMeasure Unit,
    string Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record StockTransferredOut(
    TenantId TenantId,
    InventoryItemId InventoryItemId,
    LocationId LocationId,
    IngredientId IngredientId,
    decimal Quantity,
    decimal QuantityAfter,
    UnitOfMeasure Unit,
    string TransferReference,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record StockTransferredIn(
    TenantId TenantId,
    InventoryItemId InventoryItemId,
    LocationId LocationId,
    IngredientId IngredientId,
    decimal Quantity,
    decimal QuantityAfter,
    UnitOfMeasure Unit,
    string TransferReference,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LowStockReached(
    TenantId TenantId,
    InventoryItemId InventoryItemId,
    LocationId LocationId,
    IngredientId IngredientId,
    decimal CurrentQuantity,
    decimal ReorderThreshold,
    UnitOfMeasure Unit,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
