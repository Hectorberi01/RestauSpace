namespace Kitchen.Domain;

public static class StationRoutingErrors
{
    public static readonly DomainError LocationAlreadyHasRouting = new("kitchen.station_routing.location_already_has_routing", "This location already has a station routing.", DomainErrorKind.Conflict);
    public static readonly DomainError StationOtherLocation = new("kitchen.station_routing.station_other_location", "A routing can only target a station of its own location.");
    public static readonly DomainError StationInactive = new("kitchen.station_routing.station_inactive", "A routing cannot target an inactive station.");
    public static readonly DomainError RouteLimitReached = new("kitchen.station_routing.route_limit_reached", "A routing cannot hold more than 2000 product routes.");
    public static readonly DomainError RouteNotFound = new("kitchen.station_routing.route_not_found", "This product has no route in this routing.");
}
