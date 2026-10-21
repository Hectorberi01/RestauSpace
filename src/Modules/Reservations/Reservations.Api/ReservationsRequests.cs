using Reservations.Domain;

namespace Reservations.Api;

public sealed record CreatedResponse(Guid Id);

/// <summary>
/// <paramref name="StartAt"/> : instant ISO 8601 avec décalage (« 2026-11-20T20:00:00+01:00 ») ; durée absente = durée
/// par défaut de l'établissement. Au moins un téléphone (E.164) ou un email.
/// </summary>
public sealed record CreateReservationRequest(
    Guid LocationId,
    DateTimeOffset StartAt,
    int PartySize,
    string? GuestName,
    string? GuestPhone = null,
    string? GuestEmail = null,
    string? Notes = null,
    int? DurationMinutes = null,
    Guid? CustomerId = null,
    bool WithoutDeposit = false);

public sealed record RescheduleRequest(DateTimeOffset StartAt, int? DurationMinutes = null);

public sealed record PartySizeRequest(int PartySize);

public sealed record SeatRequest(Guid? TableSessionId = null);

/// <param name="KeepDeposit">Annulation tardive : le restaurant garde l'acompte payé (sinon il est remboursé).</param>
public sealed record CancelRequest(string? Reason = null, bool KeepDeposit = false);

public sealed record DepositRuleRequest(
    DepositMode Mode, DepositBasis Basis = DepositBasis.PerGuest, decimal? Amount = null, int MinPartySize = 1, int PaymentWindowMinutes = 30);

public sealed record WaitlistSettingsRequest(bool AcceptsOnline, int OfferWindowMinutes = 15);

/// <param name="DesiredStart">Instant ISO 8601 avec décalage.</param>
public sealed record JoinWaitlistRequest(
    Guid LocationId, DateTimeOffset DesiredStart, int PartySize, int FlexibilityMinutes, string? GuestName, string? GuestPhone,
    string? GuestEmail = null, string? Notes = null, Guid? CustomerId = null);

public sealed record BookWaitlistRequest(DateTimeOffset StartAt);

public sealed record ReservationPolicyRequest(
    int MinLeadTimeMinutes,
    int MaxAdvanceDays,
    int DefaultDurationMinutes,
    int CancellationCutoffMinutes,
    int MinDurationMinutes,
    int MaxDurationMinutes,
    int MaxPartySize,
    bool AcceptsOnlineBookings = false);

/// <param name="CustomerId">La fiche client à rattacher ; null pour détacher la réservation.</param>
public sealed record ReservationCustomerRequest(Guid? CustomerId);
