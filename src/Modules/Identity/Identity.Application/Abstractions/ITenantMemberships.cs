namespace Identity.Application.Abstractions;

/// <summary>
/// Indique si un utilisateur peut agir pour un tenant : membre du personnel actif d'un tenant non fermé. Identity ne
/// connaît pas les restaurants : l'hôte répond à partir des modules Staff et Tenants.
/// </summary>
public interface ITenantMemberships
{
    Task<bool> CanActForTenantAsync(UserId userId, TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Membre gelé (au-delà de la limite du plan) : il ne peut pas sélectionner ce restaurant. Sans hôte branché : jamais.</summary>
    Task<bool> IsFrozenAsync(UserId userId, TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>Restaurants dont l'utilisateur est le SEUL propriétaire actif : son départ les laisserait sans propriétaire.</summary>
    Task<IReadOnlyList<TenantId>> SoleOwnerTenantsAsync(UserId userId, CancellationToken cancellationToken = default);
}
