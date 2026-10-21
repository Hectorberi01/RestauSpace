namespace Reservations.Domain;

/// <summary>
/// Service de domaine pur qui décide si un créneau peut accueillir un groupe sans dépasser la capacité en places.
/// La couche Application fournit la capacité (places des tables en service) et tout ce qui l'occupe sur le créneau
/// (réservations retenant de la capacité ET groupes walk-in), lus dans la même transaction/le même verrou que
/// l'écriture qui suit.
/// </summary>
public static class ReservationAvailabilityPolicy
{
    /// <summary>Plafond absolu de places d'un établissement ; au-delà, c'est un bug de l'appelant, pas un restaurant.</summary>
    public const int MaxCapacity = 100_000;

    /// <param name="capacity">Nombre de places des tables en service dans l'établissement (module Floor).</param>
    /// <param name="existingBookings">Réservations retenant de la capacité et walk-ins de l'établissement ; ceux hors du créneau sont ignorés.</param>
    /// <param name="excludedReservationId">La réservation déplacée ou redimensionnée, dont les places actuelles ne comptent pas contre elle-même.</param>
    public static AvailabilityDecision Evaluate(
        LocationId locationId,
        Period slot,
        int partySize,
        int capacity,
        IEnumerable<ExistingBooking> existingBookings,
        ReservationId? excludedReservationId = null)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(existingBookings);
        Guard.NotEmpty(locationId);
        if (excludedReservationId is { } excluded)
        {
            Guard.NotEmpty(excluded);
        }

        // Pas de borne haute sur le groupe ici : une demande surdimensionnée reçoit simplement « non disponible »
        // (Reservation impose ReservationPolicy.MaxPartySize avec sa propre erreur).
        DomainException.ThrowIf(partySize <= 0, ReservationErrors.PartySizeNotPositive);
        DomainException.ThrowIf(capacity < 0, ReservationErrors.CapacityNegative);
        DomainException.ThrowIf(capacity > MaxCapacity, ReservationErrors.CapacityTooLarge);

        var peak = PeakBookedSeats(slot, existingBookings, excludedReservationId);
        return new AvailabilityDecision(locationId, slot, partySize, capacity, peak, excludedReservationId);
    }

    // Balayage du créneau : des réservations bout à bout ne s'additionnent jamais, seules les simultanées.
    private static int PeakBookedSeats(Period slot, IEnumerable<ExistingBooking> bookings, ReservationId? excluded)
    {
        var changes = new List<(DateTimeOffset At, long Seats)>();
        foreach (var booking in bookings)
        {
            ArgumentNullException.ThrowIfNull(booking, nameof(bookings));

            // Un walk-in n'a pas d'id de réservation : il ne doit jamais correspondre à une exclusion absente.
            var isExcluded = excluded is not null && booking.ReservationId == excluded;
            if (isExcluded || !booking.Slot.Overlaps(slot))
            {
                continue;
            }

            changes.Add((booking.Slot.StartUtc, booking.PartySize));
            changes.Add((booking.Slot.EndUtc, -(long)booking.PartySize));
        }

        // Les créneaux sont semi-ouverts : au même instant, les places sont libérées avant d'être prises.
        changes.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Seats.CompareTo(b.Seats));

        // Cumul en long : en int, de nombreuses grosses réservations déborderaient et deviendraient invisibles.
        long current = 0;
        long peak = 0;
        foreach (var (_, seats) in changes)
        {
            current += seats;
            peak = Math.Max(peak, current);
        }

        // La saturation est assez exacte : la capacité ne dépasse jamais MaxCapacity, très en dessous de int.MaxValue.
        return (int)Math.Min(peak, int.MaxValue);
    }
}
