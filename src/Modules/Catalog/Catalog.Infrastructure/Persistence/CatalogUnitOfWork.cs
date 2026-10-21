using Catalog.Application;
using Catalog.Application.Abstractions;
using Catalog.Domain;
using Catalog.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Catalog.Infrastructure.Persistence;

internal sealed class CatalogUnitOfWork(CatalogDbContext context, IClock clock) : OutboxUnitOfWork<CatalogDbContext>(context), ICatalogUnitOfWork
{
    protected override DomainError ConcurrentUpdate => CatalogApplicationErrors.ConcurrentUpdate;

    protected override IEnumerable<IDomainEvent> DerivedEvents(IReadOnlyList<IAggregateRoot> changed)
    {
        var priced = Context.ChangeTracker.Entries<Product>().Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity.TenantId)
            .Concat(Context.ChangeTracker.Entries<ModifierGroup>().Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity.TenantId))
            .Distinct()
            .Select(tenantId => (IDomainEvent)new CatalogPriced(tenantId, clock.UtcNow));

        // Un enfant modifié a déjà marqué sa racine (BeforeSave) : il suffit de regarder les racines.
        var edited = Context.ChangeTracker.Entries<Product>().Where(IsWritten).Select(entry => entry.Entity.TenantId)
            .Concat(Context.ChangeTracker.Entries<ModifierGroup>().Where(IsWritten).Select(entry => entry.Entity.TenantId))
            .Concat(Context.ChangeTracker.Entries<Menu>().Where(IsWritten).Select(entry => entry.Entity.TenantId))
            .Distinct()
            .Select(tenantId => (IDomainEvent)new CatalogChanged(tenantId, clock.UtcNow));

        return priced.Concat(edited).ToList();
    }

    private static bool IsWritten(EntityEntry entry) => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    /// <summary>
    /// 1. Les variantes, options et sections sont des listes ordonnées : leur position en base est recopiée depuis
    ///    l'ordre de la liste de l'agrégat.
    /// 2. Ajouter, modifier, déplacer ou retirer un enfant n'écrit que des lignes d'enfants. La ligne de l'agrégat est
    ///    aussi réécrite, pour que sa version protège ce qui a été décidé d'après l'agrégat tel que chargé (noms
    ///    uniques, bornes, ordre).
    /// </summary>
    protected override void BeforeSave()
    {
        foreach (var product in Context.ChangeTracker.Entries<Product>().Where(entry => entry.State != EntityState.Deleted).ToList())
        {
            SetPositions(product.Entity.Variants);
        }

        foreach (var group in Context.ChangeTracker.Entries<ModifierGroup>().Where(entry => entry.State != EntityState.Deleted).ToList())
        {
            SetPositions(group.Entity.Modifiers);
        }

        foreach (var menu in Context.ChangeTracker.Entries<Menu>().Where(entry => entry.State != EntityState.Deleted).ToList())
        {
            SetPositions(menu.Entity.Sections);
        }

        Touch<ProductVariant, Product, ProductId>(ProductConfiguration.OwnerProperty, product => product.Id, nameof(Product.Status));
        Touch<Modifier, ModifierGroup, ModifierGroupId>(ModifierGroupConfiguration.OwnerProperty, group => group.Id, nameof(ModifierGroup.Name));
        Touch<MenuSection, Menu, MenuId>(MenuConfiguration.OwnerProperty, menu => menu.Id, nameof(Menu.Status));
    }

    protected override Exception? Translate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: TaxCategoryConfiguration.TenantCodeIndex,
        }
            ? new DomainException(TaxCategoryErrors.CodeNotUnique)
            : null;

    private void SetPositions<TChild>(IEnumerable<TChild> children)
        where TChild : class
    {
        var position = 0;
        foreach (var child in children)
        {
            var property = Context.Entry(child).Property<int>(Ordering.PositionProperty);
            if (property.CurrentValue != position)
            {
                property.CurrentValue = position;
            }

            position++;
        }
    }

    private void Touch<TChild, TRoot, TRootId>(
        string ownerProperty,
        Func<TRoot, TRootId> idOf,
        string guardedProperty)
        where TChild : class
        where TRoot : class
        where TRootId : notnull
    {
        var owners = Context.ChangeTracker.Entries<TChild>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Property<TRootId>(ownerProperty))
            .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
            .ToHashSet();

        // Matérialisé : marquer une racine modifiée pendant le parcours du change tracker le modifierait.
        foreach (var root in Context.ChangeTracker.Entries<TRoot>().ToList())
        {
            if (root.State == EntityState.Unchanged && owners.Contains(idOf(root.Entity)))
            {
                root.Property(guardedProperty).IsModified = true;
            }
        }
    }
}
