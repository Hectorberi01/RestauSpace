using Orders.Application.Abstractions;
using Orders.Domain;

namespace Orders.Infrastructure.Persistence;

/// <summary>Mise à jour en masse de la colonne client, dans le tenant donné ; la version de chaque ligne change avec elle.</summary>
internal sealed class OrderCustomerReassignment(OrdersDbContext context) : IOrderCustomerReassignment
{
    public Task<int> ReassignAsync(TenantId tenantId, CustomerId from, CustomerId to, CancellationToken cancellationToken = default) =>
        context.Orders
            .Where(row => row.TenantId == tenantId && row.CustomerId == from)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerId, (CustomerId?)to), cancellationToken);
}
