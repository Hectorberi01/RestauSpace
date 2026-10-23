using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reservations.Application;
using Reservations.Application.Commands;
using Reservations.Application.Queries;
using Reservations.Domain;
using SaasRestaurant.BuildingBlocks.Api;

namespace Reservations.Api;

/// <summary>
/// Réservations du restaurant sélectionné dans l'access token, gérées par son personnel. Aucune route ne prend de
/// tenant : ce n'est jamais une valeur de requête. L'hôte chaîne l'autorisation (<c>Tenancy.TenantMemberPolicy</c>) sur
/// le groupe renvoyé ; les réservations (y compris les lectures qui exposent les coordonnées des clients) exigent
/// <see cref="ReservationsPermissions.Manage"/>, la politique d'un établissement <see cref="ReservationsPermissions.Configure"/>.
/// </summary>
public static class ReservationsEndpoints
{
    private const string Manage = ReservationsPermissions.Manage;
    private const string GetReservationRoute = "GetReservation";

    // Permission du module Customers (référencée par son code : pas de dépendance entre modules). L'historique d'une
    // personne est une donnée personnelle : il exige le droit de consulter les fiches clients.
    private const string ViewCustomers = "customers.view";

    public static RouteGroupBuilder MapReservationsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/reservations").WithTags("Reservations")
            .AllowApiKeyRead(ReservationsPermissions.Manage, ReservationsPermissions.Configure);

        group.MapPost("/", async (
            CreateReservationRequest request, ICommandHandler<CreateReservationCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateReservationCommand(
                    request.LocationId,
                    request.StartAt,
                    request.PartySize,
                    request.GuestName,
                    request.GuestPhone,
                    request.GuestEmail,
                    request.Notes,
                    request.DurationMinutes,
                    request.CustomerId,
                    request.WithoutDeposit),
                ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetReservationRoute, new { id });
        }).RequirePermission(Manage);

        // Agenda : réservations qui commencent dans [from, to[ (31 jours au plus). Coordonnées des clients : Manage exigé.
        group.MapGet("/", async (
            DateTimeOffset from,
            DateTimeOffset to,
            Guid? locationId,
            ReservationStatus? status,
            IQueryHandler<ListReservationsQuery, IReadOnlyList<ReservationDto>> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListReservationsQuery(from, to, locationId, status), ct)))
            .RequirePermission(Manage);

        group.MapGet("/by-customer/{customerId:guid}", async (
            Guid customerId,
            int? page,
            int? pageSize,
            IQueryHandler<ListCustomerReservationsQuery, PagedResult<ReservationDto>> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new ListCustomerReservationsQuery(customerId, page ?? 1, pageSize ?? ListCustomerReservationsQuery.DefaultPageSize), ct)))
            .RequirePermission(ViewCustomers);

        group.MapGet("/availability", async (
            Guid locationId,
            DateTimeOffset startAt,
            int partySize,
            int? durationMinutes,
            IQueryHandler<GetAvailabilityQuery, AvailabilityDto> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetAvailabilityQuery(locationId, startAt, partySize, durationMinutes), ct)));

        group.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetReservationByIdQuery, ReservationDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetReservationByIdQuery(id), ct)))
            .WithName(GetReservationRoute)
            .RequirePermission(Manage);

        group.MapPost("/{id:guid}/confirm", (
            Guid id, ICommandHandler<ConfirmReservationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ConfirmReservationCommand(id), ct)))
            .RequirePermission(Manage);

        group.MapPut("/{id:guid}/customer", (
            Guid id, ReservationCustomerRequest request, ICommandHandler<ChangeReservationCustomerCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeReservationCustomerCommand(id, request.CustomerId), ct)))
            .RequirePermission(Manage);

        group.MapPut("/{id:guid}/slot", (
            Guid id, RescheduleRequest request, ICommandHandler<RescheduleReservationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RescheduleReservationCommand(id, request.StartAt, request.DurationMinutes), ct)))
            .RequirePermission(Manage);

        group.MapPut("/{id:guid}/party-size", (
            Guid id, PartySizeRequest request, ICommandHandler<ChangePartySizeCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangePartySizeCommand(id, request.PartySize), ct)))
            .RequirePermission(Manage);

        // Corps facultatif : { tableSessionId } quand la table est suivie dans Floor.
        group.MapPost("/{id:guid}/seat", (
            Guid id, SeatRequest? request, ICommandHandler<SeatReservationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SeatReservationCommand(id, request?.TableSessionId), ct)))
            .RequirePermission(Manage);

        group.MapPost("/{id:guid}/complete", (
            Guid id, ICommandHandler<CompleteReservationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CompleteReservationCommand(id), ct)))
            .RequirePermission(Manage);

        group.MapPost("/{id:guid}/cancel", (
            Guid id, CancelRequest? request, ICommandHandler<CancelReservationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CancelReservationCommand(id, request?.Reason, request?.KeepDeposit ?? false), ct)))
            .RequirePermission(Manage);

        // Acompte : l'équipe y renonce, ou rembourse un acompte gardé. L'encaissement passe par Payments.
        group.MapPost("/{id:guid}/deposit/waive", (
            Guid id, ICommandHandler<WaiveDepositCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new WaiveDepositCommand(id), ct)))
            .RequirePermission(Manage);

        group.MapPost("/{id:guid}/deposit/refund", (
            Guid id, ICommandHandler<RefundDepositCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RefundDepositCommand(id), ct)))
            .RequirePermission(Manage);

        group.MapGet("/deposit-rules/{locationId:guid}", async (
            Guid locationId, IQueryHandler<GetDepositRuleQuery, DepositRuleDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetDepositRuleQuery(locationId), ct)));

        group.MapPut("/deposit-rules/{locationId:guid}", (
            Guid locationId, DepositRuleRequest request, ICommandHandler<SetDepositRuleCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(
                new SetDepositRuleCommand(locationId, request.Mode, request.Basis, request.Amount, request.MinPartySize, request.PaymentWindowMinutes), ct)))
            .RequirePermission(ReservationsPermissions.Configure);

        // Liste d'attente des créneaux complets.
        group.MapGet("/waitlist-settings/{locationId:guid}", async (
            Guid locationId, IQueryHandler<GetWaitlistSettingsQuery, WaitlistSettingsDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetWaitlistSettingsQuery(locationId), ct)));

        group.MapPut("/waitlist-settings/{locationId:guid}", (
            Guid locationId, WaitlistSettingsRequest request, ICommandHandler<SetWaitlistSettingsCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetWaitlistSettingsCommand(locationId, request.AcceptsOnline, request.OfferWindowMinutes), ct)))
            .RequirePermission(ReservationsPermissions.Configure);

        group.MapGet("/waitlist", async (
            DateTimeOffset from, DateTimeOffset to, Guid? locationId,
            IQueryHandler<ListWaitlistQuery, IReadOnlyList<WaitlistEntryDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListWaitlistQuery(from, to, locationId), ct)))
            .RequirePermission(Manage);

        group.MapPost("/waitlist", async (
            JoinWaitlistRequest request, ICommandHandler<JoinWaitlistCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(
                new JoinWaitlistCommand(
                    request.LocationId, request.DesiredStart, request.PartySize, request.FlexibilityMinutes, request.GuestName, request.GuestPhone,
                    request.GuestEmail, request.Notes, request.CustomerId),
                ct))))
            .RequirePermission(Manage);

        group.MapPost("/waitlist/{id:guid}/cancel", (
            Guid id, ICommandHandler<CancelWaitlistEntryCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CancelWaitlistEntryCommand(id), ct)))
            .RequirePermission(Manage);

        group.MapPost("/waitlist/{id:guid}/book", async (
            Guid id, BookWaitlistRequest request, ICommandHandler<BookWaitlistEntryCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(new BookWaitlistEntryCommand(id, request.StartAt), ct))))
            .RequirePermission(Manage);

        // Propose tout de suite les places libres aux inscriptions en attente (sinon fait à chaque libération de places).
        group.MapPost("/waitlist/offers", async (
            Guid locationId, ICommandHandler<OfferFreedCapacityCommand, int> handler, CancellationToken ct) =>
            TypedResults.Ok(new { offered = await handler.HandleAsync(new OfferFreedCapacityCommand(locationId), ct) }))
            .RequirePermission(Manage);

        group.MapPost("/{id:guid}/no-show", (
            Guid id, ICommandHandler<MarkReservationNoShowCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MarkReservationNoShowCommand(id), ct)))
            .RequirePermission(Manage);

        // Politique de réservation par établissement.
        group.MapGet("/policies/{locationId:guid}", async (
            Guid locationId, IQueryHandler<GetReservationPolicyQuery, ReservationPolicyDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetReservationPolicyQuery(locationId), ct)));

        group.MapPut("/policies/{locationId:guid}", (
            Guid locationId, ReservationPolicyRequest request, ICommandHandler<SetReservationPolicyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(
                new SetReservationPolicyCommand(
                    locationId,
                    request.MinLeadTimeMinutes,
                    request.MaxAdvanceDays,
                    request.DefaultDurationMinutes,
                    request.CancellationCutoffMinutes,
                    request.MinDurationMinutes,
                    request.MaxDurationMinutes,
                    request.MaxPartySize,
                    request.AcceptsOnlineBookings),
                ct)))
            .RequirePermission(ReservationsPermissions.Configure);

        return group;
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
