using Billing.Application.Abstractions;
using Billing.Application.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Billing.Infrastructure.Workers;

/// <summary>
/// Le job d'échéance des abonnements : à chaque passage, les abonnements dont la période est terminée sont traités un par
/// un (<see cref="ProcessDueSubscriptionCommand"/>), chacun dans son propre scope POUR son tenant
/// (<see cref="BackgroundTenant"/>). Les suites côté restaurant (suspension, réactivation) partent par l'outbox. Plusieurs
/// instances peuvent tourner : un conflit de version remet l'abonnement au passage suivant. Journal : ids et compteurs.
/// Chaque passage traite aussi les options offertes échues et relit les règlements en ligne en attente chez le prestataire
/// (<see cref="SettleSubscriptionCheckoutCommand"/>).
/// </summary>
public sealed class SubscriptionDueWorker(
    IServiceScopeFactory scopes, BillingOptions options, IClock clock, ILogger<SubscriptionDueWorker> logger) : BackgroundService
{
    /// <summary>Un passage ; renvoie le nombre d'abonnements dont l'état a changé.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DueSubscription> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IPlatformSubscriptions>().ListDueAsync(clock.UtcNow, options.DueBatchSize, cancellationToken);
        }

        var changed = 0;
        foreach (var subscription in due)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(subscription.TenantId);
            try
            {
                var outcome = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ProcessDueSubscriptionCommand, SubscriptionDueOutcome>>()
                    .HandleAsync(new ProcessDueSubscriptionCommand(subscription.SubscriptionId.Value), cancellationToken);
                if (outcome != SubscriptionDueOutcome.Nothing)
                {
                    changed++;
                    logger.LogInformation(
                        "Subscription {SubscriptionId} of tenant {TenantId}: {Outcome}.", subscription.SubscriptionId.Value, subscription.TenantId.Value, outcome);
                }
            }
            catch (Exception exception) when (exception is ConcurrencyException or DomainException)
            {
                logger.LogWarning(
                    "Subscription {SubscriptionId} postponed: {Error}.", subscription.SubscriptionId.Value, exception.GetType().Name);
            }
        }

        return changed;
    }

    /// <summary>Un passage sur les préavis de fin d'essai ou d'offre ; renvoie le nombre de restaurants prévenus.</summary>
    public async Task<int> NoticeEndingAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DueSubscription> ending;
        await using (var scope = scopes.CreateAsyncScope())
        {
            ending = await scope.ServiceProvider.GetRequiredService<IPlatformSubscriptions>()
                .ListEndingAsync(clock.UtcNow, Billing.Domain.SaasSubscription.EndingNoticeWindow, options.DueBatchSize, cancellationToken);
        }

        var noticed = 0;
        foreach (var subscription in ending)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(subscription.TenantId);
            try
            {
                if (await scope.ServiceProvider.GetRequiredService<ICommandHandler<NoticeSubscriptionEndingCommand, bool>>()
                    .HandleAsync(new NoticeSubscriptionEndingCommand(subscription.SubscriptionId.Value), cancellationToken))
                {
                    noticed++;
                }
            }
            catch (Exception exception) when (exception is ConcurrencyException or DomainException)
            {
                logger.LogWarning("Subscription {SubscriptionId} ending notice postponed: {Error}.", subscription.SubscriptionId.Value, exception.GetType().Name);
            }
        }

        return noticed;
    }

    /// <summary>Un passage sur les options offertes échues (renouvelées, arrêtées ou rendues payantes) ; renvoie leur nombre.</summary>
    public async Task<int> ProcessAddOnOffersAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(TenantId TenantId, Billing.Domain.TenantAddOnId Id)> ended;
        await using (var scope = scopes.CreateAsyncScope())
        {
            ended = await scope.ServiceProvider.GetRequiredService<ITenantAddOnRepository>()
                .ListComplimentaryEndedAsync(clock.UtcNow, options.DueBatchSize, cancellationToken);
        }

        var changed = 0;
        foreach (var (tenantId, addOnId) in ended)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(tenantId);
            try
            {
                if (await scope.ServiceProvider.GetRequiredService<ICommandHandler<ProcessAddOnOfferCommand, bool>>()
                    .HandleAsync(new ProcessAddOnOfferCommand(addOnId.Value), cancellationToken))
                {
                    changed++;
                    logger.LogInformation("Add-on offer {AddOnId} of tenant {TenantId} processed.", addOnId.Value, tenantId.Value);
                }
            }
            catch (Exception exception) when (exception is ConcurrencyException or DomainException)
            {
                logger.LogWarning("Add-on offer {AddOnId} postponed: {Error}.", addOnId.Value, exception.GetType().Name);
            }
        }

        return changed;
    }

    /// <summary>Un passage sur les règlements en ligne ; renvoie le nombre de règlements clos.</summary>
    public async Task<int> SettleCheckoutsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(TenantId TenantId, Billing.Domain.SubscriptionCheckoutId Id)> pending;
        await using (var scope = scopes.CreateAsyncScope())
        {
            pending = await scope.ServiceProvider.GetRequiredService<ISubscriptionCheckoutRepository>().ListPendingAsync(options.CheckoutBatchSize, cancellationToken);
        }

        var closed = 0;
        foreach (var (tenantId, checkoutId) in pending)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(tenantId);
            try
            {
                var status = await scope.ServiceProvider.GetRequiredService<ICommandHandler<SettleSubscriptionCheckoutCommand, Billing.Domain.SubscriptionCheckoutStatus>>()
                    .HandleAsync(new SettleSubscriptionCheckoutCommand(tenantId.Value, checkoutId.Value), cancellationToken);
                if (status != Billing.Domain.SubscriptionCheckoutStatus.Pending)
                {
                    closed++;
                    logger.LogInformation("Subscription checkout {CheckoutId} of tenant {TenantId}: {Status}.", checkoutId.Value, tenantId.Value, status);
                }
            }
            catch (Exception exception) when (exception is ConcurrencyException or DomainException)
            {
                logger.LogWarning("Subscription checkout {CheckoutId} postponed: {Error}.", checkoutId.Value, exception.GetType().Name);
            }
        }

        return closed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
                await NoticeEndingAsync(stoppingToken);
                await ProcessAddOnOffersAsync(stoppingToken);
                await SettleCheckoutsAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Subscription due pass failed; retrying at the next interval.");
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
