using Locations.Application;
using Locations.Application.Abstractions;
using Locations.Domain.Locations;
using Locations.Infrastructure.Persistence.Configurations;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Locations.Infrastructure.Persistence;

internal sealed class LocationsUnitOfWork(LocationsDbContext context): OutboxUnitOfWork<LocationsDbContext>(context), ILocationsUnitOfWork
{
    protected override DomainError ConcurrentUpdate => LocationsApplicationErrors.ConcurrentUpdate;

    /// <summary>
    /// Ajouter ou retirer une exception n'écrit qu'une ligne d'exception. La ligne de l'établissement est aussi
    /// touchée, pour que sa version protège ce qui a été décidé d'après l'établissement chargé (une exception par date).
    /// </summary>
    protected override void BeforeSave()
    {
        var owners = Context.ChangeTracker.Entries<ExceptionalSchedule>()
            .Where(schedule => schedule.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(schedule => schedule.Property<LocationId>(ExceptionalScheduleConfiguration.LocationIdProperty))
            .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
            .ToHashSet();

        foreach (var location in Context.ChangeTracker.Entries<RestaurantLocation>())
        {
            if (location.State == EntityState.Unchanged && owners.Contains(location.Entity.Id))
            {
                location.Property(candidate => candidate.CreatedAtUtc).IsModified = true;
            }
        }
    }

    protected override Exception? Translate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ExceptionalScheduleConfiguration.LocationDateIndex,
        }
            ? new DomainException(LocationErrors.ExceptionalDateAlreadyDefined)
            : null;
}
