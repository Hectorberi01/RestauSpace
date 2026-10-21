namespace Kitchen.Application;

/// <summary>Erreurs des cas d'usage de Kitchen (les erreurs des règles métier sont dans Kitchen.Domain).</summary>
public static class KitchenApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("kitchen.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError StationNotFound = new("kitchen.station.not_found", "The station does not exist.");
    public static readonly DomainError RoutingNotFound = new("kitchen.station_routing.not_found", "This location has no station routing yet.");
    public static readonly DomainError TicketNotFound = new("kitchen.ticket.not_found", "The kitchen ticket does not exist.");
    public static readonly DomainError LocationNotFound = new("kitchen.location.not_found", "The location does not exist.");
    public static readonly DomainError OrderNotFound = new("kitchen.order.not_found", "The order does not exist.");
    public static readonly DomainError ProductNotFound = new("kitchen.product.not_found", "The product does not exist.");

    public static readonly DomainError TenantNotWritable =
        new("kitchen.tenant.not_writable", "The restaurant account is suspended or closed: its kitchen can be read, not changed.");

    public static readonly DomainError StationInUse =
        new("kitchen.station.in_use", "The station is used by the routing of its location; route its products elsewhere first.");

    public static readonly DomainError OrderNotAccepted =
        new("kitchen.order.not_accepted", "A kitchen ticket is created only for an order that has been accepted.");

    public static readonly DomainError TicketAlreadyExists =
        new("kitchen.ticket.already_exists", "This order already has a kitchen ticket.", DomainErrorKind.Conflict);
}
