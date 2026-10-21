namespace Kitchen.Domain;

public static class KitchenStationErrors
{
    public static readonly DomainError NameEmpty = new("kitchen.station.name_empty", "A station name is required.");
    public static readonly DomainError NameTooLong = new("kitchen.station.name_too_long", "A station name cannot exceed 60 characters.");
    public static readonly DomainError NameNotUnique = new("kitchen.station.name_not_unique", "Another station of this location already uses this name.", DomainErrorKind.Conflict);
    public static readonly DomainError AlreadyActive = new("kitchen.station.already_active", "The station is already active.");
    public static readonly DomainError AlreadyInactive = new("kitchen.station.already_inactive", "The station is already inactive.");
}
