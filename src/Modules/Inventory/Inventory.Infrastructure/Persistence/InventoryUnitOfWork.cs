using Inventory.Application;
using Inventory.Application.Abstractions;
using Inventory.Domain;
using Inventory.Infrastructure.Persistence.Configurations;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Inventory.Infrastructure.Persistence;

internal sealed class InventoryUnitOfWork(InventoryDbContext context) : OutboxUnitOfWork<InventoryDbContext>(context), IInventoryUnitOfWork
{
    protected override DomainError ConcurrentUpdate => InventoryApplicationErrors.ConcurrentUpdate;

    /// <summary>
    /// Changer une ligne de recette n'écrit que la ligne. La recette est aussi réécrite, pour que sa version protège ce
    /// qui a été décidé d'après la recette telle que chargée (deux modifications simultanées).
    /// </summary>
    protected override void BeforeSave()
    {
        var owners = Context.ChangeTracker.Entries<RecipeComponent>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Property<RecipeId>(RecipeConfiguration.OwnerProperty))
            .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
            .ToHashSet();

        // Matérialisé : marquer une recette modifiée pendant le parcours du change tracker le modifierait.
        foreach (var recipe in Context.ChangeTracker.Entries<Recipe>().ToList())
        {
            if (recipe.State == EntityState.Unchanged && owners.Contains(recipe.Entity.Id))
            {
                recipe.Property(candidate => candidate.ProductId).IsModified = true;
            }
        }
    }

    /// <summary>Les index uniques, garde-fous derrière les contrôles faits avant d'écrire (deux requêtes simultanées).</summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        (exception.InnerException as PostgresException) switch
        {
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IngredientConfiguration.NameIndex } =>
                new DomainException(InventoryErrors.IngredientNameNotUnique),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: InventoryItemConfiguration.LocationIngredientIndex } =>
                new DomainException(InventoryErrors.ItemNotUniqueForLocation),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: RecipeConfiguration.ProductIndex or RecipeConfiguration.VariantIndex } =>
                new DomainException(InventoryErrors.RecipeNotUniqueForProduct),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OrderConsumptionConfiguration.Key } =>
                new DomainException(InventoryApplicationErrors.OrderAlreadyConsumed),
            _ => null,
        };
}
