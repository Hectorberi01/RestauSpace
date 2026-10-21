using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Reservations.Application.Abstractions;
using Reservations.Application.Commands;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Reservations.Infrastructure.Workers;

/// <summary>
/// Toutes les minutes : annule les réservations dont l'acompte n'a pas été payé à temps (les places sont rendues), libère
/// les offres de la liste d'attente expirées et ferme les inscriptions dont l'heure est passée. Puis propose les places
/// libérées aux suivants, une fois par établissement touché. Chaque élément est traité dans un scope propre, pour son
/// tenant ; une erreur sur l'un n'arrête pas les autres.
/// </summary>
public sealed class ReservationsDueWorker(IServiceScopeFactory scopes, ReservationsOptions options, IClock clock, ILogger<ReservationsDueWorker> logger)
    : BackgroundService
{
    public async Task<int> ResolveAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DueItem> deposits;
        IReadOnlyList<DueItem> waitlist;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var due = scope.ServiceProvider.GetRequiredService<IReservationsDueWork>();
            deposits = await due.UnpaidDepositsAsync(clock.UtcNow, options.DueBatchSize, cancellationToken);
            waitlist = await due.WaitlistAsync(clock.UtcNow, options.DueBatchSize, cancellationToken);
        }

        var resolved = 0;
        var touched = new HashSet<(TenantId, LocationId)>();
        foreach (var item in deposits)
        {
            if (await RunAsync(item, new ExpireUnpaidDepositCommand(item.Id), cancellationToken))
            {
                resolved++;
                touched.Add((item.TenantId, item.LocationId));
            }
        }

        foreach (var item in waitlist)
        {
            if (await RunAsync(item, new ResolveWaitlistEntryCommand(item.Id), cancellationToken))
            {
                resolved++;
                touched.Add((item.TenantId, item.LocationId));
            }
        }

        foreach (var (tenant, location) in touched)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(tenant);
                await scope.ServiceProvider.GetRequiredService<ICommandHandler<OfferFreedCapacityCommand, int>>()
                    .HandleAsync(new OfferFreedCapacityCommand(location.Value), cancellationToken);
            }
            catch (Exception exception) when (exception is DomainException or ConcurrencyException or NotFoundException)
            {
                logger.LogWarning("Freed tables of location {LocationId} could not be offered: {Code}.", location, (exception as DomainException)?.Error.Code ?? exception.GetType().Name);
            }
        }

        return resolved;
    }

    private async Task<bool> RunAsync<TCommand>(DueItem item, TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(item.TenantId);
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is DomainException or ConcurrencyException or NotFoundException)
        {
            logger.LogWarning("Due reservation item {Id} could not be resolved: {Code}.", item.Id, (exception as DomainException)?.Error.Code ?? exception.GetType().Name);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var resolved = await ResolveAsync(stoppingToken);
                if (resolved > 0)
                {
                    logger.LogInformation("{Count} due reservation items resolved.", resolved);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The reservations due pass failed; it will be retried.");
            }

            try
            {
                await Task.Delay(options.DueInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
