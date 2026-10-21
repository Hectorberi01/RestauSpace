using SaasRestaurant.BuildingBlocks.Infrastructure;
using Tenants.Application;
using Tenants.Application.Abstractions;

namespace Tenants.Infrastructure.Persistence;

internal sealed class TenantsUnitOfWork(TenantsDbContext context)
    : OutboxUnitOfWork<TenantsDbContext>(context), ITenantsUnitOfWork
{
    protected override DomainError ConcurrentUpdate => TenantApplicationErrors.ConcurrentUpdate;
}
