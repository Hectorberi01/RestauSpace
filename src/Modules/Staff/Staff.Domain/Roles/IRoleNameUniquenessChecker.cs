namespace Staff.Domain;

/// <summary>Port : « ce nom de rôle est-il libre dans ce tenant ? » (insensible à la casse, hors <paramref name="excluding"/>).</summary>
public interface IRoleNameUniquenessChecker
{
    Task<bool> IsNameUniqueAsync(TenantId tenantId, RoleName name, RoleId? excluding = null, CancellationToken cancellationToken = default);
}
