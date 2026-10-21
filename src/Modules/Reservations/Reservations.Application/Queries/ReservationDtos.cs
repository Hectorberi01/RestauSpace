using Reservations.Domain;

namespace Reservations.Application.Queries;

/// <param name="StartAtUtc">Instant de début, en UTC (l'heure locale se lit avec le fuseau de l'établissement).</param>
/// <param name="TableSessionId">Session de table de l'installation, si elle est suivie.</param>
public sealed record ReservationDto(
    Guid Id,
    Guid LocationId,
    ReservationStatus Status,
    DateTimeOffset StartAtUtc,
    int DurationMinutes,
    int PartySize,
    string GuestName,
    string? GuestPhone,
    string? GuestEmail,
    string? Notes,
    Guid? CustomerId,
    Guid? TableSessionId,
    ReservationActor? CancelledBy,
    string? CancellationReason,
    DateTimeOffset CreatedAtUtc,
    Commands.DepositDto? Deposit = null)
{
    public static ReservationDto From(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        return new ReservationDto(
            reservation.Id.Value,
            reservation.LocationId.Value,
            reservation.Status,
            reservation.StartAtUtc,
            (int)reservation.Duration.TotalMinutes,
            reservation.PartySize,
            reservation.GuestContact.Name,
            reservation.GuestContact.Phone?.Value,
            reservation.GuestContact.Email?.Value,
            reservation.Notes,
            reservation.CustomerId?.Value,
            reservation.TableSessionId?.Value,
            reservation.CancelledBy,
            reservation.CancellationReason,
            reservation.CreatedAtUtc,
            Commands.DepositDto.From(reservation));
    }
}

/// <param name="OpenDuringSlot">L'établissement est ouvert pendant tout le créneau.</param>
/// <param name="IsAvailable">Ouvert ET assez de places : une réservation serait acceptée par la capacité.</param>
public sealed record AvailabilityDto(
    DateTimeOffset StartAtUtc,
    int DurationMinutes,
    int PartySize,
    bool OpenDuringSlot,
    bool IsAvailable,
    int Capacity,
    int PeakBookedSeats,
    int RemainingSeats);

public sealed record ReservationPolicyDto(
    int MinLeadTimeMinutes,
    int MaxAdvanceDays,
    int DefaultDurationMinutes,
    int CancellationCutoffMinutes,
    int MinDurationMinutes,
    int MaxDurationMinutes,
    int MaxPartySize,
    bool AcceptsOnlineBookings = false)
{
    public static ReservationPolicyDto From(ReservationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new ReservationPolicyDto(
            (int)policy.MinLeadTime.TotalMinutes,
            (int)policy.MaxAdvance.TotalDays,
            (int)policy.DefaultDuration.TotalMinutes,
            (int)policy.CancellationCutoff.TotalMinutes,
            (int)policy.MinDuration.TotalMinutes,
            (int)policy.MaxDuration.TotalMinutes,
            policy.MaxPartySize,
            policy.AcceptsOnlineBookings);
    }
}
