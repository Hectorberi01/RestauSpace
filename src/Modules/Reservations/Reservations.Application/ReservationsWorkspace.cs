using Reservations.Application.Abstractions;
using Reservations.Domain;

namespace Reservations.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte de la requête (jamais de la requête), une écriture
/// exige un tenant qui les accepte, chaque réservation est chargée dans ce tenant seulement (404 sinon), et toute
/// décision de capacité est prise sous le verrou de l'établissement, dans la transaction qui écrit ensuite.
/// Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class ReservationsWorkspace(
    ITenantContext tenantContext,
    IReservationTenants tenants,
    IReservationLocations locations,
    IReservationRepository reservations,
    IWaitlistRepository waitlist,
    IReservationPolicyStore policies,
    IReservationCapacityLock capacityLock,
    IReservationFloor floor,
    IReservationsUnitOfWork unitOfWork,
    IClock clock)
{
    public IReservationRepository Reservations => reservations;

    public IReservationPolicyStore Policies => policies;

    public IWaitlistRepository Waitlist => waitlist;

    public IClock Clock => clock;

    public async Task<WaitlistEntry> LoadWaitlistEntryAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await waitlist.FindAsync(tenantId, WaitlistEntryId.From(id), cancellationToken))
        ?? throw new NotFoundException(ReservationsApplicationErrors.WaitlistEntryNotFound);

    /// <summary>Devise du restaurant, pour l'acompte.</summary>
    public Task<Currency> CurrencyAsync(TenantId tenantId, CancellationToken cancellationToken) => tenants.CurrencyAsync(tenantId, cancellationToken);

    public IReservationFloor Floor => floor;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    /// <summary>Refuse l'écriture pour un tenant suspendu, fermé ou inconnu.</summary>
    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(ReservationsApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<Reservation> LoadReservationAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await reservations.FindAsync(tenantId, ReservationId.From(id), cancellationToken))
        ?? throw new NotFoundException(ReservationsApplicationErrors.ReservationNotFound);

    public async Task<LocationReference> RequireLocationAsync(TenantId tenantId, Guid locationId, CancellationToken cancellationToken) =>
        (locationId == Guid.Empty ? null : await locations.FindAsync(tenantId, LocationId.From(locationId), cancellationToken))
        ?? throw new NotFoundException(ReservationsApplicationErrors.LocationNotFound);

    /// <summary>Refuse un créneau qui déborde des horaires d'ouverture (exceptions comprises) de l'établissement.</summary>
    public async Task EnsureOpenDuringAsync(TenantId tenantId, LocationId locationId, Period slot, CancellationToken cancellationToken)
    {
        if (!await locations.IsOpenDuringAsync(tenantId, locationId, slot, cancellationToken))
        {
            throw new DomainException(ReservationsApplicationErrors.LocationClosedDuringSlot);
        }
    }

    /// <summary>
    /// Créneau d'une demande : durée par défaut de la policy quand <paramref name="durationMinutes"/> est absent. Les
    /// bornes de durée restent celles du domaine ; ici, seulement ce qui empêcherait de construire le créneau.
    /// </summary>
    public static Period SlotFor(ReservationPolicy policy, DateTimeOffset startAt, int? durationMinutes)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (durationMinutes is not { } minutes)
        {
            return policy.SlotFor(startAt);
        }

        DomainException.ThrowIf(minutes <= 0, ReservationsApplicationErrors.DurationInvalid);
        DomainException.ThrowIf(minutes > ReservationPolicy.MaxDurationCeiling.TotalMinutes, ReservationErrors.DurationTooLong);
        var duration = TimeSpan.FromMinutes(minutes);
        var startUtc = startAt.ToUniversalTime();
        DomainException.ThrowIf(startUtc > DateTimeOffset.MaxValue - duration, ReservationErrors.BeyondBookingHorizon);
        return Period.Create(startUtc, startUtc + duration);
    }

    /// <summary>
    /// Décision de disponibilité pour une demande, à consommer tout de suite. <paramref name="forWrite"/> : prend d'abord
    /// le verrou de capacité de l'établissement (la décision précède une écriture) ; sinon simple lecture.
    /// </summary>
    public async Task<AvailabilityDecision> EvaluateAsync(
        TenantId tenantId,
        LocationId locationId,
        Period slot,
        int partySize,
        ReservationPolicy policy,
        ReservationId? excludedReservationId,
        bool forWrite,
        CancellationToken cancellationToken,
        WaitlistEntryId? excludedOffer = null)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(policy);
        if (forWrite)
        {
            await capacityLock.AcquireAsync(tenantId, locationId, cancellationToken);
        }

        // Une réservation qui commence jusqu'à la plus longue durée possible avant le créneau peut encore le chevaucher.
        var widened = Period.Create(SafeSubtract(slot.StartUtc, ReservationPolicy.MaxDurationCeiling), slot.EndUtc);
        var holding = await reservations.ListHoldingCapacityAsync(tenantId, locationId, widened, cancellationToken);
        var occupancy = await floor.GetOccupancyAsync(tenantId, locationId, cancellationToken);

        var bookings = holding.Where(reservation => reservation.HoldsCapacity).Select(reservation => reservation.ToExistingBooking()).ToList();
        bookings.AddRange(WalkIns(occupancy, holding, policy, clock.UtcNow));
        bookings.AddRange(await OfferHoldsAsync(tenantId, locationId, policy, excludedOffer, cancellationToken));

        var capacity = Math.Clamp(occupancy.SeatsInService, 0, ReservationAvailabilityPolicy.MaxCapacity);
        return ReservationAvailabilityPolicy.Evaluate(locationId, slot, partySize, capacity, bookings, excludedReservationId);
    }

    /// <summary>
    /// Décisions de lecture pour plusieurs créneaux d'un même établissement, avec un seul chargement des réservations et
    /// de la salle (proposition de créneaux au public ; la réservation revérifie sous verrou).
    /// </summary>
    public async Task<IReadOnlyList<AvailabilityDecision>> EvaluateManyAsync(
        TenantId tenantId, LocationId locationId, IReadOnlyList<Period> slots, int partySize, ReservationPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(policy);
        if (slots.Count == 0)
        {
            return [];
        }

        var widened = Period.Create(SafeSubtract(slots.Min(slot => slot.StartUtc), ReservationPolicy.MaxDurationCeiling), slots.Max(slot => slot.EndUtc));
        var holding = await reservations.ListHoldingCapacityAsync(tenantId, locationId, widened, cancellationToken);
        var occupancy = await floor.GetOccupancyAsync(tenantId, locationId, cancellationToken);
        var bookings = holding.Where(reservation => reservation.HoldsCapacity).Select(reservation => reservation.ToExistingBooking()).ToList();
        bookings.AddRange(WalkIns(occupancy, holding, policy, clock.UtcNow));
        bookings.AddRange(await OfferHoldsAsync(tenantId, locationId, policy, null, cancellationToken));
        var capacity = Math.Clamp(occupancy.SeatsInService, 0, ReservationAvailabilityPolicy.MaxCapacity);
        return slots.Select(slot => ReservationAvailabilityPolicy.Evaluate(locationId, slot, partySize, capacity, bookings, null)).ToList();
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Décision jamais consommée, pour les appels sans changement (même créneau, même taille) : le domaine y applique ses
    /// gardes d'état et ne regarde pas la décision.
    /// </summary>
    public static AvailabilityDecision UnusedDecision(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        return ReservationAvailabilityPolicy.Evaluate(
            reservation.LocationId, reservation.Slot, reservation.PartySize, 0, [], reservation.Id);
    }

    /// <summary>
    /// Clients installés sans réservation : ils occupent leurs places de l'ouverture de leur session jusqu'à la durée par
    /// défaut de la policy, ou jusqu'à maintenant s'ils sont restés plus longtemps. Une session ouverte pour une
    /// réservation, ou rattachée à une réservation installée, compte déjà par la réservation.
    /// </summary>
    private static IEnumerable<ExistingBooking> WalkIns(
        FloorOccupancy occupancy, IReadOnlyList<Reservation> holding, ReservationPolicy policy, DateTimeOffset now)
    {
        var seatedSessions = holding
            .Where(reservation => reservation.TableSessionId is not null)
            .Select(reservation => reservation.TableSessionId!.Value)
            .ToHashSet();

        foreach (var session in occupancy.OpenSessions)
        {
            if (!session.IsOpen || session.ForReservation || seatedSessions.Contains(session.Id) || session.GuestCount <= 0)
            {
                continue;
            }

            var expectedEnd = session.OpenedAtUtc + policy.DefaultDuration;
            var end = expectedEnd > now ? expectedEnd : now;
            if (end <= session.OpenedAtUtc)
            {
                continue;
            }

            yield return ExistingBooking.ForWalkIn(
                Period.Create(session.OpenedAtUtc, end), Math.Min(session.GuestCount, ExistingBooking.MaxPartySize));
        }
    }

    /// <summary>Une place proposée à la liste d'attente reste retenue jusqu'à l'expiration de l'offre.</summary>
    private async Task<IEnumerable<ExistingBooking>> OfferHoldsAsync(
        TenantId tenantId, LocationId locationId, ReservationPolicy policy, WaitlistEntryId? excluded, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var open = await waitlist.ListOpenAsync(tenantId, locationId, cancellationToken);
        return open
            .Where(entry => entry.Id != excluded)
            .Select(entry => entry.OfferHold(policy.DefaultDuration, now))
            .OfType<ExistingBooking>()
            .ToList();
    }

    private static DateTimeOffset SafeSubtract(DateTimeOffset instant, TimeSpan duration) =>
        instant - DateTimeOffset.MinValue < duration ? DateTimeOffset.MinValue : instant - duration;
}
