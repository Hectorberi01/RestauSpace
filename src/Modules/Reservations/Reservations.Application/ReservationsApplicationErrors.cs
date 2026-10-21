namespace Reservations.Application;

/// <summary>Erreurs des cas d'usage de Reservations (celles des règles métier vivent dans Reservations.Domain).</summary>
public static class ReservationsApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("reservations.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError ReservationNotFound = new("reservations.reservation.not_found", "The reservation does not exist.");
    public static readonly DomainError LocationNotFound = new("reservations.location.not_found", "The location does not exist.");
    public static readonly DomainError CustomerNotFound = new("reservations.customer.not_found", "The customer does not exist.");
    public static readonly DomainError TableSessionNotFound = new("reservations.table_session.not_found", "The table session does not exist.");

    public static readonly DomainError TenantNotWritable =
        new("reservations.tenant.not_writable", "The restaurant account is suspended or closed: its reservations can be read, not changed.");

    public static readonly DomainError LocationClosedDuringSlot =
        new("reservations.reservation.location_closed_during_slot", "The location is not open during the whole slot.");

    public static readonly DomainError TableSessionNotUsable =
        new("reservations.table_session.not_usable", "The table session is closed or belongs to another location.");

    public static readonly DomainError DurationInvalid =
        new("reservations.reservation.duration_invalid", "A duration is a whole number of minutes greater than zero.");

    public static readonly DomainError PagingInvalid =
        new("reservations.paging.invalid", "page must be at least 1 and pageSize between 1 and 100.");

    public static readonly DomainError OnlineBookingDisabled =
        new("reservations.online.disabled", "This location does not take reservations online.");

    public static readonly DomainError ContactRequired =
        new("reservations.online.contact_required", "Give a phone number so the restaurant can reach you.");

    public static readonly DomainError TooManySlots =
        new("reservations.online.too_many_slots", "Ask for 200 slots at most at a time.");

    public static readonly DomainError WaitlistEntryNotFound = new("reservations.waitlist.not_found", "The waiting list entry does not exist.");

    public static readonly DomainError WaitlistDisabled =
        new("reservations.waitlist.disabled", "This location does not take waiting list sign-ups online.");

    public static readonly DomainError SlotAvailable =
        new("reservations.waitlist.slot_available", "A table is still free at this time: book it directly.");

    public static readonly DomainError OfferNoLongerAvailable =
        new("reservations.waitlist.offer_unavailable", "The offered table is no longer available.");

    public static readonly DomainError DepositNotRequired =
        new("reservations.deposit.none", "This reservation has no deposit.");

    public static readonly DomainError WindowInvalid =
        new("reservations.window.invalid", "The listing window must start before it ends and span at most 31 days.");
}
