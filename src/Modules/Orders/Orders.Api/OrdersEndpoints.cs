using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Orders.Application;
using Orders.Application.Commands;
using Orders.Application.Queries;
using Orders.Domain;
using SaasRestaurant.BuildingBlocks.Api;

namespace Orders.Api;

/// <summary>
/// Commandes du restaurant sélectionné dans l'access token. Aucune route ne prend de tenant : ce n'est jamais une valeur
/// de requête. L'hôte chaîne l'autorisation (<c>Tenancy.TenantMemberPolicy</c>) sur le groupe renvoyé ; les lectures
/// sont ouvertes à tout membre. Prise de commande : <see cref="OrdersPermissions.Take"/> ; suivi et annulation :
/// <see cref="OrdersPermissions.Process"/> ; remises manuelles : <see cref="OrdersPermissions.Discount"/>.
/// </summary>
public static class OrdersEndpoints
{
    private const string Take = OrdersPermissions.Take;
    private const string Process = OrdersPermissions.Process;
    private const string GetOrderRoute = "GetOrder";

    // Permission du module Customers (référencée par son code : pas de dépendance entre modules). Relier des commandes
    // à une personne est une donnée personnelle : cet historique exige le droit de consulter les fiches clients.
    private const string ViewCustomers = "customers.view";

    public static RouteGroupBuilder MapOrdersEndpoints(this IEndpointRouteBuilder routes)
    {
        var orders = routes.MapGroup("/orders").WithTags("Orders")
            .AllowApiKeyRead(OrdersPermissions.Take, OrdersPermissions.Process);

        // Le canal vient de l'appelant authentifié : une clé d'API serveur donne « Api », le personnel « Counter », ou
        // « Phone » / « WhatsApp » qu'il déclare pour une commande reçue à distance (jamais un canal en ligne).
        orders.MapPost("/", async (
            CreateOrderRequest request, HttpContext http, ICommandHandler<CreateOrderCommand, Guid> handler, CancellationToken ct) =>
        {
            // Une commande de plateforme peut être saisie par l'équipe ou poussée par un intégrateur (clé d'API).
            var origin = request.Channel == OrderChannel.DeliveryPlatform || request.Platform is not null
                ? OrderOrigin.FromPlatform(request.Platform, request.ExternalReference)
                : ApiKeyClaims.IsApiKey(http.User)
                    ? new OrderOrigin(OrderChannel.Api)
                    : request.Channel is OrderChannel.Phone or OrderChannel.WhatsApp ? new OrderOrigin(request.Channel.Value) : OrderOrigin.Counter;
            var id = await handler.HandleAsync(
                new CreateOrderCommand(request.LocationId, request.Type, request.TableSessionId, request.CustomerId, origin, request.ScheduledForUtc), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetOrderRoute, new { id });
        }).RequirePermission(Take);

        // Historique : filtres cumulés côté serveur (période de création, numéro ou sa fin, type, canal, total TTC).
        orders.MapGet("/", async (
            [AsParameters] OrderHistoryFilter filter,
            IQueryHandler<ListOrdersQuery, PagedResult<OrderSummaryDto>> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(filter.ToQuery(null), ct)));

        // Les commandes d'un client : mêmes filtres, réservé à customers.view.
        orders.MapGet("/by-customer/{customerId:guid}", async (
            Guid customerId,
            [AsParameters] OrderHistoryFilter filter,
            IQueryHandler<ListOrdersQuery, PagedResult<OrderSummaryDto>> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(filter.ToQuery(customerId), ct)))
            .RequirePermission(ViewCustomers);

        orders.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetOrderByIdQuery, OrderDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetOrderByIdQuery(id), ct)))
            .WithName(GetOrderRoute);

        // Lignes du brouillon.
        orders.MapPut("/{id:guid}/customer", (
            Guid id, OrderCustomerRequest request, ICommandHandler<ChangeOrderCustomerCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeOrderCustomerCommand(id, request.CustomerId), ct)))
            .RequirePermission(Take);

        orders.MapPost("/{id:guid}/items", async (
            Guid id, AddItemRequest request, ICommandHandler<AddOrderItemCommand, Guid> handler, CancellationToken ct) =>
        {
            var itemId = await handler.HandleAsync(
                new AddOrderItemCommand(id, request.ProductId, request.Quantity, request.VariantId, request.ModifierIds, request.Note, request.Components), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(itemId), GetOrderRoute, new { id });
        }).RequirePermission(Take);

        // Recomposer une ligne du brouillon (variante, options, quantité, note) : la ligne garde sa place.
        orders.MapPut("/{id:guid}/items/{itemId:guid}", async (
            Guid id, Guid itemId, ReplaceItemRequest request, ICommandHandler<ReplaceOrderItemCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(
                new ReplaceOrderItemCommand(id, itemId, request.Quantity, request.VariantId, request.ModifierIds, request.Note, request.Components), ct))))
            .RequirePermission(Take);

        // Changer de table une commande sur place en cours.
        orders.MapPut("/{id:guid}/table-session", (
            Guid id, TableSessionRequest request, ICommandHandler<MoveOrderToTableSessionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MoveOrderToTableSessionCommand(id, request.TableSessionId), ct)))
            .RequirePermission(Take);

        orders.MapPut("/{id:guid}/items/{itemId:guid}/quantity", (
            Guid id, Guid itemId, QuantityRequest request, ICommandHandler<ChangeOrderItemQuantityCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeOrderItemQuantityCommand(id, itemId, request.Quantity), ct)))
            .RequirePermission(Take);

        orders.MapPut("/{id:guid}/items/{itemId:guid}/note", (
            Guid id, Guid itemId, NoteRequest request, ICommandHandler<ChangeOrderItemNoteCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeOrderItemNoteCommand(id, itemId, request.Note), ct)))
            .RequirePermission(Take);

        orders.MapDelete("/{id:guid}/items/{itemId:guid}", (
            Guid id, Guid itemId, ICommandHandler<RemoveOrderItemCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveOrderItemCommand(id, itemId), ct)))
            .RequirePermission(Take);

        // Remises manuelles.
        orders.MapPost("/{id:guid}/discounts", (
            Guid id, ManualDiscountRequest request, ICommandHandler<ApplyManualDiscountCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ApplyManualDiscountCommand(id, request.Amount, request.Reference, request.Label), ct)))
            .RequirePermission(OrdersPermissions.Discount);

        orders.MapDelete("/{id:guid}/discounts/{source}/{reference}", (
            Guid id, DiscountSource source, string reference, ICommandHandler<RemoveDiscountCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveDiscountCommand(id, source, reference), ct)))
            .RequirePermission(OrdersPermissions.Discount);

        // Cycle de vie.
        orders.MapPost("/{id:guid}/place", (
            Guid id, ICommandHandler<PlaceOrderCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new PlaceOrderCommand(id), ct)))
            .RequirePermission(Take);

        orders.MapPost("/{id:guid}/accept", (
            Guid id, ICommandHandler<AcceptOrderCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AcceptOrderCommand(id), ct)))
            .RequirePermission(Process);

        orders.MapPost("/{id:guid}/start-preparation", (
            Guid id, ICommandHandler<StartOrderPreparationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new StartOrderPreparationCommand(id), ct)))
            .RequirePermission(Process);

        orders.MapPost("/{id:guid}/ready", (
            Guid id, ICommandHandler<MarkOrderReadyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MarkOrderReadyCommand(id), ct)))
            .RequirePermission(Process);

        orders.MapPost("/{id:guid}/complete", (
            Guid id, ICommandHandler<CompleteOrderCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CompleteOrderCommand(id), ct)))
            .RequirePermission(Process);

        orders.MapPost("/{id:guid}/cancel", (
            Guid id, CancelOrderRequest request, ICommandHandler<CancelOrderCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CancelOrderCommand(id, request.Reason), ct)))
            .RequirePermission(Process);

        return orders;
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
