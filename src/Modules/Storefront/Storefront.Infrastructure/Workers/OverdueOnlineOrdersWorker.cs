using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaasRestaurant.BuildingBlocks.Infrastructure;
using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;

namespace Storefront.Infrastructure.Workers;

/// <summary>
/// Toutes les minutes : annule les commandes payées en ligne restées impayées, et refuse celles en validation manuelle que
/// personne n'a acceptées, une fois leur échéance passée (le client est prévenu, et remboursé s'il avait payé). Une
/// passe lit les échéances dépassées de tous les restaurants, puis traite chaque commande dans un scope propre, pour le
/// tenant de la commande. Une erreur sur l'une n'arrête pas les autres ; une passe ratée est reprise à la suivante.
/// </summary>
public sealed class OverdueOnlineOrdersWorker(IServiceScopeFactory scopes, StorefrontOptions options, IClock clock, ILogger<OverdueOnlineOrdersWorker> logger)
    : BackgroundService
{
    public async Task<int> ResolveAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<OverdueOnlineOrder> overdue;
        await using (var scope = scopes.CreateAsyncScope())
        {
            overdue = await scope.ServiceProvider.GetRequiredService<IOverdueOnlineOrders>().ListAsync(clock.UtcNow, options.OverdueBatchSize, cancellationToken);
        }

        var resolved = 0;
        foreach (var item in overdue)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(item.TenantId);
                await scope.ServiceProvider.GetRequiredService<ICommandHandler<ResolveOverdueOnlineOrderCommand>>()
                    .HandleAsync(new ResolveOverdueOnlineOrderCommand(item.Id.Value), cancellationToken);
                resolved++;
            }
            catch (Exception exception) when (exception is DomainException or ConcurrencyException or NotFoundException)
            {
                logger.LogWarning("Overdue online order {OnlineOrderId} could not be resolved: {Code}.", item.Id, (exception as DomainException)?.Error.Code ?? exception.GetType().Name);
            }
        }

        return resolved;
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
                    logger.LogInformation("{Count} overdue online orders resolved.", resolved);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The overdue online orders pass failed; it will be retried.");
            }

            try
            {
                await Task.Delay(options.OverdueCheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
