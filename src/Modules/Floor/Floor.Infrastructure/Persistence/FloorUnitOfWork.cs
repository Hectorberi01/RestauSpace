using Floor.Application;
using Floor.Application.Abstractions;
using Floor.Domain;
using Floor.Infrastructure.Persistence.Configurations;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Floor.Infrastructure.Persistence;

internal sealed class FloorUnitOfWork(FloorDbContext context) : OutboxUnitOfWork<FloorDbContext>(context), IFloorUnitOfWork
{
    protected override DomainError ConcurrentUpdate => FloorApplicationErrors.ConcurrentUpdate;

    /// <summary>
    /// 1. Les tables gardent l'ordre de la liste de leur zone : leur position est recopiée avant l'écriture.
    /// 2. Ajouter, modifier ou retirer une table n'écrit que des lignes de tables. La ligne de la zone est aussi
    ///    réécrite, pour que sa version protège ce qui a été décidé d'après la zone telle que chargée (libellés uniques).
    /// </summary>
    protected override void BeforeSave()
    {
        foreach (var area in Context.ChangeTracker.Entries<DiningArea>().Where(entry => entry.State != EntityState.Deleted).ToList())
        {
            var position = 0;
            foreach (var table in area.Entity.Tables)
            {
                var property = Context.Entry(table).Property<int>(RestaurantTableConfiguration.PositionProperty);
                if (property.CurrentValue != position)
                {
                    property.CurrentValue = position;
                }

                position++;
            }
        }

        var areas = Context.ChangeTracker.Entries<RestaurantTable>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Property<DiningAreaId>(DiningAreaConfiguration.OwnerProperty))
            .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
            .ToHashSet();

        // Matérialisé : marquer une zone modifiée pendant le parcours du change tracker le modifierait.
        foreach (var area in Context.ChangeTracker.Entries<DiningArea>().ToList())
        {
            if (area.State == EntityState.Unchanged && areas.Contains(area.Entity.Id))
            {
                area.Property(candidate => candidate.Name).IsModified = true;
            }
        }
    }

    protected override Exception? Translate(DbUpdateException exception) =>
        (exception.InnerException as PostgresException) switch
        {
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: TableSessionConfiguration.OpenSessionPerTableIndex } =>
                new DomainException(TableSessionErrors.TableAlreadyOccupied),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: RestaurantTableConfiguration.AreaLabelIndex } =>
                new DomainException(DiningAreaErrors.TableLabelAlreadyUsed),
            _ => null,
        };
}
