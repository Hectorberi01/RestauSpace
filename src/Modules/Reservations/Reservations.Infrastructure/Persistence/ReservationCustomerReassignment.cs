using Reservations.Application.Abstractions;
using Reservations.Domain;

namespace Reservations.Infrastructure.Persistence;

/// <summary>Mise à jour en masse de la colonne client, dans le tenant donné ; la version de chaque ligne change avec elle.</summary>
internal sealed class ReservationCustomerReassignment(ReservationsDbContext context) : IReservationCustomerReassignment
{
    public Task<int> ReassignAsync(TenantId tenantId, CustomerId from, CustomerId to, CancellationToken cancellationToken = default) =>
        context.Reservations
            .Where(row => row.TenantId == tenantId && row.CustomerId == from)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerId, (CustomerId?)to), cancellationToken);
}
