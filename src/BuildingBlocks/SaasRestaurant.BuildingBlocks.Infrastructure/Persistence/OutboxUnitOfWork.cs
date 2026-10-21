namespace SaasRestaurant.BuildingBlocks.Infrastructure;

/// <summary>
/// Commite les agrégats suivis et leurs domain events (sous forme de lignes d'outbox) en une seule transaction. Un
/// agrégat modifié par quelqu'un d'autre depuis son chargement donne une <see cref="ConcurrencyException"/>.
/// </summary>
public abstract class OutboxUnitOfWork<TContext>(TContext context) : IUnitOfWork
    where TContext : DbContext
{
    protected TContext Context => context;

    protected abstract DomainError ConcurrentUpdate { get; }

    protected virtual void BeforeSave()
    {
    }

    protected virtual IEnumerable<IDomainEvent> DerivedEvents(IReadOnlyList<IAggregateRoot> changed) => [];

    /// <summary>Permet à un module de traduire un refus de la base (index unique...) en sa propre erreur métier.</summary>
    protected virtual Exception? Translate(DbUpdateException exception) => null;

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        BeforeSave();

        var aggregates = context.ChangeTracker.Entries<IAggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();
        var messages = aggregates.SelectMany(aggregate => aggregate.DomainEvents)
            .Concat(DerivedEvents(aggregates))
            .Select(OutboxMessage.From)
            .ToList();
        context.Set<OutboxMessage>().AddRange(messages);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Les événements sont toujours sur les agrégats : une nouvelle tentative ne doit pas les écrire deux fois.
            foreach (var message in messages)
            {
                context.Entry(message).State = EntityState.Detached;
            }

            if (exception is DbUpdateConcurrencyException)
            {
                throw new ConcurrencyException(ConcurrentUpdate, exception);
            }

            if (exception is DbUpdateException refused && Translate(refused) is { } translated)
            {
                throw translated;
            }

            throw;
        }

        aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());
    }
}
