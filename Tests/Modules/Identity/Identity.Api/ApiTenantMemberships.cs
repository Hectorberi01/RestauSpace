using System.Collections.Concurrent;
using Identity.Application.Abstractions;

namespace Identity.Api;

/// <summary>Appartenances accordées puis retirées par les tests d'API, à la place du module Staff.</summary>
public sealed class ApiTenantMemberships : ITenantMemberships
{
    private static readonly ConcurrentDictionary<(Guid UserId, Guid TenantId), bool> Members = new();

    public static void Grant(Guid userId, Guid tenantId) => Members[(userId, tenantId)] = true;

    public static void Revoke(Guid userId, Guid tenantId) => Members.TryRemove((userId, tenantId), out _);

    public Task<bool> CanActForTenantAsync(UserId userId, TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Members.ContainsKey((userId.Value, tenantId.Value)));

    public Task<IReadOnlyList<TenantId>> SoleOwnerTenantsAsync(UserId userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantId>>([]);
}
