namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>
/// Indique si un utilisateur peut effectuer une action dans un tenant ; le module Staff répond d'après ses rôles.
/// Les codes de permission sont déclarés par chaque module ("locations.manage") ; Staff ne connaît que les codes,
/// pas les modules.
/// </summary>
public interface IPermissionChecker
{
    /// <summary>Actions au niveau du tenant : le rôle tenant-wide d'un membre du personnel ACTIF.</summary>
    Task<bool> HasTenantPermissionAsync(
        UserId userId, TenantId tenantId, string permission, CancellationToken cancellationToken = default);

    /// <summary>Actions sur un établissement : son rôle sur place, sinon le rôle tenant-wide (membre actif seulement).</summary>
    Task<bool> HasLocationPermissionAsync(
        UserId userId, TenantId tenantId, LocationId locationId, string permission, CancellationToken cancellationToken = default);
}
