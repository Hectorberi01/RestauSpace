using Npgsql;
using Orders.Application;
using Orders.Application.Abstractions;
using Orders.Domain;
using Orders.Infrastructure.Persistence.Configurations;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Orders.Infrastructure.Persistence;

internal sealed class OrdersUnitOfWork(OrdersDbContext context) : OutboxUnitOfWork<OrdersDbContext>(context), IOrdersUnitOfWork
{
    protected override DomainError ConcurrentUpdate => OrdersApplicationErrors.ConcurrentUpdate;

    /// <summary>
    /// 1. Les lignes gardent leur ordre d'ajout : leur position est recopiée depuis la liste de la commande.
    /// 2. Une modification qui ne touche qu'une ligne (sa note) réécrit aussi la commande, pour que sa version protège ce
    ///    qui a été décidé d'après elle (brouillon encore modifiable).
    /// </summary>
    protected override void BeforeSave()
    {
        foreach (var order in Context.ChangeTracker.Entries<Order>().Where(entry => entry.State != EntityState.Deleted).ToList())
        {
            var position = 0;
            foreach (var item in order.Entity.Items)
            {
                var property = Context.Entry(item).Property<int>(OrderItemConfiguration.PositionProperty);
                if (property.CurrentValue != position)
                {
                    property.CurrentValue = position;
                }

                position++;
            }
        }

        var owners = Context.ChangeTracker.Entries<OrderItem>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Property<OrderId>(OrderConfiguration.OwnerProperty))
            .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
            .ToHashSet();

        // Matérialisé : marquer une commande modifiée pendant le parcours du change tracker le modifierait.
        foreach (var order in Context.ChangeTracker.Entries<Order>().ToList())
        {
            if (order.State == EntityState.Unchanged && owners.Contains(order.Entity.Id))
            {
                order.Property(candidate => candidate.Status).IsModified = true;
            }
        }
    }

    protected override Exception? Translate(DbUpdateException exception) =>
        (exception.InnerException as PostgresException) switch
        {
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OrderConfiguration.LocationNumberIndex } =>
                new DomainException(OrdersApplicationErrors.NumberAlreadyUsed),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OrderConfiguration.PlatformReferenceIndex } =>
                new DomainException(OrderErrors.ExternalReferenceDuplicate),
            _ => null,
        };
}
