using Delivery.Application;
using Delivery.Application.Commands;
using Delivery.Application.Queries;
using Delivery.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Delivery.Api;

/// <summary>
/// Retrait et livraison du restaurant sélectionné dans l'access token (ou de la clé d'API). Aucune route ne prend de
/// tenant : ce n'est jamais une valeur de requête. L'hôte chaîne l'autorisation (<c>Tenancy.TenantMemberPolicy</c>) sur
/// le groupe renvoyé. Lire et suivre les fiches : <see cref="DeliveryPermissions.Operate"/> ; régler les services et
/// les zones : <see cref="DeliveryPermissions.Configure"/> ; fiches livreurs : <see cref="DeliveryPermissions.ManageCouriers"/> ;
/// remises des livreurs : <see cref="DeliveryPermissions.Remit"/>.
/// </summary>
public static class DeliveryEndpoints
{
    private const string Operate = DeliveryPermissions.Operate;
    private const string Configure = DeliveryPermissions.Configure;
    private const string ManageCouriers = DeliveryPermissions.ManageCouriers;
    private const string Remit = DeliveryPermissions.Remit;
    private const string GetRemittanceRoute = "GetCourierRemittance";
    private const string GetSettingsRoute = "GetDeliverySettings";
    private const string GetFulfillmentRoute = "GetFulfillment";

    public static RouteGroupBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/delivery").WithTags("Delivery");
        MapSettings(group);
        MapFulfillments(group);
        MapCouriers(group);
        MapRemittances(group);
        return group;
    }

    private static void MapSettings(RouteGroupBuilder group)
    {
        // Sans réglages enregistrés, les valeurs par défaut (retrait actif, livraison inactive, aucune zone).
        group.MapGet("/settings/{locationId:guid}", async (
            Guid locationId, IQueryHandler<GetDeliverySettingsQuery, DeliverySettingsDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetDeliverySettingsQuery(locationId), ct)))
            .WithName(GetSettingsRoute)
            .RequirePermission(Operate);

        group.MapPut("/settings/{locationId:guid}/pickup", (
            Guid locationId, ServiceSettingsRequest request, ICommandHandler<ConfigurePickupCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ConfigurePickupCommand(locationId, request.Enabled, request.LeadMinutes), ct)))
            .RequirePermission(Configure);

        group.MapPut("/settings/{locationId:guid}/delivery", (
            Guid locationId, ServiceSettingsRequest request, ICommandHandler<ConfigureDeliveryCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ConfigureDeliveryCommand(locationId, request.Enabled, request.LeadMinutes), ct)))
            .RequirePermission(Configure);

        group.MapPost("/settings/{locationId:guid}/zones", async (
            Guid locationId, DeliveryZoneRequest request, ICommandHandler<AddDeliveryZoneCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new AddDeliveryZoneCommand(locationId, request.Name, request.Fee, request.MinimumOrder, request.EstimatedMinutes), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetSettingsRoute, new { locationId });
        }).RequirePermission(Configure);

        group.MapPut("/settings/{locationId:guid}/zones/{zoneId:guid}", (
            Guid locationId, Guid zoneId, DeliveryZoneRequest request, ICommandHandler<UpdateDeliveryZoneCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(
                new UpdateDeliveryZoneCommand(locationId, zoneId, request.Name, request.Fee, request.MinimumOrder, request.EstimatedMinutes), ct)))
            .RequirePermission(Configure);

        group.MapPost("/settings/{locationId:guid}/zones/{zoneId:guid}/activate", (
            Guid locationId, Guid zoneId, ICommandHandler<SetDeliveryZoneActiveCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetDeliveryZoneActiveCommand(locationId, zoneId, true), ct)))
            .RequirePermission(Configure);

        group.MapPost("/settings/{locationId:guid}/zones/{zoneId:guid}/deactivate", (
            Guid locationId, Guid zoneId, ICommandHandler<SetDeliveryZoneActiveCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetDeliveryZoneActiveCommand(locationId, zoneId, false), ct)))
            .RequirePermission(Configure);

        group.MapDelete("/settings/{locationId:guid}/zones/{zoneId:guid}", (
            Guid locationId, Guid zoneId, ICommandHandler<RemoveDeliveryZoneCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveDeliveryZoneCommand(locationId, zoneId), ct)))
            .RequirePermission(Configure);
    }

    private static void MapFulfillments(RouteGroupBuilder group)
    {
        group.MapPost("/fulfillments/pickup", async (
            CreatePickupRequest request, ICommandHandler<CreatePickupCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreatePickupCommand(request.OrderId, request.CustomerName, request.CustomerPhone, request.PickupAt), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetFulfillmentRoute, new { id });
        }).RequirePermission(Operate);

        group.MapPost("/fulfillments/delivery", async (
            CreateDeliveryRequest request, ICommandHandler<CreateDeliveryCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateDeliveryCommand(
                    request.OrderId, request.ZoneId, request.CustomerName, request.CustomerPhone, request.AddressLine, request.Landmark, request.Instructions),
                ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetFulfillmentRoute, new { id });
        }).RequirePermission(Operate);

        // De la promesse la plus proche à la plus lointaine ; période sur la date de création [from, to[.
        group.MapGet("/fulfillments", async (
            Guid? locationId,
            FulfillmentMode? mode,
            FulfillmentStatus? status,
            bool? openOnly,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize,
            IQueryHandler<ListFulfillmentsQuery, IReadOnlyList<FulfillmentDto>> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(
                new ListFulfillmentsQuery(locationId, mode, status, openOnly ?? false, from, to, skip, take), ct))))
            .RequirePermission(Operate);

        group.MapGet("/fulfillments/{id:guid}", async (
            Guid id, IQueryHandler<GetFulfillmentQuery, FulfillmentDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetFulfillmentQuery(id), ct)))
            .WithName(GetFulfillmentRoute)
            .RequirePermission(Operate);

        group.MapGet("/fulfillments/by-order/{orderId:guid}", async (
            Guid orderId, IQueryHandler<GetOrderFulfillmentQuery, FulfillmentDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetOrderFulfillmentQuery(orderId), ct)))
            .RequirePermission(Operate);

        MapStep(group, "ready", FulfillmentStep.ReadyForPickup);
        MapStep(group, "picked-up", FulfillmentStep.PickedUp);
        MapStep(group, "depart", FulfillmentStep.Depart);

        // Corps facultatif : seulement quand le livreur a encaissé le reste à payer.
        group.MapPost("/fulfillments/{id:guid}/delivered", (
            Guid id, DeliveredRequest? request, ICommandHandler<ConfirmDeliveryCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ConfirmDeliveryCommand(id, request?.Collected, request?.Method), ct)))
            .RequirePermission(Operate);
        MapStep(group, "retry", FulfillmentStep.Retry);

        group.MapPost("/fulfillments/{id:guid}/failed", (
            Guid id, ReasonRequest request, ICommandHandler<AdvanceFulfillmentCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AdvanceFulfillmentCommand(id, FulfillmentStep.Failed, request.Reason), ct)))
            .RequirePermission(Operate);

        group.MapPost("/fulfillments/{id:guid}/cancel", (
            Guid id, ReasonRequest? request, ICommandHandler<AdvanceFulfillmentCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AdvanceFulfillmentCommand(id, FulfillmentStep.Cancel, request?.Reason), ct)))
            .RequirePermission(Operate);

        group.MapPut("/fulfillments/{id:guid}/courier", (
            Guid id, CourierRequest request, ICommandHandler<AssignCourierCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AssignCourierCommand(id, request.Name, request.Phone, request.CourierId), ct)))
            .RequirePermission(Operate);

        group.MapPut("/fulfillments/{id:guid}/contact", (
            Guid id, ContactRequest request, ICommandHandler<UpdateFulfillmentContactCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new UpdateFulfillmentContactCommand(id, request.CustomerName, request.CustomerPhone), ct)))
            .RequirePermission(Operate);

        group.MapPut("/fulfillments/{id:guid}/address", (
            Guid id, AddressRequest request, ICommandHandler<ChangeDeliveryAddressCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeDeliveryAddressCommand(id, request.AddressLine, request.Landmark, request.Instructions), ct)))
            .RequirePermission(Operate);
    }

    private static void MapCouriers(RouteGroupBuilder group)
    {
        // Lecture ouverte à delivery.operate : il faut choisir le livreur d'une course.
        group.MapGet("/couriers", async (
            bool? includeInactive, IQueryHandler<ListCouriersQuery, IReadOnlyList<CourierDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListCouriersQuery(includeInactive ?? false), ct)))
            .RequirePermission(Operate);

        group.MapPost("/couriers", async (
            CourierProfileRequest request, ICommandHandler<CreateCourierCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(
                await handler.HandleAsync(new CreateCourierCommand(request.Name, request.Phone, request.Kind, request.FeePerDelivery), ct))))
            .RequirePermission(ManageCouriers);

        group.MapPut("/couriers/{id:guid}", (
            Guid id, CourierProfileRequest request, ICommandHandler<UpdateCourierCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new UpdateCourierCommand(id, request.Name, request.Phone, request.Kind, request.FeePerDelivery), ct)))
            .RequirePermission(ManageCouriers);

        group.MapPost("/couriers/{id:guid}/activate", (
            Guid id, ICommandHandler<SetCourierActiveCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetCourierActiveCommand(id, true), ct)))
            .RequirePermission(ManageCouriers);

        group.MapPost("/couriers/{id:guid}/deactivate", (
            Guid id, ICommandHandler<SetCourierActiveCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetCourierActiveCommand(id, false), ct)))
            .RequirePermission(ManageCouriers);

        // Ce que chaque livreur détient ou attend sur ses courses terminées pas encore remises.
        group.MapGet("/couriers/balances", async (
            Guid locationId, IQueryHandler<GetCourierBalancesQuery, IReadOnlyList<CourierBalanceDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetCourierBalancesQuery(locationId), ct)))
            .RequirePermission(Remit);
    }

    private static void MapRemittances(RouteGroupBuilder group)
    {
        group.MapPost("/remittances", async (
            RemittanceRequest request, ICommandHandler<RecordRemittanceCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new RecordRemittanceCommand(request.FulfillmentIds ?? [], request.Counted, request.Note), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetRemittanceRoute, new { id });
        }).RequirePermission(Remit);

        group.MapGet("/remittances", async (
            Guid? locationId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize,
            IQueryHandler<ListRemittancesQuery, IReadOnlyList<CourierRemittanceDto>> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(
                new ListRemittancesQuery(locationId, from, to, skip, take), ct))))
            .RequirePermission(Remit);

        group.MapGet("/remittances/{id:guid}", async (
            Guid id, IQueryHandler<GetRemittanceQuery, CourierRemittanceDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetRemittanceQuery(id), ct)))
            .WithName(GetRemittanceRoute)
            .RequirePermission(Remit);
    }

    private static void MapStep(RouteGroupBuilder group, string action, FulfillmentStep step) =>
        group.MapPost($"/fulfillments/{{id:guid}}/{action}", (
            Guid id, ICommandHandler<AdvanceFulfillmentCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AdvanceFulfillmentCommand(id, step), ct)))
            .RequirePermission(Operate);

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
