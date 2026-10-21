using Inventory.Application.Abstractions;
using Inventory.Domain;

namespace Inventory.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte (requête, ou message traité en arrière-plan), une
/// écriture exige un tenant qui les accepte, chaque agrégat est chargé dans ce tenant seulement (404 sinon), puis on
/// sauvegarde. Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class InventoryWorkspace(
    ITenantContext tenantContext,
    IInventoryTenants tenants,
    IInventoryLocations locations,
    IIngredientRepository ingredients,
    IInventoryItemRepository items,
    IStockMovementLedger movements,
    IRecipeRepository recipes,
    IInventoryUnitOfWork unitOfWork)
{
    public IIngredientRepository Ingredients => ingredients;

    public IInventoryItemRepository Items => items;

    public IStockMovementLedger Movements => movements;

    public IRecipeRepository Recipes => recipes;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(InventoryApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<LocationReference> RequireLocationAsync(TenantId tenantId, Guid locationId, CancellationToken cancellationToken) =>
        (locationId == Guid.Empty ? null : await locations.FindAsync(tenantId, LocationId.From(locationId), cancellationToken))
        ?? throw new NotFoundException(InventoryApplicationErrors.LocationNotFound);

    public async Task<Ingredient> LoadIngredientAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await ingredients.FindAsync(tenantId, IngredientId.From(id), cancellationToken))
        ?? throw new NotFoundException(InventoryApplicationErrors.IngredientNotFound);

    public async Task<InventoryItem> LoadItemAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await items.FindAsync(tenantId, InventoryItemId.From(id), cancellationToken))
        ?? throw new NotFoundException(InventoryApplicationErrors.ItemNotFound);

    public async Task<Recipe> LoadRecipeAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await recipes.FindAsync(tenantId, RecipeId.From(id), cancellationToken))
        ?? throw new NotFoundException(InventoryApplicationErrors.RecipeNotFound);

    /// <summary>Charge l'article du tenant courant (tenant qui accepte les écritures), applique <paramref name="change"/> et sauvegarde.</summary>
    public async Task EditItemAsync(Guid id, Action<InventoryItem> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadItemAsync(CurrentTenant, id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    /// <summary>Charge la recette du tenant courant (tenant qui accepte les écritures), applique <paramref name="change"/> et sauvegarde.</summary>
    public async Task EditRecipeAsync(Guid id, Func<Recipe, Task> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        await change(await LoadRecipeAsync(CurrentTenant, id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// Une quantité saisie : un montant et, facultativement, son unité. Sans unité, c'est l'unité attendue par le contexte
/// (celle de l'article de stock, ou de base de l'ingrédient).
/// </summary>
public sealed record QuantityInput(decimal Amount, UnitOfMeasure? Unit = null)
{
    /// <summary>La quantité saisie ; une saisie absente est refusée (400 métier, pas une exception technique).</summary>
    public static Quantity Require(QuantityInput? input, UnitOfMeasure defaultUnit) =>
        input is null
            ? throw new DomainException(InventoryApplicationErrors.QuantityRequired)
            : Quantity.Create(input.Amount, input.Unit ?? defaultUnit);
}
