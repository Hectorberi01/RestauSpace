using Notifications.Application;
using Notifications.Application.Abstractions;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence.Configurations;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Notifications.Infrastructure.Persistence;

/// <summary>
/// Unit of work du module : une transaction sur son schéma, les domain events écrits dans son outbox dans la même
/// transaction. Un conflit de version (deux écritures simultanées du même agrégat, deux workers sur la même
/// notification) devient <c>notifications.concurrent_update</c>.
/// </summary>
internal sealed class NotificationsUnitOfWork(NotificationsDbContext context)
    : OutboxUnitOfWork<NotificationsDbContext>(context), INotificationsUnitOfWork
{
    protected override DomainError ConcurrentUpdate => NotificationsApplicationErrors.ConcurrentUpdate;

    /// <summary>Les index uniques, garde-fous derrière les contrôles faits avant d'écrire (deux requêtes simultanées).</summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        (exception.InnerException as PostgresException) switch
        {
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: NotificationProviderConfiguration.CodeIndex } =>
                new DomainException(NotificationProviderErrors.CodeNotUnique),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: RestaurantNotificationConfigurationConfiguration.ProviderIndex } =>
                new DomainException(NotificationConfigurationErrors.AlreadyExists),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: NotificationTemplateConfiguration.VersionIndex } =>
                new DomainException(TemplateErrors.VersionAlreadyExists),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: NotificationPreferenceConfiguration.OwnerIndex } =>
                new DomainException(NotificationPreferenceErrors.AlreadyExists),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: NotificationConfiguration.IdempotencyIndex } =>
                new DomainException(NotificationsApplicationErrors.DuplicateRequest),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: InboxMessageConfiguration.PrimaryKey } =>
                new DomainException(NotificationsApplicationErrors.DuplicateEvent),
            _ => null,
        };

    /// <summary>
    /// 1. Tentatives et affectations gardent leur ordre : leur position est recopiée avant l'écriture.
    /// 2. Chaque enfant porte le tenant de sa racine (colonne <c>tenant_id</c> des tables enfants).
    /// 3. Un changement qui ne touche qu'une affectation réécrit aussi la configuration, pour que sa version protège
    ///    la décision prise d'après l'agrégat tel que chargé.
    /// </summary>
    protected override void BeforeSave()
    {
        foreach (var notification in Context.ChangeTracker.Entries<Notification>().Where(entry => entry.State != EntityState.Deleted).ToList())
        {
            var position = 0;
            foreach (var delivery in notification.Entity.Deliveries)
            {
                var entry = Context.Entry(delivery);
                Set(entry.Property<int>(NotificationDeliveryConfiguration.PositionProperty), position++);
                Set(entry.Property<TenantId?>(ChildTenant.Property), notification.Entity.TenantId);
            }
        }

        foreach (var configuration in Context.ChangeTracker.Entries<RestaurantNotificationConfiguration>()
            .Where(entry => entry.State != EntityState.Deleted)
            .ToList())
        {
            var position = 0;
            foreach (var assignment in configuration.Entity.Assignments)
            {
                var entry = Context.Entry(assignment);
                Set(entry.Property<int>(NotificationChannelAssignmentConfiguration.PositionProperty), position++);
                Set(entry.Property<TenantId>(ChildTenant.Property), configuration.Entity.TenantId);
            }

            if (configuration.State == EntityState.Unchanged && configuration.Entity.Assignments.Any(
                assignment => Context.Entry(assignment).State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                configuration.Property(candidate => candidate.UpdatedAtUtc).IsModified = true;
            }
        }

        foreach (var preference in Context.ChangeTracker.Entries<NotificationPreference>().Where(entry => entry.State != EntityState.Deleted).ToList())
        {
            foreach (var choice in preference.Entity.Choices)
            {
                Set(Context.Entry(choice).Property<TenantId>(ChildTenant.Property), preference.Entity.TenantId);
            }
        }
    }

    private static void Set<TEntity, T>(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<TEntity, T> property, T value)
        where TEntity : class
    {
        if (!EqualityComparer<T>.Default.Equals(property.CurrentValue, value))
        {
            property.CurrentValue = value;
        }
    }
}
