using Files.Application;
using Files.Application.Abstractions;
using Files.Application.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Files.Infrastructure.Workers;

/// <summary>
/// Le job du module : les uploads jamais confirmés sont supprimés (l'objet éventuellement envoyé l'est par la réaction à
/// <c>FileDeleted</c>). Un scope par fichier, POUR son tenant (<see cref="BackgroundTenant"/>). Journal : ids et compteurs.
/// </summary>
public sealed class AbandonedUploadsWorker(
    IServiceScopeFactory scopes, FilesOptions options, FilesSettings settings, IClock clock, ILogger<AbandonedUploadsWorker> logger) : BackgroundService
{
    /// <summary>Un passage ; renvoie le nombre de fichiers supprimés.</summary>
    public async Task<int> CleanUpAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AbandonedUpload> abandoned;
        await using (var scope = scopes.CreateAsyncScope())
        {
            abandoned = await scope.ServiceProvider.GetRequiredService<IAbandonedUploads>()
                .ListAsync(clock.UtcNow - settings.AbandonedUploadAfter, options.CleanupBatchSize, cancellationToken);
        }

        var deleted = 0;
        foreach (var upload in abandoned)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(upload.TenantId);
            try
            {
                if (await scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteAbandonedUploadCommand, bool>>()
                    .HandleAsync(new DeleteAbandonedUploadCommand(upload.FileId.Value), cancellationToken))
                {
                    deleted++;
                }
            }
            catch (Exception exception) when (exception is ConcurrencyException or DomainException)
            {
                logger.LogWarning("Abandoned upload {FileId} postponed: {Error}.", upload.FileId.Value, exception.GetType().Name);
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation("{Count} abandoned uploads deleted.", deleted);
        }

        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanUpAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Abandoned uploads pass failed; retrying at the next interval.");
            }

            try
            {
                await Task.Delay(options.CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
