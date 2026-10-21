namespace Reservations.Domain;

/// <summary>
/// Résultat de <see cref="ReservationAvailabilityPolicy.Evaluate"/> pour une demande précise (établissement, créneau,
/// nombre de couverts). Seule la policy peut la produire, une <see cref="Reservation"/> refuse une décision prise pour
/// une autre demande, et c'est un jeton À USAGE UNIQUE : la réservation qu'elle autorise la consomme, donc la capacité
/// évaluée une fois ne peut jamais être dépensée deux fois. D'où son identité et son état plutôt qu'un value object.
/// L'usage unique ne la rend pas fraîche : une décision ne dit rien des réservations écrites après son évaluation.
/// Évaluer et consommer dans une même transaction qui tient le verrou de capacité reste le travail de l'Application.
/// </summary>
public sealed class AvailabilityDecision
{
    internal AvailabilityDecision(
        LocationId locationId,
        Period slot,
        int partySize,
        int capacity,
        int peakBookedSeats,
        ReservationId? excludedReservationId)
    {
        LocationId = locationId;
        Slot = slot;
        PartySize = partySize;
        Capacity = capacity;
        PeakBookedSeats = peakBookedSeats;
        ExcludedReservationId = excludedReservationId;
    }

    public LocationId LocationId { get; }

    public Period Slot { get; }

    public int PartySize { get; }

    public int Capacity { get; }

    /// <summary>
    /// Nombre maximal de places occupées simultanément par d'autres (réservations et clients sans réservation) à un
    /// instant quelconque du créneau ; plafonne à <see cref="int.MaxValue"/>.
    /// </summary>
    public int PeakBookedSeats { get; }

    /// <summary>Réservation dont la place a été exclue du décompte (report / changement de nombre de couverts).</summary>
    public ReservationId? ExcludedReservationId { get; }

    public int RemainingSeats => Math.Max(0, Capacity - PeakBookedSeats);

    // Comparé par une soustraction sur des valeurs positives : "peak + party" pourrait déborder en arithmétique int.
    public bool IsAvailable => PartySize <= (long)Capacity - PeakBookedSeats;

    /// <summary>Vrai dès qu'une réservation a été créée ou modifiée avec cette décision.</summary>
    public bool IsConsumed { get; private set; }

    /// <summary>
    /// Dépense la décision pour exactement la demande pour laquelle elle a été prise. Un usage refusé (déjà utilisée,
    /// autre demande, capacité insuffisante) lève une exception et laisse la décision intacte.
    /// </summary>
    internal void Consume(LocationId locationId, Period slot, int partySize, ReservationId? reservationId)
    {
        DomainException.ThrowIf(IsConsumed, ReservationErrors.AvailabilityDecisionAlreadyUsed);

        // Ignorer les places d'une autre réservation sous-estimerait l'occupation ; n'en ignorer aucune est juste plus strict.
        var excludesAnotherReservation = ExcludedReservationId is not null && ExcludedReservationId != reservationId;
        DomainException.ThrowIf(
            LocationId != locationId || Slot != slot || PartySize != partySize || excludesAnotherReservation,
            ReservationErrors.AvailabilityDecisionMismatch);
        DomainException.ThrowIf(!IsAvailable, ReservationErrors.CapacityInsufficient);

        IsConsumed = true;
    }
}
