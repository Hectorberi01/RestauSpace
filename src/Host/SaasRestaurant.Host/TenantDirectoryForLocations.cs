using Locations.Application.Abstractions;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

/// <summary>Répond à Locations depuis Tenants : fuseau horaire par défaut, écritures métier autorisées ou non.</summary>
internal sealed class TenantDirectoryForLocations(CachedTenants tenants) : ITenantDirectory
{
    public async Task<TenantSettings?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(tenantId, cancellationToken);
        return tenant is null
            ? null
            : new TenantSettings(TimeZoneId.Create(tenant.TimeZoneId), tenant.CanAcceptBusinessWrites);
    }
}
