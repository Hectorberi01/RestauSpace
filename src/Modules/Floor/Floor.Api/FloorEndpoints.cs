using Floor.Application;
using Floor.Application.Commands;
using Floor.Application.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Floor.Api;

/// <summary>
/// Plan de salle et service du restaurant sélectionné dans l'access token. Aucune route ne prend de tenant : ce n'est
/// jamais une valeur de requête. L'hôte chaîne l'autorisation (<c>Tenancy.TenantMemberPolicy</c>) sur le groupe
/// renvoyé ; les lectures sont ouvertes à tout membre, sauf la file d'attente (coordonnées des clients) qui exige
/// <see cref="FloorPermissions.Serve"/>. Le plan exige <see cref="FloorPermissions.ManageLayout"/>, le
/// service (sessions, mise hors service) <see cref="FloorPermissions.Serve"/>.
/// </summary>
public static class FloorEndpoints
{
    private const string Layout = FloorPermissions.ManageLayout;
    private const string Serve = FloorPermissions.Serve;
    private const string GetDiningAreaRoute = "GetDiningArea";
    private const string GetTableSessionRoute = "GetTableSession";

    public static RouteGroupBuilder MapFloorEndpoints(this IEndpointRouteBuilder routes)
    {
        var floor = routes.MapGroup("/floor").WithTags("Floor")
            .AllowApiKeyRead(FloorPermissions.Serve, FloorPermissions.ManageLayout);
        MapDiningAreas(floor.MapGroup("/dining-areas"));
        MapTableSessions(floor.MapGroup("/table-sessions"));
        MapWaitingParties(floor.MapGroup("/waiting-parties"));

        // QR codes des tables : la commande à table sur le site du restaurant.
        floor.MapPost("/qr-codes", async (
            QrCodesRequest request, ICommandHandler<PrepareTableQrCodesCommand, IReadOnlyList<TableQrDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new PrepareTableQrCodesCommand(request.LocationId), ct)))
            .RequirePermission(Layout);

        floor.MapPost("/tables/{tableId:guid}/qr-token", async (
            Guid tableId, ICommandHandler<RotateTableQrTokenCommand, TableQrDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new RotateTableQrTokenCommand(tableId), ct)))
            .RequirePermission(Layout);
        return floor;
    }

    private static void MapDiningAreas(RouteGroupBuilder areas)
    {
        areas.MapPost("/", async (
            CreateDiningAreaRequest request, ICommandHandler<CreateDiningAreaCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateDiningAreaCommand(request.LocationId, request.Name), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetDiningAreaRoute, new { id });
        }).RequirePermission(Layout);

        // Plan de salle : zones, tables et session en cours de chaque table.
        areas.MapGet("/", async (
            Guid? locationId, IQueryHandler<ListDiningAreasQuery, IReadOnlyList<DiningAreaDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListDiningAreasQuery(locationId), ct)));

        areas.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetDiningAreaByIdQuery, DiningAreaDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetDiningAreaByIdQuery(id), ct)))
            .WithName(GetDiningAreaRoute);

        areas.MapPut("/{id:guid}/name", (
            Guid id, NameRequest request, ICommandHandler<RenameDiningAreaCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameDiningAreaCommand(id, request.Name), ct)))
            .RequirePermission(Layout);

        // Tables de la zone.
        areas.MapPost("/{id:guid}/tables", async (
            Guid id, AddTableRequest request, ICommandHandler<AddTableCommand, Guid> handler, CancellationToken ct) =>
        {
            var tableId = await handler.HandleAsync(new AddTableCommand(id, request.Label, request.Capacity), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(tableId), GetDiningAreaRoute, new { id });
        }).RequirePermission(Layout);

        areas.MapDelete("/{id:guid}", (
            Guid id, ICommandHandler<DeleteDiningAreaCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DeleteDiningAreaCommand(id), ct)))
            .RequirePermission(Layout);

        areas.MapPut("/{id:guid}/tables/{tableId:guid}/label", (
            Guid id, Guid tableId, TableLabelRequest request, ICommandHandler<RenameTableCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameTableCommand(id, tableId, request.Label), ct)))
            .RequirePermission(Layout);

        // Plan dessiné : place et forme de la table dans la grille de la salle.
        areas.MapPut("/{id:guid}/tables/{tableId:guid}/layout", (
            Guid id, Guid tableId, TableLayoutRequest request, ICommandHandler<PlaceTableCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new PlaceTableCommand(id, tableId, request.X, request.Y, request.Width, request.Height, request.Shape, request.Rotation), ct)))
            .RequirePermission(Layout);

        areas.MapPut("/{id:guid}/tables/{tableId:guid}/capacity", (
            Guid id, Guid tableId, CapacityRequest request, ICommandHandler<ChangeTableCapacityCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeTableCapacityCommand(id, tableId, request.Capacity), ct)))
            .RequirePermission(Layout);

        areas.MapDelete("/{id:guid}/tables/{tableId:guid}", (
            Guid id, Guid tableId, ICommandHandler<RemoveTableCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveTableCommand(id, tableId), ct)))
            .RequirePermission(Layout);

        areas.MapPost("/{id:guid}/tables/{tableId:guid}/out-of-service", (
            Guid id, Guid tableId, ICommandHandler<SetTableOutOfServiceCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetTableOutOfServiceCommand(id, tableId), ct)))
            .RequirePermission(Serve);

        areas.MapPost("/{id:guid}/tables/{tableId:guid}/restore", (
            Guid id, Guid tableId, ICommandHandler<RestoreTableCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RestoreTableCommand(id, tableId), ct)))
            .RequirePermission(Serve);
    }

    private static void MapTableSessions(RouteGroupBuilder sessions)
    {
        sessions.MapPost("/", async (
            OpenTableSessionRequest request, ICommandHandler<OpenTableSessionCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new OpenTableSessionCommand(request.TableId, request.GuestCount, request.ReservationId), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetTableSessionRoute, new { id });
        }).RequirePermission(Serve);

        sessions.MapGet("/", async (
            Guid? locationId, IQueryHandler<ListOpenTableSessionsQuery, IReadOnlyList<TableSessionDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListOpenTableSessionsQuery(locationId), ct)));

        sessions.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetTableSessionByIdQuery, TableSessionDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetTableSessionByIdQuery(id), ct)))
            .WithName(GetTableSessionRoute);

        sessions.MapPut("/{id:guid}/guest-count", (
            Guid id, GuestCountRequest request, ICommandHandler<ChangeGuestCountCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeGuestCountCommand(id, request.GuestCount), ct)))
            .RequirePermission(Serve);

        sessions.MapPost("/{id:guid}/move", (
            Guid id, MoveTableSessionRequest request, ICommandHandler<MoveTableSessionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MoveTableSessionCommand(id, request.TableId), ct)))
            .RequirePermission(Serve);

        sessions.MapPost("/{id:guid}/merge", (
            Guid id, MergeTableSessionsRequest request, ICommandHandler<MergeTableSessionsCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MergeTableSessionsCommand(id, request.IntoTableSessionId), ct)))
            .RequirePermission(Serve);

        sessions.MapPost("/{id:guid}/close", (
            Guid id, ICommandHandler<CloseTableSessionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CloseTableSessionCommand(id), ct)))
            .RequirePermission(Serve);
    }

    /// <summary>File d'attente à la porte : groupes sans réservation, prévenus quand leur table est prête.</summary>
    private static void MapWaitingParties(RouteGroupBuilder parties)
    {
        parties.MapGet("/", async (
            Guid locationId, IQueryHandler<ListWaitingPartiesQuery, IReadOnlyList<WaitingPartyDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListWaitingPartiesQuery(locationId), ct)))
            .RequirePermission(Serve);

        parties.MapPost("/", async (
            AddWaitingPartyRequest request, ICommandHandler<AddWaitingPartyCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(
                new AddWaitingPartyCommand(request.LocationId, request.Name, request.PartySize, request.Phone, request.Note, request.QuotedMinutes), ct))))
            .RequirePermission(Serve);

        parties.MapPost("/{id:guid}/notify", (
            Guid id, ICommandHandler<NotifyWaitingPartyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new NotifyWaitingPartyCommand(id), ct)))
            .RequirePermission(Serve);

        parties.MapPost("/{id:guid}/seat", async (
            Guid id, SeatWaitingPartyRequest request, ICommandHandler<SeatWaitingPartyCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(new SeatWaitingPartyCommand(id, request.TableId), ct))))
            .RequirePermission(Serve);

        parties.MapPost("/{id:guid}/remove", (
            Guid id, ICommandHandler<RemoveWaitingPartyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveWaitingPartyCommand(id), ct)))
            .RequirePermission(Serve);
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
