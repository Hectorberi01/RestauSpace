using Inventory.Application;
using Inventory.Application.Commands;
using Inventory.Application.Queries;
using Inventory.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Inventory.Api;

/// <summary>
/// Stock du restaurant sélectionné dans l'access token. Aucune route ne prend de tenant : ce n'est jamais une valeur de
/// requête. L'hôte chaîne l'autorisation (<c>Tenancy.TenantMemberPolicy</c>) sur le groupe renvoyé ; les lectures sont
/// ouvertes à tout membre. Référentiel (ingrédients, articles, seuils, recettes) : <see cref="InventoryPermissions.Manage"/> ;
/// mouvements : <see cref="InventoryPermissions.Operate"/>. La consommation des commandes est déclenchée par l'hôte au
/// début de leur préparation ; <c>POST /inventory/orders/{id}/consumption</c> la relance à la main (idempotent).
/// </summary>
public static class InventoryEndpoints
{
    private const string Manage = InventoryPermissions.Manage;
    private const string Operate = InventoryPermissions.Operate;
    private const string GetIngredientRoute = "GetIngredient";
    private const string GetItemRoute = "GetInventoryItem";
    private const string GetRecipeRoute = "GetRecipe";

    public static RouteGroupBuilder MapInventoryEndpoints(this IEndpointRouteBuilder routes)
    {
        var inventory = routes.MapGroup("/inventory").WithTags("Inventory")
            .AllowApiKeyRead(InventoryPermissions.Manage, InventoryPermissions.Operate);
        MapIngredients(inventory.MapGroup("/ingredients"));
        MapItems(inventory.MapGroup("/items"));
        MapRecipes(inventory.MapGroup("/recipes"));
        MapSuppliers(inventory.MapGroup("/suppliers"));
        MapPurchaseOrders(inventory.MapGroup("/purchase-orders"));

        inventory.MapGet("/reorder-suggestions", async (
            Guid? locationId, IQueryHandler<GetReorderSuggestionsQuery, IReadOnlyList<ReorderSuggestionDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetReorderSuggestionsQuery(locationId ?? Guid.Empty), ct)));

        inventory.MapGet("/recipe-costs", async (
            Guid? locationId, IQueryHandler<GetRecipeCostsQuery, IReadOnlyList<RecipeCostDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetRecipeCostsQuery(locationId ?? Guid.Empty), ct)));

        inventory.MapPost("/transfers", (
            TransferRequest request, ICommandHandler<TransferStockCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(
                new TransferStockCommand(request.SourceItemId, request.DestinationItemId, request.Quantity, request.Reference), ct)))
            .RequirePermission(Operate);

        inventory.MapPost("/orders/{orderId:guid}/consumption", async (
            Guid orderId, ICommandHandler<ConsumeStockForOrderCommand, OrderConsumptionDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ConsumeStockForOrderCommand(orderId), ct)))
            .RequirePermission(Operate);

        return inventory;
    }

    private static void MapIngredients(RouteGroupBuilder ingredients)
    {
        ingredients.MapPost("/", async (
            CreateIngredientRequest request, ICommandHandler<CreateIngredientCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateIngredientCommand(request.Name, request.BaseUnit), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetIngredientRoute, new { id });
        }).RequirePermission(Manage);

        ingredients.MapGet("/", async (
            bool? includeInactive, IQueryHandler<ListIngredientsQuery, IReadOnlyList<IngredientDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListIngredientsQuery(includeInactive ?? false), ct)));

        ingredients.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetIngredientByIdQuery, IngredientDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetIngredientByIdQuery(id), ct)))
            .WithName(GetIngredientRoute);

        ingredients.MapPut("/{id:guid}/name", (
            Guid id, NameRequest request, ICommandHandler<RenameIngredientCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameIngredientCommand(id, request.Name), ct)))
            .RequirePermission(Manage);

        ingredients.MapPost("/{id:guid}/deactivate", (
            Guid id, ICommandHandler<DeactivateIngredientCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DeactivateIngredientCommand(id), ct)))
            .RequirePermission(Manage);
    }

    private static void MapItems(RouteGroupBuilder items)
    {
        items.MapPost("/", async (
            CreateItemRequest request, ICommandHandler<CreateInventoryItemCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateInventoryItemCommand(
                    request.LocationId, request.IngredientId, request.Unit, request.ReorderThreshold, request.NegativeStockPolicy),
                ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetItemRoute, new { id });
        }).RequirePermission(Manage);

        // Sans établissement : tous ; lowStockOnly : la liste de réapprovisionnement.
        items.MapGet("/", async (
            Guid? locationId, bool? lowStockOnly, IQueryHandler<ListInventoryItemsQuery, IReadOnlyList<InventoryItemDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListInventoryItemsQuery(locationId, lowStockOnly ?? false), ct)));

        items.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetInventoryItemByIdQuery, InventoryItemDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetInventoryItemByIdQuery(id), ct)))
            .WithName(GetItemRoute);

        items.MapGet("/{id:guid}/movements", async (
            Guid id, int? page, int? pageSize, IQueryHandler<ListStockMovementsQuery, IReadOnlyList<StockMovementDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(new ListStockMovementsQuery(id, take, skip), ct))));

        items.MapPost("/{id:guid}/receipts", (
            Guid id, MovementRequest request, ICommandHandler<ReceiveStockCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ReceiveStockCommand(id, request.Quantity, request.Reference, request.TotalCost), ct)))
            .RequirePermission(Operate);

        items.MapPut("/{id:guid}/unit-cost", (
            Guid id, UnitCostRequest request, ICommandHandler<SetUnitCostCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetUnitCostCommand(id, request.UnitCost), ct)))
            .RequirePermission(Manage);

        items.MapPost("/{id:guid}/consumptions", (
            Guid id, MovementRequest request, ICommandHandler<ConsumeStockCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ConsumeStockCommand(id, request.Quantity, request.Reference), ct)))
            .RequirePermission(Operate);

        items.MapPost("/{id:guid}/waste", (
            Guid id, WasteRequest request, ICommandHandler<RecordWasteCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RecordWasteCommand(id, request.Quantity, request.Reason, request.Reference), ct)))
            .RequirePermission(Operate);

        items.MapPost("/{id:guid}/counts", (
            Guid id, CountRequest request, ICommandHandler<CountStockCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CountStockCommand(id, request.CountedQuantity, request.Reason, request.Reference), ct)))
            .RequirePermission(Operate);

        items.MapPut("/{id:guid}/reorder-threshold", (
            Guid id, ThresholdRequest request, ICommandHandler<ChangeReorderThresholdCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeReorderThresholdCommand(id, request.Threshold), ct)))
            .RequirePermission(Manage);

        items.MapPut("/{id:guid}/negative-stock-policy", (
            Guid id, PolicyRequest request, ICommandHandler<ChangeNegativeStockPolicyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeNegativeStockPolicyCommand(id, request.Policy), ct)))
            .RequirePermission(Manage);
    }

    private static void MapRecipes(RouteGroupBuilder recipes)
    {
        recipes.MapPost("/", async (
            CreateRecipeRequest request, ICommandHandler<CreateRecipeCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateRecipeCommand(request.ProductId, request.VariantId), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetRecipeRoute, new { id });
        }).RequirePermission(Manage);

        recipes.MapGet("/", async (
            Guid? productId, IQueryHandler<ListRecipesQuery, IReadOnlyList<RecipeDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListRecipesQuery(productId), ct)));

        recipes.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetRecipeByIdQuery, RecipeDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetRecipeByIdQuery(id), ct)))
            .WithName(GetRecipeRoute);

        recipes.MapPost("/{id:guid}/ingredients", (
            Guid id, RecipeIngredientRequest request, ICommandHandler<AddRecipeIngredientCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AddRecipeIngredientCommand(id, request.IngredientId, request.Quantity), ct)))
            .RequirePermission(Manage);

        recipes.MapPut("/{id:guid}/ingredients/{ingredientId:guid}", (
            Guid id, Guid ingredientId, RecipeQuantityRequest request, ICommandHandler<ChangeRecipeQuantityCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeRecipeQuantityCommand(id, ingredientId, request.Quantity), ct)))
            .RequirePermission(Manage);

        recipes.MapDelete("/{id:guid}/ingredients/{ingredientId:guid}", (
            Guid id, Guid ingredientId, ICommandHandler<RemoveRecipeIngredientCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveRecipeIngredientCommand(id, ingredientId), ct)))
            .RequirePermission(Manage);
    }

    private static void MapSuppliers(RouteGroupBuilder suppliers)
    {
        suppliers.MapGet("/", async (
            IQueryHandler<ListSuppliersQuery, IReadOnlyList<SupplierDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListSuppliersQuery(), ct)));

        suppliers.MapPost("/", async (
            SupplierRequest request, ICommandHandler<SaveSupplierCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(
                new SaveSupplierCommand(null, request.Name, request.Phone, request.Email, request.Notes), ct))))
            .RequirePermission(Manage);

        suppliers.MapPut("/{id:guid}", (
            Guid id, SupplierRequest request, ICommandHandler<SaveSupplierCommand, Guid> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SaveSupplierCommand(id, request.Name, request.Phone, request.Email, request.Notes), ct)))
            .RequirePermission(Manage);

        suppliers.MapPut("/{id:guid}/active", (
            Guid id, ActiveRequest request, ICommandHandler<SetSupplierActiveCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetSupplierActiveCommand(id, request.IsActive), ct)))
            .RequirePermission(Manage);
    }

    private static void MapPurchaseOrders(RouteGroupBuilder orders)
    {
        orders.MapGet("/", async (
            Guid? locationId, PurchaseOrderStatus? status, IQueryHandler<ListPurchaseOrdersQuery, IReadOnlyList<PurchaseOrderDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListPurchaseOrdersQuery(locationId, status), ct)));

        orders.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetPurchaseOrderQuery(id), ct)));

        orders.MapPost("/", async (
            CreatePurchaseOrderRequest request, ICommandHandler<CreatePurchaseOrderCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(
                new CreatePurchaseOrderCommand(request.LocationId, request.SupplierId, request.Lines, request.Notes), ct))))
            .RequirePermission(Operate);

        orders.MapPut("/{id:guid}", (
            Guid id, ComposePurchaseOrderRequest request, ICommandHandler<ComposePurchaseOrderCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ComposePurchaseOrderCommand(id, request.Lines, request.Notes), ct)))
            .RequirePermission(Operate);

        orders.MapPost("/{id:guid}/send", (
            Guid id, ICommandHandler<SendPurchaseOrderCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SendPurchaseOrderCommand(id), ct)))
            .RequirePermission(Operate);

        orders.MapPost("/{id:guid}/receipts", (
            Guid id, ReceivePurchaseOrderRequest request, ICommandHandler<ReceivePurchaseOrderCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ReceivePurchaseOrderCommand(id, request.Lines, request.Close), ct)))
            .RequirePermission(Operate);

        orders.MapPost("/{id:guid}/cancel", (
            Guid id, ICommandHandler<CancelPurchaseOrderCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CancelPurchaseOrderCommand(id), ct)))
            .RequirePermission(Operate);
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
