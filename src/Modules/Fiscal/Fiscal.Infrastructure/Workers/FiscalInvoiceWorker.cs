using Fiscal.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Fiscal.Infrastructure.Workers;

/// <summary>
/// File des factures normalisées : à chaque passe, les factures dues de tous les restaurants sont traitées une à une,
/// chacune dans un scope propre et pour son tenant. Une erreur sur l'une n'arrête pas les autres ; une passe ratée est
/// reprise à la suivante. Rien du contenu des factures ni du jeton n'est journalisé.
/// </summary>
public sealed class FiscalInvoiceWorker(IServiceScopeFactory scopes, FiscalOptions options, IClock clock, ILogger<FiscalInvoiceWorker> logger) : BackgroundService
{
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DueFiscalInvoice> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IDueFiscalInvoices>().ListAsync(clock.UtcNow, options.BatchSize, cancellationToken);
        }

        var processed = 0;
        foreach (var item in due)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(item.TenantId);
                var status = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ProcessFiscalInvoiceCommand, Fiscal.Domain.FiscalInvoiceStatus>>()
                    .HandleAsync(new ProcessFiscalInvoiceCommand(item.Id.Value), cancellationToken);
                logger.LogInformation("Fiscal invoice {InvoiceId} processed: {Status}.", item.Id.Value, status);
                processed++;
            }
            catch (Exception exception) when (exception is DomainException or ConcurrencyException or NotFoundException)
            {
                logger.LogWarning("Fiscal invoice {InvoiceId} not processed: {Code}.", item.Id.Value, (exception as DomainException)?.Error.Code ?? exception.GetType().Name);
            }
        }

        return processed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The fiscal invoices pass failed; it will be retried.");
            }

            try
            {
                await Task.Delay(options.WorkerInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
