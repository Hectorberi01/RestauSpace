namespace Delivery.Domain;

public static class DeliverySettingsErrors
{
    public static readonly DomainError LeadTimeOutOfRange = new("delivery.settings.lead_time_out_of_range", "A lead time must be between 0 and 240 minutes.");
    public static readonly DomainError DeliveryNeedsZone = new("delivery.settings.delivery_needs_zone", "Delivery needs at least one active zone.");
    public static readonly DomainError LastActiveZone = new("delivery.settings.last_active_zone", "The last active zone cannot be removed or deactivated while delivery is enabled.");
    public static readonly DomainError TooManyZones = new("delivery.settings.too_many_zones", "A location is limited to 50 delivery zones.");
    public static readonly DomainError ZoneNotFound = new("delivery.zone.not_found", "The delivery zone does not exist.");
    public static readonly DomainError ZoneNameRequired = new("delivery.zone.name_required", "A delivery zone needs a name.");
    public static readonly DomainError ZoneNameTooLong = new("delivery.zone.name_too_long", "A zone name is limited to 80 characters.");
    public static readonly DomainError ZoneNameAlreadyUsed = new("delivery.zone.name_already_used", "Another zone already has this name.", DomainErrorKind.Conflict);
    public static readonly DomainError ZoneAmountNegative = new("delivery.zone.amount_negative", "A delivery fee or minimum order cannot be negative.");
    public static readonly DomainError ZoneMinutesOutOfRange = new("delivery.zone.minutes_out_of_range", "The estimated delivery time must be between 5 and 240 minutes.");
}
