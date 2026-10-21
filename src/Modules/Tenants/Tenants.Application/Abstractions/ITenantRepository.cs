using Tenants.Domain.Tenants;

namespace Tenants.Application.Abstractions;

public interface ITenantRepository
{
    Task<IEnumerable<Tenant>> ListAsync(CancellationToken cancellationToken = default);
    Task<Tenant?> FindAsync(TenantId id, CancellationToken cancellationToken = default);
    void Add(Tenant tenant);
}
