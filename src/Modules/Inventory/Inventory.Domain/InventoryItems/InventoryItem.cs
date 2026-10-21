namespace Inventory.Domain;

/// <summary>
/// Stock d'un ingrédient dans un établissement. Le registre des mouvements fait foi ;
/// <see cref="CurrentQuantity"/> en est la somme cumulée stockée, modifiée exactement par les mouvements qu'ajoute
/// cet agrégat. Aucune règle ne relit le registre : l'agrégat n'a jamais besoin de son historique en mémoire.
/// </summary>
public sealed class InventoryItem : AggregateRoot<InventoryItemId>, ITenantOwned
{
    private readonly List<StockMovement> _movements = [];

    private InventoryItem(
        InventoryItemId id,
        TenantId tenantId,
        LocationId locationId,
        IngredientId ingredientId,
        UnitOfMeasure unit,
        Quantity reorderThreshold,
        NegativeStockPolicy negativeStockPolicy) : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        IngredientId = ingredientId;
        Unit = unit;
        ReorderThreshold = reorderThreshold;
        NegativeStockPolicy = negativeStockPolicy;
    }

    // Réservé à l'ORM.
    private InventoryItem()
    {
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public IngredientId IngredientId { get; private set; }

    /// <summary>Unité du stock et de chaque delta du registre. Fixée à la création.</summary>
    public UnitOfMeasure Unit { get; private set; }

    /// <summary>
    /// Projection du registre, en <see cref="Unit"/>. Négative uniquement sous <see cref="NegativeStockPolicy.Allow"/>
    /// ou après une consommation liée à une vente, qui n'est jamais refusée.
    /// </summary>
    public decimal CurrentQuantity { get; private set; }

    public Quantity ReorderThreshold { get; private set; } = null!;

    public NegativeStockPolicy NegativeStockPolicy { get; private set; }

    /// <summary>
    /// Les mouvements ajoutés depuis le chargement de cet agrégat — PAS l'historique de l'article, qui n'est jamais
    /// réhydraté. L'Infrastructure les insère dans la table du registre.
    /// </summary>
    public IReadOnlyCollection<StockMovement> Movements => _movements.AsReadOnly();

    public bool IsLowStock => CurrentQuantity <= ReorderThreshold.Amount;

    /// <summary>
    /// Coût moyen pondéré d'une unité (<see cref="Unit"/>), dans la devise du restaurant ; null tant qu'aucun coût n'est
    /// connu. Recalculé à chaque réception chiffrée : le stock restant garde son coût, la livraison apporte le sien.
    /// </summary>
    public decimal? AverageUnitCost { get; private set; }

    public static InventoryItem Create(
        TenantId tenantId,
        LocationReference location,
        IngredientReference ingredient,
        UnitOfMeasure unit,
        Quantity reorderThreshold,
        bool isUniqueForLocation,
        NegativeStockPolicy negativeStockPolicy = NegativeStockPolicy.Forbid)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(ingredient);
        ArgumentNullException.ThrowIfNull(reorderThreshold);

        Guard.NotEmpty(tenantId);
        // Agrégat de configuration : l'établissement doit seulement appartenir au tenant, opérationnel ou non.
        DomainException.ThrowIf(location.TenantId != tenantId, CommonErrors.TenantMismatch);
        DomainException.ThrowIf(ingredient.TenantId != tenantId, CommonErrors.TenantMismatch);
        DomainException.ThrowIf(!ingredient.IsActive, InventoryErrors.IngredientInactive);
        DomainException.ThrowIf(!Enum.IsDefined(unit), InventoryErrors.UnitUnknown);
        DomainException.ThrowIf(!unit.IsCompatibleWith(ingredient.BaseUnit), InventoryErrors.UnitIncompatible);
        DomainException.ThrowIf(!Enum.IsDefined(negativeStockPolicy), InventoryErrors.ItemPolicyUnknown);
        var threshold = reorderThreshold.ConvertTo(unit);
        DomainException.ThrowIf(!isUniqueForLocation, InventoryErrors.ItemNotUniqueForLocation);

        return new InventoryItem(
            InventoryItemId.New(),
            tenantId,
            location.LocationId,
            ingredient.IngredientId,
            unit,
            threshold,
            negativeStockPolicy);
    }

    /// <param name="totalCost">Montant payé pour cette livraison (devise du restaurant) ; null si inconnu.</param>
    public void Receive(Quantity quantity, IClock clock, string? reference = null, decimal? totalCost = null)
    {
        var amount = ToMovementAmount(quantity);
        DomainException.ThrowIf(totalCost is < 0m or > MaxCost, InventoryErrors.CostInvalid);
        decimal? unitCost = totalCost is { } paid ? decimal.Round(paid / amount, CostDecimals) : null;
        if (unitCost is { } cost)
        {
            var onHand = Math.Max(CurrentQuantity, 0m);
            AverageUnitCost = AverageUnitCost is { } average && onHand > 0m
                ? decimal.Round(((onHand * average) + totalCost!.Value) / (onHand + amount), CostDecimals)
                : cost;
        }

        var movement = Append(StockMovementType.Receipt, amount, clock, reference, reason: null, unitCost: unitCost);

        Raise(new StockReceived(
            TenantId, Id, LocationId, IngredientId, amount, CurrentQuantity, Unit, movement.Reference, movement.OccurredAtUtc));
    }

    public void Consume(Quantity quantity, IClock clock, string? reference = null)
    {
        var amount = ToMovementAmount(quantity);
        var wasLow = IsLowStock;
        var movement = Append(StockMovementType.Consumption, -amount, clock, reference, reason: null);

        Raise(new StockConsumed(
            TenantId, Id, LocationId, IngredientId, amount, CurrentQuantity, Unit, movement.Reference, movement.OccurredAtUtc));
        RaiseLowStockIfReached(wasLow, movement.OccurredAtUtc);
    }

    /// <summary>
    /// Consommation due à une commande dont la préparation a commencé : la vente a déjà eu lieu, elle est donc toujours
    /// enregistrée, même si elle fait passer le stock sous zéro avec <see cref="NegativeStockPolicy.Forbid"/>.
    /// </summary>
    public void ConsumeForSale(Quantity quantity, string orderReference, IClock clock)
    {
        var amount = ToMovementAmount(quantity);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(orderReference), InventoryErrors.OrderReferenceRequired);
        var wasLow = IsLowStock;
        var wasNegative = CurrentQuantity < 0m;
        var movement = Append(StockMovementType.Consumption, -amount, clock, orderReference, reason: null, enforcePolicy: false);

        Raise(new StockConsumed(
            TenantId, Id, LocationId, IngredientId, amount, CurrentQuantity, Unit, movement.Reference, movement.OccurredAtUtc));
        if (!wasNegative && CurrentQuantity < 0m)
        {
            Raise(new StockWentNegative(
                TenantId, Id, LocationId, IngredientId, CurrentQuantity, Unit, movement.Reference!, movement.OccurredAtUtc));
        }

        RaiseLowStockIfReached(wasLow, movement.OccurredAtUtc);
    }

    public void RecordWaste(Quantity quantity, string reason, IClock clock, string? reference = null)
    {
        var amount = ToMovementAmount(quantity);
        EnsureReason(reason);
        var wasLow = IsLowStock;
        var movement = Append(StockMovementType.Waste, -amount, clock, reference, reason);

        Raise(new WasteRecorded(
            TenantId, Id, LocationId, IngredientId, amount, CurrentQuantity, Unit, movement.Reason!, movement.OccurredAtUtc));
        RaiseLowStockIfReached(wasLow, movement.OccurredAtUtc);
    }

    /// <summary>
    /// Comptage de stock : fixe le stock à la quantité physiquement comptée. Le registre reçoit la différence
    /// signée, la correction elle-même reste donc auditable. Un comptage qui trouve exactement le stock attendu est
    /// lui aussi enregistré (mouvement <see cref="StockMovementType.CountConfirmed"/> de delta nul et
    /// <see cref="StockCountConfirmed"/>) : un inventaire physique laisse toujours une trace.
    /// </summary>
    public void Adjust(Quantity countedQuantity, string reason, IClock clock, string? reference = null)
    {
        ArgumentNullException.ThrowIfNull(countedQuantity);

        var counted = countedQuantity.ConvertTo(Unit).Amount;
        EnsureReason(reason);
        var delta = counted - CurrentQuantity;
        if (delta == 0m)
        {
            var confirmation = Append(StockMovementType.CountConfirmed, 0m, clock, reference, reason);
            Raise(new StockCountConfirmed(
                TenantId, Id, LocationId, IngredientId, CurrentQuantity, Unit, confirmation.Reason!, confirmation.OccurredAtUtc));
            return;
        }

        var wasLow = IsLowStock;
        var movement = Append(StockMovementType.Adjustment, delta, clock, reference, reason);

        Raise(new StockAdjusted(
            TenantId, Id, LocationId, IngredientId, delta, CurrentQuantity, Unit, movement.Reason!, movement.OccurredAtUtc));
        RaiseLowStockIfReached(wasLow, movement.OccurredAtUtc);
    }

    // Internal : un transfert mouvemente toujours les deux côtés, via StockTransfer.Execute.
    internal void TransferOut(Quantity quantity, string transferReference, DateTimeOffset now)
    {
        var amount = ToMovementAmount(quantity);
        EnsureTransferReference(transferReference);
        var wasLow = IsLowStock;
        var movement = AppendAt(StockMovementType.Transfer, -amount, now, transferReference, reason: null);

        Raise(new StockTransferredOut(
            TenantId, Id, LocationId, IngredientId, amount, CurrentQuantity, Unit, movement.Reference!, movement.OccurredAtUtc));
        RaiseLowStockIfReached(wasLow, movement.OccurredAtUtc);
    }

    internal void TransferIn(Quantity quantity, string transferReference, DateTimeOffset now)
    {
        var amount = ToMovementAmount(quantity);
        EnsureTransferReference(transferReference);
        var movement = AppendAt(StockMovementType.Transfer, amount, now, transferReference, reason: null);

        Raise(new StockTransferredIn(
            TenantId, Id, LocationId, IngredientId, amount, CurrentQuantity, Unit, movement.Reference!, movement.OccurredAtUtc));
    }

    public void ChangeReorderThreshold(Quantity reorderThreshold, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(reorderThreshold);
        ArgumentNullException.ThrowIfNull(clock);

        var threshold = reorderThreshold.ConvertTo(Unit);
        if (threshold == ReorderThreshold)
        {
            return;
        }

        var wasLow = IsLowStock;
        ReorderThreshold = threshold;
        RaiseLowStockIfReached(wasLow, clock.UtcNow);
    }

    /// <summary>Coût d'une unité saisi à la main (stock initial, prix connu sans livraison) ; null l'efface.</summary>
    public void SetUnitCost(decimal? unitCost)
    {
        DomainException.ThrowIf(unitCost is < 0m or > MaxCost, InventoryErrors.CostInvalid);
        AverageUnitCost = unitCost is { } cost ? decimal.Round(cost, CostDecimals) : null;
    }

    public const int CostDecimals = 6;
    public const decimal MaxCost = 1_000_000_000m;

    public void ChangeNegativeStockPolicy(NegativeStockPolicy policy)
    {
        DomainException.ThrowIf(!Enum.IsDefined(policy), InventoryErrors.ItemPolicyUnknown);
        if (policy == NegativeStockPolicy)
        {
            return;
        }

        DomainException.ThrowIf(
            policy == NegativeStockPolicy.Forbid && CurrentQuantity < 0m,
            InventoryErrors.ItemStockIsNegative);

        NegativeStockPolicy = policy;
    }

    private decimal ToMovementAmount(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        var amount = quantity.ConvertTo(Unit).Amount;
        DomainException.ThrowIf(amount <= 0m, InventoryErrors.MovementQuantityNotPositive);

        return amount;
    }

    private static void EnsureReason(string reason) =>
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(reason), InventoryErrors.MovementReasonRequired);

    private static void EnsureTransferReference(string transferReference) =>
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(transferReference), InventoryErrors.TransferReferenceRequired);

    private StockMovement Append(
        StockMovementType type,
        decimal delta,
        IClock clock,
        string? reference,
        string? reason,
        bool enforcePolicy = true,
        decimal? unitCost = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return AppendAt(type, delta, clock.UtcNow, reference, reason, enforcePolicy, unitCost);
    }

    // Chemin d'écriture unique : l'écriture du registre et la projection changent toujours ensemble.
    private StockMovement AppendAt(
        StockMovementType type,
        decimal delta,
        DateTimeOffset now,
        string? reference,
        string? reason,
        bool enforcePolicy = true,
        decimal? unitCost = null)
    {
        var quantityAfter = CurrentQuantity + delta;
        DomainException.ThrowIf(
            enforcePolicy && delta < 0m && quantityAfter < 0m && NegativeStockPolicy == NegativeStockPolicy.Forbid,
            InventoryErrors.InsufficientStock);

        var movement = StockMovement.Create(TenantId, Id, type, delta, Unit, now, reference, reason, unitCost);
        _movements.Add(movement);
        CurrentQuantity = quantityAfter;

        return movement;
    }

    // Levé uniquement au franchissement, pas à chaque mouvement tant que le stock reste bas.
    private void RaiseLowStockIfReached(bool wasLow, DateTimeOffset occurredAtUtc)
    {
        if (!wasLow && IsLowStock)
        {
            Raise(new LowStockReached(
                TenantId, Id, LocationId, IngredientId, CurrentQuantity, ReorderThreshold.Amount, Unit, occurredAtUtc));
        }
    }
}
