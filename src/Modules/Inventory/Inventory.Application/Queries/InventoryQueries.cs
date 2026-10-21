using Inventory.Application.Abstractions;
using Inventory.Domain;

namespace Inventory.Application.Queries;

// Lectures du tenant courant, ouvertes à tout membre et possibles même quand le tenant est suspendu. Une ressource d'un
// autre tenant n'est jamais trouvée.

/// <summary>Ingrédients par nom ; les désactivés seulement si <paramref name="IncludeInactive"/>.</summary>
public sealed record ListIngredientsQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<IngredientDto>>;

public sealed class ListIngredientsHandler(InventoryWorkspace workspace) : IQueryHandler<ListIngredientsQuery, IReadOnlyList<IngredientDto>>
{
    public async Task<IReadOnlyList<IngredientDto>> HandleAsync(ListIngredientsQuery query, CancellationToken cancellationToken = default) =>
        (await workspace.Ingredients.ListAsync(workspace.CurrentTenant, query.IncludeInactive, cancellationToken))
            .Select(IngredientDto.From)
            .ToList();
}

public sealed record GetIngredientByIdQuery(Guid IngredientId) : IQuery<IngredientDto>;

public sealed class GetIngredientByIdHandler(InventoryWorkspace workspace) : IQueryHandler<GetIngredientByIdQuery, IngredientDto>
{
    public async Task<IngredientDto> HandleAsync(GetIngredientByIdQuery query, CancellationToken cancellationToken = default) =>
        IngredientDto.From(await workspace.LoadIngredientAsync(workspace.CurrentTenant, query.IngredientId, cancellationToken));
}

/// <summary>
/// Articles de stock (tous établissements, ou un seul) ; <paramref name="LowStockOnly"/> : la liste de réapprovisionnement.
/// </summary>
public sealed record ListInventoryItemsQuery(Guid? LocationId = null, bool LowStockOnly = false) : IQuery<IReadOnlyList<InventoryItemDto>>;

public sealed class ListInventoryItemsHandler(InventoryWorkspace workspace)
    : IQueryHandler<ListInventoryItemsQuery, IReadOnlyList<InventoryItemDto>>
{
    public async Task<IReadOnlyList<InventoryItemDto>> HandleAsync(ListInventoryItemsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        LocationId? location = null;
        if (query.LocationId is { } locationId)
        {
            location = (await workspace.RequireLocationAsync(tenant, locationId, cancellationToken)).LocationId;
        }

        var items = await workspace.Items.ListAsync(tenant, location, query.LowStockOnly, cancellationToken);
        var names = await IngredientNames.OfAsync(workspace, tenant, items.Select(item => item.IngredientId), cancellationToken);
        return items.Select(item => InventoryItemDto.From(item, names.GetValueOrDefault(item.IngredientId))).ToList();
    }
}

public sealed record GetInventoryItemByIdQuery(Guid ItemId) : IQuery<InventoryItemDto>;

public sealed class GetInventoryItemByIdHandler(InventoryWorkspace workspace) : IQueryHandler<GetInventoryItemByIdQuery, InventoryItemDto>
{
    public async Task<InventoryItemDto> HandleAsync(GetInventoryItemByIdQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var item = await workspace.LoadItemAsync(tenant, query.ItemId, cancellationToken);
        var names = await IngredientNames.OfAsync(workspace, tenant, [item.IngredientId], cancellationToken);
        return InventoryItemDto.From(item, names.GetValueOrDefault(item.IngredientId));
    }
}

/// <summary>Le registre d'un article, les plus récents d'abord (au plus <see cref="MaxTake"/>).</summary>
public sealed record ListStockMovementsQuery(Guid ItemId, int Take = ListStockMovementsQuery.DefaultTake, int Skip = 0) : IQuery<IReadOnlyList<StockMovementDto>>
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
}

public sealed class ListStockMovementsHandler(InventoryWorkspace workspace)
    : IQueryHandler<ListStockMovementsQuery, IReadOnlyList<StockMovementDto>>
{
    public async Task<IReadOnlyList<StockMovementDto>> HandleAsync(ListStockMovementsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var item = await workspace.LoadItemAsync(tenant, query.ItemId, cancellationToken);
        var take = Math.Clamp(query.Take, 1, ListStockMovementsQuery.MaxTake);
        return (await workspace.Movements.ListAsync(tenant, item.Id, Math.Max(0, query.Skip), take, cancellationToken)).Select(StockMovementDto.From).ToList();
    }
}

public sealed record GetRecipeByIdQuery(Guid RecipeId) : IQuery<RecipeDto>;

public sealed class GetRecipeByIdHandler(InventoryWorkspace workspace) : IQueryHandler<GetRecipeByIdQuery, RecipeDto>
{
    public async Task<RecipeDto> HandleAsync(GetRecipeByIdQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var recipe = await workspace.LoadRecipeAsync(tenant, query.RecipeId, cancellationToken);
        var names = await IngredientNames.OfAsync(workspace, tenant, recipe.Ingredients.Select(component => component.IngredientId), cancellationToken);
        return RecipeDto.From(recipe, names);
    }
}

/// <summary>Recettes du restaurant ; <paramref name="ProductId"/> : seulement celles de ce produit (et de ses variantes).</summary>
public sealed record ListRecipesQuery(Guid? ProductId = null) : IQuery<IReadOnlyList<RecipeDto>>;

public sealed class ListRecipesHandler(InventoryWorkspace workspace) : IQueryHandler<ListRecipesQuery, IReadOnlyList<RecipeDto>>
{
    public async Task<IReadOnlyList<RecipeDto>> HandleAsync(ListRecipesQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        if (query.ProductId == Guid.Empty)
        {
            return [];
        }

        var recipes = query.ProductId is { } productId
            ? await workspace.Recipes.ListForProductsAsync(tenant, [ProductId.From(productId)], cancellationToken)
            : await workspace.Recipes.ListAsync(tenant, cancellationToken);
        var names = await IngredientNames.OfAsync(
            workspace, tenant, recipes.SelectMany(recipe => recipe.Ingredients).Select(component => component.IngredientId), cancellationToken);
        return recipes.Select(recipe => RecipeDto.From(recipe, names)).ToList();
    }
}

internal static class IngredientNames
{
    public static async Task<IReadOnlyDictionary<IngredientId, string>> OfAsync(
        InventoryWorkspace workspace, TenantId tenant, IEnumerable<IngredientId> ids, CancellationToken cancellationToken) =>
        (await workspace.Ingredients.FindManyAsync(tenant, ids.Distinct(), cancellationToken))
            .ToDictionary(ingredient => ingredient.Id, ingredient => ingredient.Name);
}
