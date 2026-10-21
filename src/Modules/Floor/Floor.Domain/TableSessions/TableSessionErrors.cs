namespace Floor.Domain;

public static class TableSessionErrors
{
    public static readonly DomainError GuestCountNotPositive = new("floor.table_session.guest_count_not_positive", "Guest count must be greater than zero.");
    public static readonly DomainError TableOutOfService = new("floor.table_session.table_out_of_service", "A session cannot be opened on an out-of-service table.");
    public static readonly DomainError TableAlreadyOccupied = new("floor.table_session.table_already_occupied", "This table already has an open session.", DomainErrorKind.Conflict);
    public static readonly DomainError ClosedIsImmutable = new("floor.table_session.closed_is_immutable", "A closed session can no longer be changed.");
    public static readonly DomainError HasOpenOrders = new("floor.table_session.has_open_orders", "A session cannot be closed while one of its orders is still open.");
    public static readonly DomainError AlreadyClosed = new("floor.table_session.already_closed", "The session is already closed.");
    public static readonly DomainError OtherLocation = new("floor.table_session.other_location", "Tables can only be moved or merged within the same location.");
    public static readonly DomainError MergeWithItself = new("floor.table_session.merge_with_itself", "A table cannot be merged with itself.");
}
