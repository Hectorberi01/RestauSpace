namespace Floor.Application;

/// <summary>Erreurs des cas d'usage Floor (celles des règles métier sont dans Floor.Domain).</summary>
public static class FloorApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("floor.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError DiningAreaNotFound = new("floor.dining_area.not_found", "The dining area does not exist.");
    public static readonly DomainError TableNotFound = new("floor.table.not_found", "The table does not exist.");
    public static readonly DomainError TableSessionNotFound = new("floor.table_session.not_found", "The table session does not exist.");
    public static readonly DomainError WaitingPartyNotFound = new("floor.waiting_party.not_found", "This party is not in the waiting list.");
    public static readonly DomainError LocationNotFound = new("floor.location.not_found", "The location does not exist.");

    public static readonly DomainError TenantNotWritable =
        new("floor.tenant.not_writable", "The restaurant account is suspended or closed: its floor can be read, not changed.");
}
