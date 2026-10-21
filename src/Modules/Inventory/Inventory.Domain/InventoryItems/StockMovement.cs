namespace Inventory.Domain;

/// <summary>
/// Écriture immuable du registre. Une erreur n'est jamais modifiée : elle est corrigée par un nouveau mouvement.
/// Elle porte son tenant et son article de stock pour être stockée et lue seule, sans l'historique de l'article.
/// </summary>
public sealed class StockMovement : Entity<StockMovementId>
{
    public const int ReferenceMaxLength = 100;
    public const int ReasonMaxLength = 250;

    private StockMovement(
        StockMovementId id,
        TenantId tenantId,
        InventoryItemId inventoryItemId,
        StockMovementType type,
        decimal quantityDelta,
        UnitOfMeasure unit,
        DateTimeOffset occurredAtUtc,
        string? reference,
        string? reason) : base(id)
    {
        TenantId = tenantId;
        InventoryItemId = inventoryItemId;
        Type = type;
        QuantityDelta = quantityDelta;
        Unit = unit;
        OccurredAtUtc = occurredAtUtc;
        Reference = reference;
        Reason = reason;
    }

    // Réservé à l'ORM.
    private StockMovement()
    {
    }

    public TenantId TenantId { get; private init; }

    public InventoryItemId InventoryItemId { get; private init; }

    public StockMovementType Type { get; private init; }

    /// <summary>
    /// Delta signé appliqué au stock, en <see cref="Unit"/> (l'unité de l'article) : positif = entrée,
    /// négatif = sortie. Nul uniquement pour <see cref="StockMovementType.CountConfirmed"/>.
    /// </summary>
    public decimal QuantityDelta { get; private init; }

    public UnitOfMeasure Unit { get; private init; }

    public DateTimeOffset OccurredAtUtc { get; private init; }

    /// <summary>Origine du mouvement : bon de commande fournisseur, id de commande, référence de transfert...</summary>
    public string? Reference { get; private init; }

    public string? Reason { get; private init; }

    /// <summary>Coût d'une unité de cette réception (devise du restaurant) ; null hors réception ou sans prix.</summary>
    public decimal? UnitCost { get; private init; }

    internal static StockMovement Create(
        TenantId tenantId,
        InventoryItemId inventoryItemId,
        StockMovementType type,
        decimal quantityDelta,
        UnitOfMeasure unit,
        DateTimeOffset occurredAtUtc,
        string? reference,
        string? reason,
        decimal? unitCost = null)
    {
        // Un mouvement fait varier le stock, sauf la trace d'un comptage qui l'a confirmé — celle-ci ne change rien.
        DomainException.ThrowIf(
            (quantityDelta == 0m) != (type == StockMovementType.CountConfirmed),
            InventoryErrors.MovementQuantityNotPositive);

        var normalizedReference = Normalize(reference);
        DomainException.ThrowIf(
            normalizedReference?.Length > ReferenceMaxLength,
            InventoryErrors.MovementReferenceTooLong);

        var normalizedReason = Normalize(reason);
        DomainException.ThrowIf(normalizedReason?.Length > ReasonMaxLength, InventoryErrors.MovementReasonTooLong);

        return new StockMovement(
            StockMovementId.New(),
            tenantId,
            inventoryItemId,
            type,
            quantityDelta,
            unit,
            occurredAtUtc,
            normalizedReference,
            normalizedReason)
        {
            UnitCost = unitCost,
        };
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
