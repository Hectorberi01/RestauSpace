using Inventory.Application.Abstractions;
using Inventory.Domain;
using Inventory.Infrastructure.Persistence.Configurations;

namespace Inventory.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.

internal sealed class IngredientRepository(InventoryDbContext context) : IIngredientRepository
{
    public Task<Ingredient?> FindAsync(TenantId tenantId, IngredientId id, CancellationToken cancellationToken = default) =>
        context.Ingredients.SingleOrDefaultAsync(ingredient => ingredient.TenantId == tenantId && ingredient.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Ingredient>> FindManyAsync(
        TenantId tenantId, IEnumerable<IngredientId> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        return await context.Ingredients.AsNoTracking()
            .Where(ingredient => ingredient.TenantId == tenantId && wanted.Contains(ingredient.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Ingredient>> ListAsync(TenantId tenantId, bool includeInactive, CancellationToken cancellationToken = default) =>
        await context.Ingredients.AsNoTracking()
            .Where(ingredient => ingredient.TenantId == tenantId && (includeInactive || ingredient.IsActive))
            .OrderBy(ingredient => ingredient.Name)
            .ThenBy(ingredient => ingredient.Id)
            .ToListAsync(cancellationToken);

    public void Add(Ingredient ingredient) => context.Ingredients.Add(ingredient);
}

internal sealed class InventoryItemRepository(InventoryDbContext context) : IInventoryItemRepository
{
    // L'historique n'est JAMAIS chargé : seuls les mouvements ajoutés après le chargement sont en mémoire.
    public Task<InventoryItem?> FindAsync(TenantId tenantId, InventoryItemId id, CancellationToken cancellationToken = default) =>
        context.Items.SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Id == id, cancellationToken);

    public async Task<IReadOnlyList<InventoryItem>> FindAtLocationAsync(
        TenantId tenantId, LocationId locationId, IEnumerable<IngredientId> ingredientIds, CancellationToken cancellationToken = default)
    {
        var wanted = ingredientIds.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        return await context.Items
            .Where(item => item.TenantId == tenantId && item.LocationId == locationId && wanted.Contains(item.IngredientId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItem>> ListAsync(
        TenantId tenantId, LocationId? locationId, bool lowStockOnly, CancellationToken cancellationToken = default)
    {
        var query = context.Items.AsNoTracking().Where(item => item.TenantId == tenantId);
        if (locationId is { } location)
        {
            query = query.Where(item => item.LocationId == location);
        }

        if (lowStockOnly)
        {
            query = query.Where(item => item.CurrentQuantity <= item.ReorderThreshold.Amount);
        }

        return await query
            .OrderBy(item => item.LocationId)
            .ThenBy(item => item.IngredientId)
            .ToListAsync(cancellationToken);
    }

    public void Add(InventoryItem item) => context.Items.Add(item);
}

internal sealed class StockMovementLedger(InventoryDbContext context) : IStockMovementLedger
{
    public async Task<IReadOnlyList<StockMovement>> ListAsync(
        TenantId tenantId, InventoryItemId itemId, int skip, int take, CancellationToken cancellationToken = default) =>
        await context.Movements.AsNoTracking()
            .Where(movement => movement.TenantId == tenantId && movement.InventoryItemId == itemId)
            .OrderByDescending(movement => movement.OccurredAtUtc)
            .ThenByDescending(movement => movement.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
}

internal sealed class RecipeRepository(InventoryDbContext context) : IRecipeRepository
{
    public Task<Recipe?> FindAsync(TenantId tenantId, RecipeId id, CancellationToken cancellationToken = default) =>
        context.Recipes.SingleOrDefaultAsync(recipe => recipe.TenantId == tenantId && recipe.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Recipe>> ListForProductsAsync(
        TenantId tenantId, IEnumerable<ProductId> productIds, CancellationToken cancellationToken = default)
    {
        var wanted = productIds.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        return await context.Recipes.AsNoTracking()
            .Where(recipe => recipe.TenantId == tenantId && wanted.Contains(recipe.ProductId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Recipe>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.Recipes.AsNoTracking()
            .Where(recipe => recipe.TenantId == tenantId)
            .OrderBy(recipe => recipe.ProductId)
            .ThenBy(recipe => recipe.VariantId)
            .ToListAsync(cancellationToken);

    public void Add(Recipe recipe) => context.Recipes.Add(recipe);
}

internal sealed class OrderConsumptionLog(InventoryDbContext context) : IOrderConsumptionLog
{
    public Task<bool> HasConsumedAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
        context.OrderConsumptions.AnyAsync(consumption => consumption.TenantId == tenantId && consumption.OrderId == orderId, cancellationToken);

    public void Record(TenantId tenantId, OrderId orderId, LocationId locationId, DateTimeOffset consumedAtUtc) =>
        context.OrderConsumptions.Add(new OrderConsumption(tenantId, orderId, locationId, consumedAtUtc));
}

internal sealed class IngredientNameUniquenessChecker(InventoryDbContext context) : IIngredientNameUniquenessChecker
{
    public async Task<bool> IsUniqueAsync(
        TenantId tenantId, string name, IngredientId? excludedIngredientId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Trim().ToUpperInvariant();
        return !await context.Ingredients
            .Where(ingredient => ingredient.TenantId == tenantId && (excludedIngredientId == null || ingredient.Id != excludedIngredientId))
            .AnyAsync(ingredient => EF.Property<string>(ingredient, IngredientConfiguration.NormalizedNameProperty) == normalized, cancellationToken);
    }
}

internal sealed class InventoryItemUniquenessChecker(InventoryDbContext context) : IInventoryItemUniquenessChecker
{
    public async Task<bool> IsUniqueAsync(
        TenantId tenantId, LocationId locationId, IngredientId ingredientId, CancellationToken cancellationToken = default) =>
        !await context.Items.AnyAsync(
            item => item.TenantId == tenantId && item.LocationId == locationId && item.IngredientId == ingredientId, cancellationToken);
}

internal sealed class RecipeUniquenessChecker(InventoryDbContext context) : IRecipeUniquenessChecker
{
    public async Task<bool> IsUniqueAsync(
        TenantId tenantId, ProductId productId, VariantId? variantId, CancellationToken cancellationToken = default) =>
        !await context.Recipes.AnyAsync(
            recipe => recipe.TenantId == tenantId && recipe.ProductId == productId && recipe.VariantId == variantId, cancellationToken);
}

internal sealed class SupplierRepository(InventoryDbContext context) : ISupplierRepository
{
    public Task<Supplier?> FindAsync(TenantId tenantId, SupplierId id, CancellationToken cancellationToken = default) =>
        context.Suppliers.SingleOrDefaultAsync(supplier => supplier.TenantId == tenantId && supplier.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Supplier>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.Suppliers.AsNoTracking().Where(supplier => supplier.TenantId == tenantId).OrderBy(supplier => supplier.Name).ToListAsync(cancellationToken);

    public void Add(Supplier supplier) => context.Suppliers.Add(supplier);
}

internal sealed class PurchaseOrderRepository(InventoryDbContext context) : IPurchaseOrderRepository
{
    public Task<PurchaseOrder?> FindAsync(TenantId tenantId, PurchaseOrderId id, CancellationToken cancellationToken = default) =>
        context.PurchaseOrders.SingleOrDefaultAsync(order => order.TenantId == tenantId && order.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PurchaseOrder>> ListAsync(
        TenantId tenantId, LocationId? locationId, PurchaseOrderStatus? status, int take, CancellationToken cancellationToken = default)
    {
        var query = context.PurchaseOrders.AsNoTracking().Where(order => order.TenantId == tenantId);
        if (locationId is { } location)
        {
            query = query.Where(order => order.LocationId == location);
        }

        if (status is { } wanted)
        {
            query = query.Where(order => order.Status == wanted);
        }

        return await query.OrderByDescending(order => order.CreatedAtUtc).Take(take).ToListAsync(cancellationToken);
    }

    public void Add(PurchaseOrder order) => context.PurchaseOrders.Add(order);
}
