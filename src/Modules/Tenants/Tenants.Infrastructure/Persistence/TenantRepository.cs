using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Infrastructure.Persistence;

internal sealed class TenantRepository(TenantsDbContext context) : ITenantRepository
{
    public async Task<IEnumerable<Tenant>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await context.Tenants.ToListAsync(cancellationToken);
    }

    public async Task<Tenant?> FindAsync(TenantId id, CancellationToken cancellationToken = default){
        return await context.Tenants.FindAsync([id], cancellationToken);
    }

    public void Add(Tenant tenant) {
        context.Tenants.Add(tenant); 
    }
}
