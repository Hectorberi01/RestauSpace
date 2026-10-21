namespace Reservations.Domain;

/// <summary>
/// Places déjà prises sur un créneau : par une réservation qui retient de la capacité, ou par des convives installés
/// sans réservation (walk-in, sans <see cref="ReservationId"/>).
/// </summary>
public sealed class ExistingBooking : ValueObject
{
    /// <summary>Plafond absolu de convives pour une réservation, quelle que soit la policy qui l'a acceptée.</summary>
    public const int MaxPartySize = ReservationPolicy.MaxPartySizeCeiling;

    private ExistingBooking(ReservationId? reservationId, Period slot, int partySize)
    {
        ReservationId = reservationId;
        Slot = slot;
        PartySize = partySize;
    }

    /// <summary>Null pour une occupation walk-in.</summary>
    public ReservationId? ReservationId { get; }

    public bool IsWalkIn => ReservationId is null;

    public Period Slot { get; }

    public int PartySize { get; }

    public static ExistingBooking Create(ReservationId reservationId, Period slot, int partySize)
    {
        ArgumentNullException.ThrowIfNull(slot);
        Guard.NotEmpty(reservationId);
        EnsureValidPartySize(partySize);

        return new ExistingBooking(reservationId, slot, partySize);
    }

    /// <summary>Convives installés sans réservation, pour la durée prévue d'occupation de leur table.</summary>
    public static ExistingBooking ForWalkIn(Period slot, int guests)
    {
        ArgumentNullException.ThrowIfNull(slot);
        EnsureValidPartySize(guests);

        return new ExistingBooking(null, slot, guests);
    }

    private static void EnsureValidPartySize(int partySize)
    {
        DomainException.ThrowIf(partySize <= 0, ReservationErrors.PartySizeNotPositive);
        DomainException.ThrowIf(partySize > MaxPartySize, ReservationErrors.PartySizeTooLarge);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ReservationId;
        yield return Slot;
        yield return PartySize;
    }
}
