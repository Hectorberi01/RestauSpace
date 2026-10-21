using Files.Domain;

namespace Files.Application.Commands;

/// <summary>
/// Supprime un fichier du restaurant du contexte. Suppression logique immédiate (plus jamais servi) ; l'objet est
/// effacé du stockage par la réaction à <see cref="FileDeleted"/> (par l'outbox, après commit).
/// </summary>
public sealed record DeleteFileCommand(Guid FileId) : ICommand;

public sealed class DeleteFileHandler(FilesWorkspace workspace, IClock clock) : ICommandHandler<DeleteFileCommand>
{
    public async Task HandleAsync(DeleteFileCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var file = await workspace.LoadAsync(command.FileId, cancellationToken);
        file.Delete(clock);
        // Les miniatures de la photo partent avec elle.
        foreach (var thumbnail in (await workspace.Files.ListByOwnerAsync(workspace.CurrentTenant, file.Owner, cancellationToken))
            .Where(other => other.ThumbnailOf == file.Id && other.Status != MediaFileStatus.Deleted))
        {
            thumbnail.Delete(clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Pour l'hôte (au nom d'un autre module, ex. un export de Reporting qui expire) : même suppression, sans exigence
/// d'écriture du restaurant. Déjà supprimé ou inconnu = rien à faire.
/// </summary>
public sealed record DeleteOwnedFileCommand(Guid FileId) : ICommand<bool>;

public sealed class DeleteOwnedFileHandler(FilesWorkspace workspace, IClock clock) : ICommandHandler<DeleteOwnedFileCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteOwnedFileCommand command, CancellationToken cancellationToken = default)
    {
        var file = command.FileId == Guid.Empty
            ? null
            : await workspace.Files.FindAsync(workspace.CurrentTenant, FileId.From(command.FileId), cancellationToken);
        if (file is null or { Status: MediaFileStatus.Deleted })
        {
            return false;
        }

        file.Delete(clock);
        await workspace.SaveAsync(cancellationToken);
        return true;
    }
}

/// <summary>
/// Job : un upload jamais confirmé (encore en attente après <see cref="FilesSettings.AbandonedUploadAfter"/>) est supprimé,
/// l'objet éventuellement envoyé avec lui.
/// </summary>
public sealed record DeleteAbandonedUploadCommand(Guid FileId) : ICommand<bool>;

public sealed class DeleteAbandonedUploadHandler(FilesWorkspace workspace, IClock clock) : ICommandHandler<DeleteAbandonedUploadCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteAbandonedUploadCommand command, CancellationToken cancellationToken = default)
    {
        var file = command.FileId == Guid.Empty
            ? null
            : await workspace.Files.FindAsync(workspace.CurrentTenant, FileId.From(command.FileId), cancellationToken);
        if (file is not { Status: MediaFileStatus.Pending } || file.CreatedAtUtc + workspace.Settings.AbandonedUploadAfter > clock.UtcNow)
        {
            return false;
        }

        file.Delete(clock);
        await workspace.SaveAsync(cancellationToken);
        return true;
    }
}
