using Locations.Application.Abstractions;

namespace Locations.Application;

/// <summary>
/// Gel des établissements, calculé à l'usage : les non archivés sont rangés par date de création ; au-delà de la limite
/// du plan, ils sont gelés (plus de nouvelle commande ni de réservation). Archiver ou monter de plan lève le gel.
/// </summary>
public sealed class LocationFreeze(ILocationRepository locations, ILocationPlanLimits limits)
{
    public async Task<IReadOnlySet<LocationId>> FrozenAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        if (await limits.MaxLocationsAsync(tenantId, cancellationToken) is not { } limit)
        {
            return new HashSet<LocationId>();
        }

        var ranked = await locations.ListOpenIdsByCreationAsync(tenantId, cancellationToken);
        return ranked.Skip((int)Math.Clamp(limit, 0, int.MaxValue)).ToHashSet();
    }

    public async Task<bool> IsFrozenAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        (await FrozenAsync(tenantId, cancellationToken)).Contains(locationId);
}
