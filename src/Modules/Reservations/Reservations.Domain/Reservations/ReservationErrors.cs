namespace Reservations.Domain;

public static class ReservationErrors
{
    public static readonly DomainError PartySizeNotPositive = new("reservations.reservation.party_size_not_positive", "Party size must be greater than zero.");
    public static readonly DomainError PartySizeTooLarge = new("reservations.reservation.party_size_too_large", "Party size exceeds the largest party that can be booked.");
    public static readonly DomainError NotesTooLong = new("reservations.reservation.notes_too_long", "Reservation notes are too long.");
    public static readonly DomainError LeadTimeNotRespected = new("reservations.reservation.lead_time_not_respected", "The slot starts too soon for the minimum booking lead time.");
    public static readonly DomainError BeyondBookingHorizon = new("reservations.reservation.beyond_booking_horizon", "The slot starts further ahead than the booking horizon allows.");
    public static readonly DomainError DurationTooShort = new("reservations.reservation.duration_too_short", "The slot is shorter than the minimum reservation duration.");
    public static readonly DomainError DurationTooLong = new("reservations.reservation.duration_too_long", "The slot is longer than the maximum reservation duration.");
    public static readonly DomainError LocationNotOperational = new("reservations.reservation.location_not_operational", "Reservations can only be taken for an active location.");
    public static readonly DomainError CustomerAnonymized = new("reservations.reservation.customer_anonymized", "A reservation cannot be attached to an anonymized customer.");
    public static readonly DomainError CapacityInsufficient = new("reservations.reservation.capacity_insufficient", "The remaining capacity of this slot is not enough for the party.");
    public static readonly DomainError AvailabilityDecisionMismatch = new("reservations.reservation.availability_decision_mismatch", "The availability decision was not made for this location, slot, party size and reservation.");
    public static readonly DomainError AvailabilityDecisionAlreadyUsed = new("reservations.reservation.availability_decision_already_used", "This availability decision has already been used; evaluate the availability again.");
    public static readonly DomainError AlreadyTerminal = new("reservations.reservation.already_terminal", "A completed, cancelled or no-show reservation can no longer change.");
    public static readonly DomainError NotPending = new("reservations.reservation.not_pending", "Only a pending reservation can be confirmed.");
    public static readonly DomainError NotConfirmed = new("reservations.reservation.not_confirmed", "The reservation must be confirmed first.");
    public static readonly DomainError NotSeated = new("reservations.reservation.not_seated", "Only a seated reservation can be completed.");
    public static readonly DomainError NotModifiableAfterSeating = new("reservations.reservation.not_modifiable_after_seating", "A reservation can no longer be rescheduled or resized once the guests are seated.");
    public static readonly DomainError NotCancellableAfterSeating = new("reservations.reservation.not_cancellable_after_seating", "A reservation can no longer be cancelled once the guests are seated.");
    public static readonly DomainError CancellationCutoffPassed = new("reservations.reservation.cancellation_cutoff_passed", "The customer can no longer cancel: the cancellation cutoff has passed.");
    public static readonly DomainError ModificationCutoffPassed = new("reservations.reservation.modification_cutoff_passed", "The customer can no longer change the reservation: the cutoff has passed.");
    public static readonly DomainError NoShowBeforeStart = new("reservations.reservation.no_show_before_start", "A reservation cannot be marked as no-show before its start time.");
    public static readonly DomainError ActorInvalid = new("reservations.reservation.actor_invalid", "The actor of the change is not a known value.");
    public static readonly DomainError StatusUnknown = new("reservations.reservation.status_unknown", "The reservation status is not a known value.");
    public static readonly DomainError DoesNotHoldCapacity = new("reservations.reservation.does_not_hold_capacity", "This reservation no longer holds capacity.");

    public static readonly DomainError GuestNameRequired = new("reservations.guest_contact.name_required", "Guest name is required.");
    public static readonly DomainError GuestNameTooLong = new("reservations.guest_contact.name_too_long", "Guest name is too long.");
    public static readonly DomainError GuestContactChannelRequired = new("reservations.guest_contact.channel_required", "A phone number or an email is required to reach the guest.");

    public static readonly DomainError PolicyMinLeadTimeNegative = new("reservations.policy.min_lead_time_negative", "Minimum lead time cannot be negative.");
    public static readonly DomainError PolicyMaxAdvanceNotPositive = new("reservations.policy.max_advance_not_positive", "Booking horizon must be greater than zero.");
    public static readonly DomainError PolicyMaxAdvanceNotAfterLeadTime = new("reservations.policy.max_advance_not_after_lead_time", "Booking horizon must be greater than the minimum lead time.");
    public static readonly DomainError PolicyDefaultDurationNotPositive = new("reservations.policy.default_duration_not_positive", "Default duration must be greater than zero.");
    public static readonly DomainError PolicyCancellationCutoffNegative = new("reservations.policy.cancellation_cutoff_negative", "Cancellation cutoff cannot be negative.");
    public static readonly DomainError PolicyMaxAdvanceTooLong = new("reservations.policy.max_advance_too_long", "Booking horizon cannot exceed two years.");
    public static readonly DomainError PolicyCancellationCutoffTooLong = new("reservations.policy.cancellation_cutoff_too_long", "Cancellation cutoff cannot exceed two years.");
    public static readonly DomainError PolicyDurationBoundsInvalid = new("reservations.policy.duration_bounds_invalid", "Minimum duration must be greater than zero, not above the maximum duration, itself at most 24 hours.");
    public static readonly DomainError PolicyDefaultDurationOutOfBounds = new("reservations.policy.default_duration_out_of_bounds", "Default duration must lie between the minimum and the maximum duration.");
    public static readonly DomainError PolicyMaxPartySizeInvalid = new("reservations.policy.max_party_size_invalid", "Maximum party size must be between 1 and 10000.");

    public static readonly DomainError CapacityNegative = new("reservations.availability.capacity_negative", "Capacity cannot be negative.");
    public static readonly DomainError CapacityTooLarge = new("reservations.availability.capacity_too_large", "Capacity is beyond the supported range.");
}
