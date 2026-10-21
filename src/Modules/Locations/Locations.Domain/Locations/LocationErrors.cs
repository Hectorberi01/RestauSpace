namespace Locations.Domain.Locations;

public static class LocationErrors
{
    public static readonly DomainError NameEmpty = new("locations.location_name.empty", "Location name is required.");
    public static readonly DomainError NameLengthInvalid = new("locations.location_name.length_invalid", "Location name must contain between 2 and 100 characters.");
    public static readonly DomainError CapacityNegative = new("locations.capacity.negative", "Capacity cannot be negative.");
    public static readonly DomainError CountryCodeUnknown = new("locations.address.country_code_unknown", "Country code is not an ISO 3166-1 alpha-2 code.");
    public static readonly DomainError AddressStreetEmpty = new("locations.address.street_empty", "Street is required.");
    public static readonly DomainError AddressCityEmpty = new("locations.address.city_empty", "City is required.");
    public static readonly DomainError AddressFieldTooLong = new("locations.address.field_too_long", "Street, city or postal code is too long.");
    public static readonly DomainError AddressCoordinatesIncomplete = new("locations.address.coordinates_incomplete", "Latitude and longitude must be provided together.");
    public static readonly DomainError AddressLatitudeOutOfRange = new("locations.address.latitude_out_of_range", "Latitude must be between -90 and 90.");
    public static readonly DomainError AddressLongitudeOutOfRange = new("locations.address.longitude_out_of_range", "Longitude must be between -180 and 180.");
    public static readonly DomainError RangeStartNotBeforeEnd = new("locations.business_hours.range_start_not_before_end", "A same-day range must start strictly before it ends; use an overnight range to cross midnight.");
    public static readonly DomainError OvernightRangeTooLong = new("locations.business_hours.overnight_range_too_long", "An overnight range must end, on the next day, no later than its start time.");
    public static readonly DomainError HoursRangesOverlap = new("locations.business_hours.ranges_overlap", "Opening ranges must not overlap.");
    public static readonly DomainError HoursDayUnknown = new("locations.business_hours.day_unknown", "Day of week is not a known value.");
    public static readonly DomainError HoursSlotInvalid = new("locations.business_hours.slot_invalid", "An opening slot needs a valid day of week and a range.");
    public static readonly DomainError ExceptionalDateAlreadyDefined = new("locations.exceptional_schedule.date_already_defined", "An exceptional schedule already exists for this date.", DomainErrorKind.Conflict);
    public static readonly DomainError ExceptionalOpeningWithoutRange = new("locations.exceptional_schedule.opening_without_range", "An exceptional opening needs at least one range.");
    public static readonly DomainError ExceptionalScheduleNotFound = new("locations.exceptional_schedule.not_found", "No exceptional schedule exists for this date.");
    public static readonly DomainError ReasonTooLong = new("locations.location.reason_too_long", "Reason cannot exceed 200 characters.");
    public static readonly DomainError Archived = new("locations.location.archived", "The location is archived; no further change is allowed.");
    public static readonly DomainError OnlyDraftCanBeActivated = new("locations.location.only_draft_can_be_activated", "Only a draft location can be activated.");
    public static readonly DomainError OnlyActiveCanBeTemporarilyClosed = new("locations.location.only_active_can_be_temporarily_closed", "Only an active location can be temporarily closed.");
    public static readonly DomainError OnlyTemporarilyClosedCanBeReopened = new("locations.location.only_temporarily_closed_can_be_reopened", "Only a temporarily closed location can be reopened.");
}
