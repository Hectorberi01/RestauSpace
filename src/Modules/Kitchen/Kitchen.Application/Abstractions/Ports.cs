using Kitchen.Domain;

namespace Kitchen.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête ou du message traité (jamais de la
// requête elle-même). Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IKitchenStationRepository
{
    Task<KitchenStation?> FindAsync(TenantId tenantId, StationId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KitchenStation>> FindManyAsync(TenantId tenantId, IEnumerable<StationId> ids, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par nom.</summary>
    Task<IReadOnlyList<KitchenStation>> ListAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    void Add(KitchenStation station);
}

public interface IStationRoutingRepository
{
    /// <summary>Le routage (unique) de l'établissement.</summary>
    Task<StationRouting?> FindByLocationAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    void Add(StationRouting routing);
}

public interface IKitchenTicketRepository
{
    Task<KitchenTicket?> FindAsync(TenantId tenantId, KitchenTicketId id, CancellationToken cancellationToken = default);

    /// <summary>Le ticket (unique) d'une commande.</summary>
    Task<KitchenTicket?> FindByOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Écran cuisine : tickets ni terminés ni annulés, en lecture seule, priorité la plus haute puis plus anciens d'abord ;
    /// <paramref name="stationId"/> : seulement ceux qui ont au moins une ligne à ce poste.
    /// </summary>
    Task<IReadOnlyList<KitchenTicket>> ListOpenAsync(
        TenantId tenantId, LocationId? locationId, StationId? stationId, CancellationToken cancellationToken = default);

    void Add(KitchenTicket ticket);
}

/// <summary>Ce que Kitchen doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface IKitchenTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Un établissement du tenant courant, fourni par l'hôte depuis Locations ; null s'il n'existe pas.</summary>
public interface IKitchenLocations
{
    Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

/// <summary>Un produit du tenant courant existe-t-il au catalogue ? Fourni par l'hôte depuis Catalog.</summary>
public interface IKitchenProducts
{
    Task<bool> ExistsAsync(TenantId tenantId, ProductId productId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Une commande du tenant courant telle que la cuisine peut la voir (lignes sans prix), fournie par l'hôte depuis
/// Orders ; null si elle n'existe pas.
/// </summary>
public interface IKitchenOrders
{
    Task<KitchenOrderToPrepare?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);
}

/// <param name="IsAccepted">La commande a été acceptée (ou va plus loin) et n'est pas annulée : elle peut entrer en cuisine.</param>
/// <param name="IsDineIn">Repas servi à table : les services après le premier attendent d'être envoyés par la salle.</param>
public sealed record KitchenOrderToPrepare(
    OrderId OrderId, LocationId LocationId, bool IsAccepted, IReadOnlyList<KitchenOrderLineToPrepare> Lines, bool IsDineIn = false);

public sealed record KitchenOrderLineToPrepare(
    OrderItemId OrderItemId,
    ProductId ProductId,
    string ProductName,
    string? VariantName,
    int Quantity,
    IReadOnlyList<string> ModifierNames,
    string? Note);
