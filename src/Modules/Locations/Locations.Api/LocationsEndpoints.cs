using Locations.Application;
using Locations.Application.Commands;
using Locations.Application.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Locations.Api;

/// <summary>
/// Établissements du restaurant sélectionné dans l'access token. Aucune route ne prend de tenant : ce n'est jamais une
/// valeur de requête. L'hôte chaîne l'autorisation (<c>Tenancy.TenantMemberPolicy</c>) sur le groupe renvoyé ; les
/// lectures sont ouvertes à tout membre, chaque écriture exige en plus <see cref="LocationsPermissions.Manage"/>.
/// </summary>
public static class LocationsEndpoints
{
    private const string GetLocationRoute = "GetLocation";

    public static RouteGroupBuilder MapLocationsEndpoints(this IEndpointRouteBuilder routes)
    {
        var locations = routes.MapGroup("/locations").WithTags("Locations")
            .AllowApiKeyRead(LocationsPermissions.Manage, "catalog.manage", "orders.take");

        locations.MapPost("/", async (
            CreateLocationRequest request, ICommandHandler<CreateLocationCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateLocationCommand(request.Name, request.Address, request.Seats, request.TimeZoneId, request.WeeklyHours), ct);
            return TypedResults.CreatedAtRoute(new LocationCreatedResponse(id), GetLocationRoute, new { id });
        }).RequirePermission(LocationsPermissions.Manage);

        locations.MapGet("/", async (
            int? page,
            int? pageSize,
            bool? includeArchived,
            IQueryHandler<ListLocationsQuery, PagedResult<LocationSummaryDto>> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new ListLocationsQuery(page ?? 1, pageSize ?? ListLocationsQuery.DefaultPageSize, includeArchived ?? false), ct)));

        locations.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetLocationByIdQuery, LocationDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetLocationByIdQuery(id), ct)))
            .WithName(GetLocationRoute);

        // Réglages : chacun est remplacé en entier.
        locations.MapPut("/{id:guid}/name", (
            Guid id, RenameLocationRequest request, ICommandHandler<RenameLocationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameLocationCommand(id, request.Name), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        locations.MapPut("/{id:guid}/address", (
            Guid id, AddressInput request, ICommandHandler<ChangeLocationAddressCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeLocationAddressCommand(id, request), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        locations.MapPut("/{id:guid}/contact", (
            Guid id, ContactInput request, ICommandHandler<SetLocationContactCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetLocationContactCommand(id, request), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        locations.MapPut("/{id:guid}/capacity", (
            Guid id, SetCapacityRequest request, ICommandHandler<SetLocationCapacityCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetLocationCapacityCommand(id, request.Seats), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        locations.MapPut("/{id:guid}/time-zone", (
            Guid id, ChangeTimeZoneRequest request, ICommandHandler<ChangeLocationTimeZoneCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeLocationTimeZoneCommand(id, request.TimeZoneId), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        locations.MapPut("/{id:guid}/business-hours", (
            Guid id, SetBusinessHoursRequest request, ICommandHandler<SetBusinessHoursCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetBusinessHoursCommand(id, request.WeeklyHours), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        // Cycle de vie.
        locations.MapPost("/{id:guid}/activate", (
            Guid id, ICommandHandler<ActivateLocationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ActivateLocationCommand(id), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        locations.MapPost("/{id:guid}/temporarily-close", (
            Guid id, TemporarilyCloseRequest request, ICommandHandler<TemporarilyCloseLocationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new TemporarilyCloseLocationCommand(id, request.Reason), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        locations.MapPost("/{id:guid}/reopen", (
            Guid id, ICommandHandler<ReopenLocationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ReopenLocationCommand(id), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        locations.MapPost("/{id:guid}/archive", (
            Guid id, ICommandHandler<ArchiveLocationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ArchiveLocationCommand(id), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        // Exceptions : une par date.
        locations.MapPost("/{id:guid}/exceptional-closures", (
            Guid id, ExceptionalClosureRequest request, ICommandHandler<AddExceptionalClosureCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AddExceptionalClosureCommand(id, request.Date, request.Reason), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        locations.MapPost("/{id:guid}/exceptional-openings", (
            Guid id, ExceptionalOpeningRequest request, ICommandHandler<AddExceptionalOpeningCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AddExceptionalOpeningCommand(id, request.Date, request.Ranges, request.Reason), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        locations.MapDelete("/{id:guid}/exceptions/{date}", (
            Guid id, DateOnly date, ICommandHandler<RemoveExceptionalScheduleCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveExceptionalScheduleCommand(id, date), ct)))
            .RequirePermission(LocationsPermissions.Manage);

        return locations;
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
