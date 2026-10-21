namespace Locations.Application;

/// <summary>Erreurs des cas d'usage Locations (celles des règles métier sont dans Locations.Domain).</summary>
public static class LocationsApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("locations.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // Également la réponse pour un établissement d'un autre tenant : ne jamais révéler qu'il existe.
    public static readonly DomainError NotFound = new("locations.location.not_found", "The location does not exist.");

    public static readonly DomainError TenantNotWritable =
        new("locations.tenant.not_writable", "The restaurant account is suspended or closed: locations can be read, not changed.");

    public static readonly DomainError PlanLimitReached =
        new("locations.plan.limit_reached", "The plan's location limit is reached: archive a location or upgrade the plan.");

    public static readonly DomainError TimeInvalid =
        new("locations.time.invalid", "Times must be written HH:mm (00:00 to 23:59).");

    public static readonly DomainError PagingInvalid =
        new("locations.paging.invalid", "Page must be 1 or more and page size between 1 and 100.");
}
