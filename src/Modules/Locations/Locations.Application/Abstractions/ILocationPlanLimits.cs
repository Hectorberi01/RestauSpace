namespace Locations.Application.Abstractions;

/// <summary>
/// Le nombre maximal d'établissements (non archivés) prévu par le plan du restaurant, fourni par l'hôte depuis Billing.
/// Null : aucun plafond. Vérifié à la création ; un établissement archivé libère sa place.
/// </summary>
public interface ILocationPlanLimits
{
    Task<long?> MaxLocationsAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}
