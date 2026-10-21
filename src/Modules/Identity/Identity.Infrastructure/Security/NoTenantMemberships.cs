using Identity.Application.Abstractions;

namespace Identity.Infrastructure.Security;

/// <summary>
/// Implémentation par défaut tant que l'hôte n'a pas branché les modules Staff et Tenants
/// (<c>AddIdentityTenantMemberships</c>) : personne ne peut agir pour aucun tenant. Fermé par défaut, volontairement.
/// </summary>
internal sealed class NoTenantMemberships : ITenantMemberships
{
    public Task<bool> CanActForTenantAsync(UserId userId, TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<TenantId>> SoleOwnerTenantsAsync(UserId userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantId>>([]);
}
