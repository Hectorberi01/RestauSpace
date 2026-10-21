namespace Tenants.Application.Abstractions;

public interface IPricedCatalogLedger
{
    Task RecordAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}
