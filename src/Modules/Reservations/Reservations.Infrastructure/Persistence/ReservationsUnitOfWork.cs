using Reservations.Application;
using Reservations.Application.Abstractions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Reservations.Infrastructure.Persistence;

/// <summary>
/// Comme les autres modules (outbox dans la même transaction), plus une chose : quand le verrou de capacité a ouvert une
/// transaction, c'est ici qu'elle est validée, ce qui relâche le verrou. Sans écriture réussie, elle est abandonnée avec
/// le contexte (fin de la requête).
/// </summary>
internal sealed class ReservationsUnitOfWork(ReservationsDbContext context)
    : OutboxUnitOfWork<ReservationsDbContext>(context), IReservationsUnitOfWork, IUnitOfWork
{
    protected override DomainError ConcurrentUpdate => ReservationsApplicationErrors.ConcurrentUpdate;

    public new async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await base.SaveChangesAsync(cancellationToken);
        if (Context.Database.CurrentTransaction is { } transaction)
        {
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
        }
    }
}
