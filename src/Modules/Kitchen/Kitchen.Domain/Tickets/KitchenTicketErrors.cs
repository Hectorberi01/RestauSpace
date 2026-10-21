namespace Kitchen.Domain;

public static class KitchenTicketErrors
{
    public static readonly DomainError SnapshotWithoutLine = new("kitchen.ticket.snapshot_without_line", "A kitchen ticket needs at least one order line.");
    public static readonly DomainError SnapshotDuplicateOrderItem = new("kitchen.ticket.snapshot_duplicate_order_item", "The same order item cannot appear twice in a kitchen ticket.");
    public static readonly DomainError LineNameEmpty = new("kitchen.ticket.line_name_empty", "A ticket line needs the name of the item to prepare.");
    public static readonly DomainError LineNameTooLong = new("kitchen.ticket.line_name_too_long", "A ticket line name cannot exceed 200 characters.");
    public static readonly DomainError LineQuantityNotPositive = new("kitchen.ticket.line_quantity_not_positive", "A ticket line quantity must be greater than zero.");
    public static readonly DomainError LineVariantNameTooLong = new("kitchen.ticket.line_variant_name_too_long", "A ticket line variant name cannot exceed 120 characters.");
    public static readonly DomainError LineModifierNameEmpty = new("kitchen.ticket.line_modifier_name_empty", "A ticket line modifier needs a name.");
    public static readonly DomainError LineModifierNameTooLong = new("kitchen.ticket.line_modifier_name_too_long", "A ticket line modifier name cannot exceed 120 characters.");
    public static readonly DomainError LineTooManyModifiers = new("kitchen.ticket.line_too_many_modifiers", "A ticket line cannot carry more than 50 modifiers.");
    public static readonly DomainError LineNoteTooLong = new("kitchen.ticket.line_note_too_long", "A ticket line note cannot exceed 200 characters.");
    public static readonly DomainError StationOtherLocation = new("kitchen.ticket.station_other_location", "A ticket line must be routed to a station of the order's location.");
    public static readonly DomainError StationInactive = new("kitchen.ticket.station_inactive", "A ticket line cannot be routed to an inactive station.");
    public static readonly DomainError ReadinessPercentageOutOfRange = new("kitchen.ticket.readiness_percentage_out_of_range", "The required ready percentage must be between 1 and 100.");
    public static readonly DomainError PriorityUnknown = new("kitchen.ticket.priority_unknown", "Unknown kitchen priority.");
    public static readonly DomainError Closed = new("kitchen.ticket.closed", "A completed or cancelled ticket can no longer change.");
    public static readonly DomainError NotQueued = new("kitchen.ticket.not_queued", "Only a queued ticket can be started.");
    public static readonly DomainError NotInProgress = new("kitchen.ticket.not_in_progress", "Only a ticket in progress can be marked ready.");
    public static readonly DomainError ItemsNotReady = new("kitchen.ticket.items_not_ready", "The ticket cannot be ready before its items are ready.");
    public static readonly DomainError NotReady = new("kitchen.ticket.not_ready", "Only a ready ticket can be completed.");
    public static readonly DomainError ItemsNotAllReady = new("kitchen.ticket.items_not_all_ready", "A ticket can only be completed once every item is ready.");
    public static readonly DomainError CancellationReasonTooLong = new("kitchen.ticket.cancellation_reason_too_long", "A cancellation reason cannot exceed 500 characters.");
    public static readonly DomainError ItemNotFound = new("kitchen.ticket.item_not_found", "The item does not belong to this ticket.");
    public static readonly DomainError ItemNotQueued = new("kitchen.ticket_item.not_queued", "Only a queued item can be started.");
    public static readonly DomainError ItemHeld = new("kitchen.ticket_item.held", "This dish waits for its course to be sent by the dining room.");
    public static readonly DomainError CourseUnknown = new("kitchen.ticket.course_unknown", "Unknown course.");
    public static readonly DomainError NothingToFire = new("kitchen.ticket.nothing_to_fire", "No dish of this course is waiting.");
    public static readonly DomainError ItemNotPreparing = new("kitchen.ticket_item.not_preparing", "Only an item being prepared can be marked ready.");
}
