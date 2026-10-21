namespace Reservations.Domain;

public enum ReservationStatus
{
    Pending = 1,
    Confirmed = 2,
    Seated = 3,
    Completed = 4,
    Cancelled = 5,
    NoShow = 6,
}

public static class ReservationStatusExtensions
{
    /// <summary>
    /// Statuts dont les couverts sont décomptés de la capacité de l'établissement. La query du repository qui alimente
    /// <see cref="ReservationAvailabilityPolicy"/> doit sélectionner exactement ceux-ci.
    /// </summary>
    public static IReadOnlyCollection<ReservationStatus> CapacityHoldingStatuses { get; } =
        Array.AsReadOnly([ReservationStatus.Pending, ReservationStatus.Confirmed, ReservationStatus.Seated]);

    public static bool HoldsCapacity(this ReservationStatus status)
    {
        EnsureKnown(status);
        return status is ReservationStatus.Pending or ReservationStatus.Confirmed or ReservationStatus.Seated;
    }

    public static bool IsTerminal(this ReservationStatus status)
    {
        EnsureKnown(status);
        return status is ReservationStatus.Completed or ReservationStatus.Cancelled or ReservationStatus.NoShow;
    }

    // Une valeur inconnue répondant "false" aux deux questions serait une réservation qui ne bloque pas de places
    // sans pour autant être terminée.
    private static void EnsureKnown(ReservationStatus status) =>
        DomainException.ThrowIf(!Enum.IsDefined(status), ReservationErrors.StatusUnknown);
}
