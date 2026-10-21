using Kitchen.Application;
using Kitchen.Application.Commands;
using Kitchen.Application.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Kitchen.Api;

/// <summary>
/// Cuisine du restaurant sélectionné dans l'access token. Aucune route ne prend de tenant : ce n'est jamais une valeur de
/// requête. L'hôte chaîne l'autorisation (<c>Tenancy.TenantMemberPolicy</c>) sur le groupe renvoyé ; les lectures sont
/// ouvertes à tout membre. Postes et routage : <see cref="KitchenPermissions.Configure"/> ; tickets :
/// <see cref="KitchenPermissions.Operate"/>. Les tickets naissent de l'acceptation des commandes (l'hôte relaie
/// l'événement) ; <c>POST /kitchen/tickets</c> sert à créer celui d'une commande acceptée avant que la cuisine soit
/// configurée.
/// </summary>
public static class KitchenEndpoints
{
    private const string Configure = KitchenPermissions.Configure;
    private const string Operate = KitchenPermissions.Operate;
    private const string GetTicketRoute = "GetKitchenTicket";
    private const string GetRoutingRoute = "GetStationRouting";

    public static RouteGroupBuilder MapKitchenEndpoints(this IEndpointRouteBuilder routes)
    {
        var kitchen = routes.MapGroup("/kitchen").WithTags("Kitchen")
            .AllowApiKeyRead(KitchenPermissions.Operate, KitchenPermissions.Configure);
        MapStations(kitchen.MapGroup("/stations"));
        MapRoutings(kitchen.MapGroup("/routings"));
        MapTickets(kitchen.MapGroup("/tickets"));
        return kitchen;
    }

    private static void MapStations(RouteGroupBuilder stations)
    {
        stations.MapPost("/", async (
            CreateStationRequest request, ICommandHandler<CreateStationCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateStationCommand(request.LocationId, request.Name), ct);
            return TypedResults.Created($"/kitchen/stations/{id}", new CreatedResponse(id));
        })
            .RequirePermission(Configure);

        stations.MapGet("/", async (
            Guid locationId, IQueryHandler<ListStationsQuery, IReadOnlyList<StationDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListStationsQuery(locationId), ct)));

        stations.MapPut("/{id:guid}/name", (
            Guid id, NameRequest request, ICommandHandler<RenameStationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameStationCommand(id, request.Name), ct)))
            .RequirePermission(Configure);

        stations.MapPost("/{id:guid}/activate", (
            Guid id, ICommandHandler<ActivateStationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ActivateStationCommand(id), ct)))
            .RequirePermission(Configure);

        stations.MapPost("/{id:guid}/deactivate", (
            Guid id, ICommandHandler<DeactivateStationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DeactivateStationCommand(id), ct)))
            .RequirePermission(Configure);
    }

    // Un routage par établissement : adressé par l'id de l'établissement.
    private static void MapRoutings(RouteGroupBuilder routings)
    {
        routings.MapPost("/{locationId:guid}", async (
            Guid locationId, CreateRoutingRequest request, ICommandHandler<CreateStationRoutingCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateStationRoutingCommand(locationId, request.DefaultStationId), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetRoutingRoute, new { locationId });
        }).RequirePermission(Configure);

        routings.MapGet("/{locationId:guid}", async (
            Guid locationId, IQueryHandler<GetStationRoutingQuery, StationRoutingDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetStationRoutingQuery(locationId), ct)))
            .WithName(GetRoutingRoute);

        routings.MapPut("/{locationId:guid}/default-station", (
            Guid locationId, StationRequest request, ICommandHandler<ChangeDefaultStationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeDefaultStationCommand(locationId, request.StationId), ct)))
            .RequirePermission(Configure);

        routings.MapPut("/{locationId:guid}/routes/{productId:guid}", (
            Guid locationId, Guid productId, StationRequest request, ICommandHandler<SetStationRouteCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetStationRouteCommand(locationId, productId, request.StationId), ct)))
            .RequirePermission(Configure);

        // Service du produit (entrée, plat, dessert) : à table, les services suivants attendent l'envoi par la salle.
        routings.MapPut("/{locationId:guid}/courses/{productId:guid}", (
            Guid locationId, Guid productId, CourseRequest request, ICommandHandler<SetProductCourseCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetProductCourseCommand(locationId, productId, request.Course), ct)))
            .RequirePermission(Configure);

        routings.MapDelete("/{locationId:guid}/routes/{productId:guid}", (
            Guid locationId, Guid productId, ICommandHandler<RemoveStationRouteCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveStationRouteCommand(locationId, productId), ct)))
            .RequirePermission(Configure);
    }

    private static void MapTickets(RouteGroupBuilder tickets)
    {
        tickets.MapPost("/", async (
            CreateTicketRequest request, ICommandHandler<CreateTicketForOrderCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateTicketForOrderCommand(request.OrderId), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetTicketRoute, new { id });
        }).RequirePermission(Operate);

        // Écran cuisine : tickets ouverts, par priorité puis ancienneté.
        tickets.MapGet("/", async (
            Guid? locationId, Guid? stationId, IQueryHandler<ListOpenTicketsQuery, IReadOnlyList<KitchenTicketDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListOpenTicketsQuery(locationId, stationId), ct)));

        tickets.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetTicketByIdQuery, KitchenTicketDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetTicketByIdQuery(id), ct)))
            .WithName(GetTicketRoute);

        tickets.MapPost("/{id:guid}/start", (
            Guid id, ICommandHandler<StartTicketCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new StartTicketCommand(id), ct)))
            .RequirePermission(Operate);

        tickets.MapPost("/{id:guid}/items/{itemId:guid}/start", (
            Guid id, Guid itemId, ICommandHandler<StartTicketItemCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new StartTicketItemCommand(id, itemId), ct)))
            .RequirePermission(Operate);

        tickets.MapPost("/{id:guid}/items/{itemId:guid}/ready", (
            Guid id, Guid itemId, ICommandHandler<MarkTicketItemReadyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MarkTicketItemReadyCommand(id, itemId), ct)))
            .RequirePermission(Operate);

        tickets.MapPost("/{id:guid}/ready", (
            Guid id, ICommandHandler<MarkTicketReadyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MarkTicketReadyCommand(id), ct)))
            .RequirePermission(Operate);

        tickets.MapPost("/{id:guid}/complete", (
            Guid id, ICommandHandler<CompleteTicketCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CompleteTicketCommand(id), ct)))
            .RequirePermission(Operate);

        tickets.MapPost("/{id:guid}/cancel", (
            Guid id, CancelTicketRequest? request, ICommandHandler<CancelTicketCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CancelTicketCommand(id, request?.Reason), ct)))
            .RequirePermission(Operate);

        tickets.MapPost("/{id:guid}/fire", (
            Guid id, CourseRequest request, ICommandHandler<FireCourseCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new FireCourseCommand(id, request.Course), ct)))
            .RequirePermission(Operate);

        tickets.MapPut("/{id:guid}/priority", (
            Guid id, PriorityRequest request, ICommandHandler<ChangeTicketPriorityCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeTicketPriorityCommand(id, request.Priority), ct)))
            .RequirePermission(Operate);
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
