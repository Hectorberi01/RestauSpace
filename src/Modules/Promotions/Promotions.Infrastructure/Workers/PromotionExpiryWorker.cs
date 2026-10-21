using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Promotions.Application.Abstractions;
using Promotions.Application.Commands;

namespace Promotions.Infrastructure.Workers;

/// <summary>
/// Le job d'expiration : à chaque passage, les promotions dont la période est terminée passent à Expired, restaurant par
/// restaurant, chacun dans son propre scope POUR son tenant (<see cref="BackgroundTenant"/>). Le domaine refuse déjà toute
/// application hors période : le job range seulement les statuts. Plusieurs instances peuvent tourner : un conflit de
/// version remet le lot au passage suivant. Journal : ids et compteurs seulement.
/// </summary>
public sealed class PromotionExpiryWorker(
    IServiceScopeFactory scopes, PromotionsOptions options, IClock clock, ILogger<PromotionExpiryWorker> logger) : BackgroundService
{
    /// <summary>Un passage complet ; renvoie le nombre de promotions expirées, tous restaurants confondus.</summary>
    public async Task<int> ExpireAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantId> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<IPromotionExpiryDirectory>()
                .ListTenantsWithEndedPromotionsAsync(clock.UtcNow, cancellationToken);
        }

        var total = 0;
        foreach (var tenant in tenants)
        {
            total += await ExpireForAsync(tenant, cancellationToken);
        }

        return total;
    }

    /// <summary>Tous les lots d'un restaurant, jusqu'à un lot incomplet ou une erreur.</summary>
    public async Task<int> ExpireForAsync(TenantId tenant, CancellationToken cancellationToken = default)
    {
        var total = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            int expired;
            await using (var scope = scopes.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(tenant);
                try
                {
                    expired = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ExpireEndedPromotionsCommand, int>>()
                        .HandleAsync(new ExpireEndedPromotionsCommand(options.ExpiryBatchSize), cancellationToken);
                }
                catch (Exception exception) when (exception is ConcurrencyException or DomainException)
                {
                    logger.LogWarning("Promotion expiry for tenant {TenantId} postponed: {Error}.", tenant.Value, exception.GetType().Name);
                    return total;
                }
            }

            total += expired;
            if (expired < options.ExpiryBatchSize)
            {
                break;
            }
        }

        if (total > 0)
        {
            logger.LogInformation("Promotion expiry for tenant {TenantId}: {Count} promotion(s) expired.", tenant.Value, total);
        }

        return total;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireAllAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Promotion expiry pass failed; retrying at the next interval.");
            }

            try
            {
                await Task.Delay(options.ExpiryInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
