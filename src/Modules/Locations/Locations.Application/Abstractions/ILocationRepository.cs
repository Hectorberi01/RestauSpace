using Locations.Domain.Locations;

namespace Locations.Application.Abstractions;

/// <summary>
/// Établissements d'UN SEUL tenant à la fois : chaque méthode prend le tenant, lu dans le contexte de la requête par
/// le handler, jamais dans la requête elle-même. Un établissement d'un autre tenant est tout simplement introuvable.
/// </summary>
public interface ILocationRepository
{
    Task<RestaurantLocation?> FindAsync(TenantId tenantId, LocationId id, CancellationToken cancellationToken = default);

    /// <summary>Page en lecture seule, triée par nom. <paramref name="includeArchived"/> false : sans les archivés.</summary>
    Task<LocationPage> ListAsync(
        TenantId tenantId, bool includeArchived, int skip, int take, CancellationToken cancellationToken = default);

    void Add(RestaurantLocation location);
}

public sealed record LocationPage(IReadOnlyList<RestaurantLocation> Items, int TotalCount);
