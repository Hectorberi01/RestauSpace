namespace Floor.Domain;

public static class DiningAreaErrors
{
    public static readonly DomainError NameRequired = new("floor.dining_area.name_required", "Dining area name is required.");
    public static readonly DomainError NameTooLong = new("floor.dining_area.name_too_long", "Dining area name is too long.");
    public static readonly DomainError TableLabelAlreadyUsed = new("floor.dining_area.table_label_already_used", "Another table of this dining area already has this label.", DomainErrorKind.Conflict);
    public static readonly DomainError TableNotFound = new("floor.dining_area.table_not_found", "This table does not belong to the dining area.");
    public static readonly DomainError TableHasOpenSession = new("floor.dining_area.table_has_open_session", "A table with an open session cannot be removed.");
    public static readonly DomainError TableLabelRequired = new("floor.table.label_required", "Table label is required.");
    public static readonly DomainError TableLabelTooLong = new("floor.table.label_too_long", "Table label is too long.");
    public static readonly DomainError TableCapacityNotPositive = new("floor.table.capacity_not_positive", "Table capacity must be greater than zero.");
    public static readonly DomainError TableAlreadyOutOfService = new("floor.table.already_out_of_service", "The table is already out of service.");
    public static readonly DomainError TableNotOutOfService = new("floor.table.not_out_of_service", "Only an out-of-service table can be restored.");
    public static readonly DomainError LayoutInvalid = new("floor.table.layout_invalid", "The table must fit in the plan, with a size between 4 and 40 (same width and height unless rectangular).");
    public static readonly DomainError AreaHasOpenSession = new("floor.dining_area.has_open_session", "A dining area with an occupied table cannot be deleted.", DomainErrorKind.Conflict);
    public static readonly DomainError QrTokenInvalid = new("floor.table.qr_token_invalid", "Invalid table QR token.");
}
