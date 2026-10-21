namespace Inventory.Domain;

/// <summary>
/// Service de domaine qui déplace le stock d'un ingrédient entre deux articles (deux établissements) d'un même tenant
/// en une seule opération : la même quantité quitte la source et entre dans la destination sous la même référence.
/// C'est le seul moyen d'écrire un mouvement Transfer : un transfert à sens unique ne peut donc pas exister.
/// </summary>
public static class StockTransfer
{
    public static void Execute(
        InventoryItem source,
        InventoryItem destination,
        Quantity quantity,
        string transferReference,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(clock);

        source.EnsureSameTenant(destination.TenantId);
        DomainException.ThrowIf(source.Id == destination.Id, InventoryErrors.TransferSameItem);
        DomainException.ThrowIf(source.IngredientId != destination.IngredientId, InventoryErrors.TransferIngredientMismatch);

        // La destination peut utiliser une autre unité : tout ce qui pourrait rejeter son côté (borne, précision) est
        // vérifié avant de toucher la source, pour qu'un transfert rejeté laisse les deux articles inchangés.
        _ = quantity.ConvertTo(destination.Unit);

        // Un seul instant pour tout le transfert : les deux mouvements et les deux événements portent le même horodatage.
        var now = clock.UtcNow;
        source.TransferOut(quantity, transferReference, now);
        destination.TransferIn(quantity, transferReference, now);
    }
}
