namespace Orders.Application;

/// <summary>Erreurs des cas d'usage d'Orders (celles des règles métier vivent dans Orders.Domain).</summary>
public static class OrdersApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("orders.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError OrderNotFound = new("orders.order.not_found", "The order does not exist.");
    public static readonly DomainError LocationNotFound = new("orders.location.not_found", "The location does not exist.");
    public static readonly DomainError ProductNotFound = new("orders.product.not_found", "The product does not exist.");
    public static readonly DomainError CustomerNotFound = new("orders.customer.not_found", "The customer does not exist.");
    public static readonly DomainError TableSessionNotFound = new("orders.table_session.not_found", "The table session does not exist.");

    public static readonly DomainError TenantNotWritable =
        new("orders.tenant.not_writable", "The restaurant account is suspended or closed: its orders can be read, not changed.");

    public static readonly DomainError MonthlyOrderLimitReached =
        new("orders.plan.monthly_limit_reached", "The plan's monthly order limit is reached: upgrade the plan or wait for next month.");

    public static readonly DomainError NumberAlreadyUsed =
        new("orders.order.number_already_used", "Another order of this location already has this number; try again.", DomainErrorKind.Conflict);

    public static readonly DomainError DiscountSourceNotExternal =
        new("orders.discount.source_not_external", "A manual discount is applied by a person, not by another module.");

    public static readonly DomainError PeriodInvalid = new("orders.search.period_invalid", "The period must start before it ends.");

    public static readonly DomainError AmountRangeInvalid =
        new("orders.search.amount_invalid", "Amounts must be zero or more, the minimum at most the maximum.");

    public static readonly DomainError FilterInvalid = new("orders.search.filter_invalid", "Unknown order type or channel.");

    public static readonly DomainError PagingInvalid =
        new("orders.paging.invalid", "Page must be 1 or more and page size between 1 and 100.");
}
