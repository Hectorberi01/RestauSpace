using Kitchen.Application;
using Kitchen.Application.Abstractions;
using Kitchen.Domain;
using Kitchen.Infrastructure.Persistence.Configurations;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Kitchen.Infrastructure.Persistence;

internal sealed class KitchenUnitOfWork(KitchenDbContext context) : OutboxUnitOfWork<KitchenDbContext>(context), IKitchenUnitOfWork
{
    protected override DomainError ConcurrentUpdate => KitchenApplicationErrors.ConcurrentUpdate;

    /// <summary>
    /// 1. Les lignes d'un ticket gardent l'ordre de la commande : leur position est recopiée avant l'écriture.
    /// 2. Démarrer ou terminer une ligne n'écrit que la ligne. La ligne du ticket est aussi réécrite, pour que sa version
    ///    protège ce qui a été décidé d'après le ticket tel que chargé (deux postes qui agissent en même temps).
    /// </summary>
    protected override void BeforeSave()
    {
        foreach (var ticket in Context.ChangeTracker.Entries<KitchenTicket>().Where(entry => entry.State != EntityState.Deleted).ToList())
        {
            var position = 0;
            foreach (var item in ticket.Entity.Items)
            {
                var property = Context.Entry(item).Property<int>(KitchenTicketItemConfiguration.PositionProperty);
                if (property.CurrentValue != position)
                {
                    property.CurrentValue = position;
                }

                position++;
            }
        }

        var owners = Context.ChangeTracker.Entries<KitchenTicketItem>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Property<KitchenTicketId>(KitchenTicketConfiguration.OwnerProperty))
            .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
            .ToHashSet();

        // Matérialisé : marquer un ticket modifié pendant le parcours du change tracker le modifierait.
        foreach (var ticket in Context.ChangeTracker.Entries<KitchenTicket>().ToList())
        {
            if (ticket.State == EntityState.Unchanged && owners.Contains(ticket.Entity.Id))
            {
                ticket.Property(candidate => candidate.Status).IsModified = true;
            }
        }
    }

    protected override Exception? Translate(DbUpdateException exception) =>
        (exception.InnerException as PostgresException) switch
        {
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: KitchenStationConfiguration.LocationNameIndex } =>
                new DomainException(KitchenStationErrors.NameNotUnique),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: StationRoutingConfiguration.LocationIndex } =>
                new DomainException(StationRoutingErrors.LocationAlreadyHasRouting),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: KitchenTicketConfiguration.OrderIndex } =>
                new DomainException(KitchenApplicationErrors.TicketAlreadyExists),
            _ => null,
        };
}
