using Customers.Application;
using Customers.Application.Abstractions;
using Customers.Domain;
using Customers.Infrastructure.Persistence.Configurations;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Customers.Infrastructure.Persistence;

internal sealed class CustomersUnitOfWork(CustomersDbContext context) : OutboxUnitOfWork<CustomersDbContext>(context), ICustomersUnitOfWork
{
    protected override DomainError ConcurrentUpdate => CustomersApplicationErrors.ConcurrentUpdate;

    /// <summary>
    /// Ajouter ou retirer un tag n'écrit que la ligne du tag. La fiche est aussi réécrite, pour que sa version protège la
    /// décision prise d'après la fiche telle que chargée (deux ajouts simultanés ne dépassent pas le maximum de tags).
    /// </summary>
    protected override void BeforeSave()
    {
        var owners = Context.ChangeTracker.Entries<CustomerTag>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Property<CustomerId>(CustomerConfiguration.OwnerProperty))
            .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
            .ToHashSet();

        // Matérialisé : marquer une fiche modifiée pendant le parcours du change tracker le modifierait.
        foreach (var customer in Context.ChangeTracker.Entries<Customer>().ToList())
        {
            if (customer.State == EntityState.Unchanged && owners.Contains(customer.Entity.Id))
            {
                customer.Property(candidate => candidate.Name).IsModified = true;
            }
        }
    }

    /// <summary>L'index unique (client, canal), garde-fou derrière le contrôle fait avant d'écrire (deux octrois simultanés).</summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        (exception.InnerException as PostgresException) switch
        {
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ConsentConfiguration.CustomerChannelIndex } =>
                new DomainException(ConsentErrors.NotUnique),
            _ => null,
        };
}
