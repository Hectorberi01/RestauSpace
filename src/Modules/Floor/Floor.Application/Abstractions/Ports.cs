using Floor.Domain;

namespace Floor.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête (jamais de la requête elle-même).
// Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IDiningAreaRepository
{
    Task<DiningArea?> FindAsync(TenantId tenantId, DiningAreaId id, CancellationToken cancellationToken = default);

    /// <summary>La zone qui contient la table donnée.</summary>
    Task<DiningArea?> FindByTableAsync(TenantId tenantId, TableId tableId, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, triée par nom ; <paramref name="locationId"/> null = tous les établissements.</summary>
    Task<IReadOnlyList<DiningArea>> ListAsync(TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default);

    /// <summary>Les zones d'un établissement, suivies pour modification.</summary>
    Task<IReadOnlyList<DiningArea>> ListForUpdateAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    /// <summary>La zone dont une table porte ce jeton de QR (lecture seule).</summary>
    Task<DiningArea?> FindByQrTokenAsync(TenantId tenantId, string token, CancellationToken cancellationToken = default);

    void Add(DiningArea area);

    void Remove(DiningArea area);
}

public interface ITableSessionRepository
{
    Task<TableSession?> FindAsync(TenantId tenantId, TableSessionId id, CancellationToken cancellationToken = default);

    /// <summary>Sessions ouvertes, en lecture seule, par heure d'ouverture ; <paramref name="locationId"/> null = toutes.</summary>
    Task<IReadOnlyList<TableSession>> ListOpenAsync(
        TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default);

    void Add(TableSession session);
}

/// <summary>La file d'attente à la porte (groupes sans réservation), pour le tenant donné.</summary>
public interface IWaitingPartyRepository
{
    Task<WaitingParty?> FindAsync(TenantId tenantId, WaitingPartyId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture : les groupes en attente de l'établissement, et ceux fermés depuis <paramref name="closedSinceUtc"/>, par arrivée.</summary>
    Task<IReadOnlyList<WaitingParty>> ListAsync(TenantId tenantId, LocationId locationId, DateTimeOffset closedSinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Efface les groupes fermés avant <paramref name="beforeUtc"/> (rien n'est gardé au-delà de la journée).</summary>
    Task<int> PurgeClosedAsync(TenantId tenantId, LocationId locationId, DateTimeOffset beforeUtc, CancellationToken cancellationToken = default);

    void Add(WaitingParty party);
}

/// <summary>Ce que Floor doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface IFloorTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Un établissement du tenant courant, fourni par l'hôte depuis le module Locations ; null s'il n'existe pas.</summary>
public interface IFloorLocations
{
    Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

public interface ITableSessionOrderLedger
{
    Task RecordOpenedAsync(TenantId tenantId, Guid orderId, TableSessionId tableSessionId, CancellationToken cancellationToken = default);

    Task RecordClosedAsync(TenantId tenantId, Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Les commandes en cours d'une session passent sur une autre (fusion de tables), servi par l'hôte depuis Orders.
/// </summary>
public interface ITableSessionOrdersMover
{
    Task MoveOpenOrdersAsync(TenantId tenantId, TableSessionId from, TableSessionId to, CancellationToken cancellationToken = default);
}
