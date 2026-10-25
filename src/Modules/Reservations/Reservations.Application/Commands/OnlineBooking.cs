using Reservations.Application.Abstractions;
using Reservations.Domain;

namespace Reservations.Application.Commands;

// Réservation par le client lui-même, depuis le site du restaurant (appelée par l'hôte pour Storefront) : seulement
// dans un établissement qui l'accepte, avec les mêmes règles (fenêtre, taille de groupe, horaires, capacité sous
// verrou) que pour le personnel. Une réservation prise en ligne est confirmée tout de suite, ou dès le paiement de
// l'acompte si l'établissement en demande un : le client reçoit sa confirmation par l'événement ReservationConfirmed. Il
// l'annule lui-même jusqu'au délai de la politique (acompte remboursé).

/// <summary>Ce que le public voit des règles d'un établissement.</summary>
public sealed record OnlineBookingRulesDto(
    bool AcceptsOnlineBookings, int MinLeadTimeMinutes, int MaxAdvanceDays, int DurationMinutes, int CancellationCutoffMinutes, int MaxPartySize);

public sealed record GetOnlineBookingRulesQuery(Guid LocationId) : IQuery<OnlineBookingRulesDto>;

public sealed class GetOnlineBookingRulesHandler(ReservationsWorkspace workspace) : IQueryHandler<GetOnlineBookingRulesQuery, OnlineBookingRulesDto>
{
    public async Task<OnlineBookingRulesDto> HandleAsync(GetOnlineBookingRulesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, query.LocationId, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, location.LocationId, cancellationToken);
        return new OnlineBookingRulesDto(
            policy.AcceptsOnlineBookings && location.IsOperational,
            (int)policy.MinLeadTime.TotalMinutes,
            (int)policy.MaxAdvance.TotalDays,
            (int)policy.DefaultDuration.TotalMinutes,
            (int)policy.CancellationCutoff.TotalMinutes,
            policy.MaxPartySize);
    }
}

public sealed record SlotAvailabilityDto(DateTimeOffset StartAtUtc, bool IsAvailable);

/// <summary>
/// Disponibilité de créneaux candidats (durée par défaut) pour un groupe : hors fenêtre de réservation ou sans place,
/// un créneau est indisponible. Les horaires d'ouverture sont ceux de l'appelant ; la réservation les revérifie.
/// </summary>
public sealed record CheckOnlineSlotsQuery(Guid LocationId, int PartySize, IReadOnlyList<DateTimeOffset>? Starts) : IQuery<IReadOnlyList<SlotAvailabilityDto>>;

public sealed class CheckOnlineSlotsHandler(ReservationsWorkspace workspace, IClock clock) : IQueryHandler<CheckOnlineSlotsQuery, IReadOnlyList<SlotAvailabilityDto>>
{
    public const int MaxSlots = 200;

    public async Task<IReadOnlyList<SlotAvailabilityDto>> HandleAsync(CheckOnlineSlotsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var starts = (query.Starts ?? []).Select(start => start.ToUniversalTime()).Distinct().Order().ToList();
        DomainException.ThrowIf(starts.Count > MaxSlots, ReservationsApplicationErrors.TooManySlots);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, query.LocationId, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, location.LocationId, cancellationToken);
        DomainException.ThrowIf(!policy.AcceptsOnlineBookings || !location.IsOperational, ReservationsApplicationErrors.OnlineBookingDisabled);
        if (query.PartySize < 1 || query.PartySize > policy.MaxPartySize)
        {
            return starts.Select(start => new SlotAvailabilityDto(start, false)).ToList();
        }

        var bookable = starts.Where(start => policy.CanBookAt(start, clock)).ToList();
        var decisions = await workspace.EvaluateManyAsync(
            tenant, location.LocationId, bookable.Select(policy.SlotFor).ToList(), query.PartySize, policy, cancellationToken);
        var available = bookable.Zip(decisions).Where(pair => pair.Second.IsAvailable).Select(pair => pair.First).ToHashSet();
        return starts.Select(start => new SlotAvailabilityDto(start, available.Contains(start))).ToList();
    }
}

/// <summary>Réservation du client sur le site : un téléphone est exigé (confirmation par SMS ou WhatsApp). Renvoie son id.</summary>
public sealed record BookOnlineCommand(
    Guid LocationId,
    DateTimeOffset StartAt,
    int PartySize,
    string? GuestName,
    string? GuestPhone,
    string? GuestEmail = null,
    string? Notes = null,
    Guid? CustomerId = null) : ICommand<Guid>
{
    public override string ToString() => $"BookOnlineCommand ({LocationId}, {StartAt:O}, {PartySize}, ***)";
}

public sealed class BookOnlineHandler(ReservationsWorkspace workspace, IReservationCustomers customers, IClock clock)
    : ICommandHandler<BookOnlineCommand, Guid>
{
    public async Task<Guid> HandleAsync(BookOnlineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireBookableLocationAsync(tenant, command.LocationId, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, location.LocationId, cancellationToken);
        DomainException.ThrowIf(!policy.AcceptsOnlineBookings, ReservationsApplicationErrors.OnlineBookingDisabled);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(command.GuestPhone), ReservationsApplicationErrors.ContactRequired);

        var slot = policy.SlotFor(command.StartAt);
        var contact = GuestContact.Create(
            command.GuestName,
            PhoneNumber.Create(command.GuestPhone),
            string.IsNullOrWhiteSpace(command.GuestEmail) ? null : Email.Create(command.GuestEmail));
        var customer = command.CustomerId is { } customerId && customerId != Guid.Empty
            ? await customers.FindAsync(tenant, CustomerId.From(customerId), cancellationToken) is { IsAnonymized: false } found ? found : null
            : null;

        await workspace.EnsureOpenDuringAsync(tenant, location.LocationId, slot, cancellationToken);
        var availability = await workspace.EvaluateAsync(
            tenant, location.LocationId, slot, command.PartySize, policy, excludedReservationId: null, forWrite: true, cancellationToken);
        var reservation = Reservation.Create(tenant, location, customer, contact, command.PartySize, slot, command.Notes, policy, availability, clock);
        if (!await Deposits.RequireAsync(workspace, reservation, cancellationToken))
        {
            reservation.Confirm(clock);
        }

        workspace.Reservations.Add(reservation);
        await workspace.SaveAsync(cancellationToken);
        return reservation.Id.Value;
    }
}

/// <summary>Ce que le client voit de sa réservation (lien signé) : jamais les notes de l'équipe.</summary>
public sealed record GuestReservationDto(
    Guid Id,
    Guid LocationId,
    ReservationStatus Status,
    DateTimeOffset StartAtUtc,
    int DurationMinutes,
    int PartySize,
    string GuestName,
    bool CanCancel,
    DateTimeOffset? CancelBeforeUtc,
    DepositDto? Deposit = null);

public sealed record GetGuestReservationQuery(Guid ReservationId) : IQuery<GuestReservationDto>;

public sealed class GetGuestReservationHandler(ReservationsWorkspace workspace, IClock clock) : IQueryHandler<GetGuestReservationQuery, GuestReservationDto>
{
    public async Task<GuestReservationDto> HandleAsync(GetGuestReservationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var reservation = await workspace.LoadReservationAsync(tenant, query.ReservationId, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, reservation.LocationId, cancellationToken);
        var open = reservation.Status is ReservationStatus.Pending or ReservationStatus.Confirmed;
        return new GuestReservationDto(
            reservation.Id.Value,
            reservation.LocationId.Value,
            reservation.Status,
            reservation.StartAtUtc,
            (int)reservation.Duration.TotalMinutes,
            reservation.PartySize,
            reservation.GuestContact.Name,
            open && policy.CanCancelAt(reservation.StartAtUtc, clock),
            open ? reservation.StartAtUtc - policy.CancellationCutoff : null,
            DepositDto.From(reservation));
    }
}

/// <summary>Annulation par le client (lien signé) : refusée après le délai de la politique.</summary>
public sealed record CancelGuestReservationCommand(Guid ReservationId) : ICommand;

public sealed class CancelGuestReservationHandler(ReservationsWorkspace workspace, IClock clock) : ICommandHandler<CancelGuestReservationCommand>
{
    public async Task HandleAsync(CancelGuestReservationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var reservation = await workspace.LoadReservationAsync(tenant, command.ReservationId, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, reservation.LocationId, cancellationToken);
        reservation.Cancel(ReservationActor.Customer, "Annulée par le client en ligne", policy, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}
