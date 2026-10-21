using System.Text.Json;
using Files.Application.Abstractions;
using Files.Domain;
using Microsoft.Extensions.Logging;

namespace Files.Infrastructure.Storage;

/// <summary>
/// Réaction du module à son propre événement <c>files.FileDeleted</c> (par l'outbox, après commit, au moins une fois) :
/// l'objet est effacé du stockage. Idempotent (effacer un objet absent n'est pas une erreur) ; un échec du stockage
/// remonte pour que le dispatcher retente. Journal : id du fichier seulement.
/// </summary>
internal sealed class StoredObjectCleanup(IObjectStorage storage, ILogger<StoredObjectCleanup> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not (DependencyInjection.Schema, nameof(FileDeleted)))
        {
            return;
        }

        using var content = JsonDocument.Parse(message.Content);
        var root = content.RootElement;
        await storage.DeleteAsync(StorageKey.Create(root.GetProperty("StorageKey").GetString()), cancellationToken);
        logger.LogInformation("Stored object of file {FileId} deleted.", root.GetProperty("FileId").GetGuid());
    }
}
