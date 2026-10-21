using Loyalty.Application.Abstractions;
using Loyalty.Application.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loyalty.Infrastructure.Workers;

/// <summary>
/// Le job d'expiration des points : à chaque passage, pour chaque restaurant dont le barème fait expirer les points, le
/// solde des comptes inactifs expire (<see cref="ExpireInactivePointsCommand"/>). Chaque restaurant est traité dans son
/// propre scope, POUR son tenant (<see cref="BackgroundTenant"/>). Rejouable : une période d'inactivité n'expire qu'une
/// fois (référence au registre). Plusieurs instances peuvent tourner : un conflit de version fait seulement remettre le
/// lot au passage suivant. Journal : ids et compteurs seulement.
/// </summary>
public sealed class LoyaltyExpiryWorker(IServiceScopeFactory scopes, LoyaltyOptions options, ILogger<LoyaltyExpiryWorker> logger) : BackgroundService
{
    /// <summary>Un passage complet ; renvoie le nombre de comptes expirés, tous restaurants confondus.</summary>
    public async Task<int> ExpireAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantId> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<ILoyaltyProgramDirectory>().ListTenantsWithExpiryAsync(cancellationToken);
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
                    expired = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ExpireInactivePointsCommand, int>>()
                        .HandleAsync(new ExpireInactivePointsCommand(options.ExpiryBatchSize), cancellationToken);
                }
                catch (Exception exception) when (exception is ConcurrencyException or DomainException)
                {
                    logger.LogWarning("Loyalty expiry for tenant {TenantId} postponed: {Error}.", tenant.Value, exception.GetType().Name);
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
            logger.LogInformation("Loyalty expiry for tenant {TenantId}: {Count} account(s) expired.", tenant.Value, total);
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
                logger.LogError(exception, "Loyalty expiry pass failed; retrying at the next interval.");
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
