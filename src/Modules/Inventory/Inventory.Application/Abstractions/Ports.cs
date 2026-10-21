using Inventory.Domain;

namespace Inventory.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête ou du message traité (jamais de la
// requête elle-même). Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IIngredientRepository
{
    Task<Ingredient?> FindAsync(TenantId tenantId, IngredientId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule.</summary>
    Task<IReadOnlyList<Ingredient>> FindManyAsync(TenantId tenantId, IEnumerable<IngredientId> ids, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par nom.</summary>
    Task<IReadOnlyList<Ingredient>> ListAsync(TenantId tenantId, bool includeInactive, CancellationToken cancellationToken = default);

    void Add(Ingredient ingredient);
}

public interface IInventoryItemRepository
{
    /// <summary>Sans son historique : seuls les mouvements ajoutés ensuite sont en mémoire (et insérés à la sauvegarde).</summary>
    Task<InventoryItem?> FindAsync(TenantId tenantId, InventoryItemId id, CancellationToken cancellationToken = default);

    /// <summary>Les articles de ces ingrédients dans un établissement, pour les modifier (consommation d'une commande).</summary>
    Task<IReadOnlyList<InventoryItem>> FindAtLocationAsync(
        TenantId tenantId, LocationId locationId, IEnumerable<IngredientId> ingredientIds, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule ; <paramref name="lowStockOnly"/> : seulement ceux au seuil ou en dessous.</summary>
    Task<IReadOnlyList<InventoryItem>> ListAsync(
        TenantId tenantId, LocationId? locationId, bool lowStockOnly, CancellationToken cancellationToken = default);

    void Add(InventoryItem item);
}

/// <summary>Le registre des mouvements d'un article, en lecture seule (il ne se modifie jamais).</summary>
public interface IStockMovementLedger
{
    /// <summary>Les plus récents d'abord, <paramref name="skip"/> sautés, au plus <paramref name="take"/>.</summary>
    Task<IReadOnlyList<StockMovement>> ListAsync(
        TenantId tenantId, InventoryItemId itemId, int skip, int take, CancellationToken cancellationToken = default);
}

public interface IRecipeRepository
{
    Task<Recipe?> FindAsync(TenantId tenantId, RecipeId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule : toutes les recettes (produit et variantes) de ces produits.</summary>
    Task<IReadOnlyList<Recipe>> ListForProductsAsync(
        TenantId tenantId, IEnumerable<ProductId> productIds, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule.</summary>
    Task<IReadOnlyList<Recipe>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(Recipe recipe);
}

/// <summary>
/// Les commandes dont le stock a déjà été consommé : la consommation d'une commande n'a lieu qu'une fois, même si
/// l'événement est reçu deux fois. Écrit dans la même transaction que les mouvements.
/// </summary>
public interface IOrderConsumptionLog
{
    Task<bool> HasConsumedAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);

    void Record(TenantId tenantId, OrderId orderId, LocationId locationId, DateTimeOffset consumedAtUtc);
}

/// <summary>Ce qu'Inventory doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface IInventoryTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Un établissement du tenant courant, fourni par l'hôte depuis Locations ; null s'il n'existe pas.</summary>
public interface IInventoryLocations
{
    Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

/// <summary>Un produit du tenant courant et ses variantes, fourni par l'hôte depuis Catalog ; null s'il n'existe pas.</summary>
public interface IInventoryProducts
{
    Task<InventoryProduct?> FindAsync(TenantId tenantId, ProductId productId, CancellationToken cancellationToken = default);
}

public sealed record InventoryProduct(ProductReference Product, IReadOnlyList<VariantId> VariantIds);

/// <summary>Une commande du tenant courant telle que le stock la voit (sans prix), fournie par l'hôte depuis Orders.</summary>
public interface IInventoryOrders
{
    Task<InventoryOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);
}

/// <param name="HasStartedPreparation">En préparation, prête ou terminée : les ingrédients ont été utilisés.</param>
public sealed record InventoryOrder(OrderId OrderId, LocationId LocationId, bool HasStartedPreparation, IReadOnlyList<InventoryOrderLine> Lines);

public sealed record InventoryOrderLine(ProductId ProductId, VariantId? VariantId, int Quantity);

public interface ISupplierRepository
{
    Task<Supplier?> FindAsync(TenantId tenantId, SupplierId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par nom.</summary>
    Task<IReadOnlyList<Supplier>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(Supplier supplier);
}

public interface IPurchaseOrderRepository
{
    Task<PurchaseOrder?> FindAsync(TenantId tenantId, PurchaseOrderId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, les plus récents d'abord.</summary>
    Task<IReadOnlyList<PurchaseOrder>> ListAsync(
        TenantId tenantId, LocationId? locationId, PurchaseOrderStatus? status, int take, CancellationToken cancellationToken = default);

    void Add(PurchaseOrder order);
}
