namespace Inventory.Domain;

public enum StockMovementType
{
    Receipt = 1,
    Consumption = 2,
    Adjustment = 3,
    Waste = 4,
    Transfer = 5,

    /// <summary>Un inventaire qui a trouvé exactement le stock attendu : le seul mouvement de delta nul.</summary>
    CountConfirmed = 6,
}

/// <summary>
/// Décide si un mouvement sortant manuel peut faire passer le stock sous zéro. Une consommation de vente
/// (<see cref="InventoryItem.ConsumeForSale"/>) n'est jamais refusée, quelle que soit la policy.
/// </summary>
public enum NegativeStockPolicy
{
    Forbid = 1,
    Allow = 2,
}
