using Files.Application.Abstractions;
using Files.Application.Storage;
using Files.Domain;

namespace Files.Application.Commands;

// Téléversement direct vers le stockage : (1) le personnel déclare le fichier et reçoit une URL d'envoi signée ; (2) le
// navigateur envoie les octets au stockage, sans passer par l'API ; (3) le personnel confirme, et le serveur vérifie ce
// que le stockage a RÉELLEMENT reçu (taille, type reconnu aux premiers octets, antivirus) avant de le rendre disponible.

/// <summary>Ce qu'il faut pour envoyer les octets : l'URL signée et les en-têtes à reprendre tels quels.</summary>
public sealed record UploadTicket(Guid FileId, Uri UploadUrl, string Method, IReadOnlyDictionary<string, string> Headers, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Déclare un fichier pour un propriétaire du restaurant du contexte (produit, établissement, logo). Type et taille
/// contrôlés par la politique du type de propriétaire ; propriétaire vérifié dans le tenant.
/// </summary>
/// <param name="ThumbnailOf">La photo (même propriétaire) dont ce fichier est la miniature ; null pour une photo.</param>
public sealed record RequestUploadCommand(string? OwnerType, Guid OwnerId, string? FileName, string? ContentType, long SizeInBytes, Guid? ThumbnailOf = null)
    : ICommand<UploadTicket>;

public sealed class RequestUploadHandler(FilesWorkspace workspace, IClock clock) : ICommandHandler<RequestUploadCommand, UploadTicket>
{
    public async Task<UploadTicket> HandleAsync(RequestUploadCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var owner = FileOwner.Create(command.OwnerType, command.OwnerId);
        var ownerType = FileOwnerTypes.RequireUploadable(owner);
        var fileName = Domain.FileName.Create(command.FileName);
        var contentType = Domain.ContentType.Create(command.ContentType);
        if (!await workspace.Owners.BelongsToTenantAsync(tenant, owner, cancellationToken))
        {
            throw new NotFoundException(FilesApplicationErrors.OwnerNotFound);
        }

        MediaFile file;
        if (command.ThumbnailOf is { } originalId)
        {
            var original = await workspace.LoadAsync(originalId, cancellationToken);
            DomainException.ThrowIf(original.Owner != owner, FilesApplicationErrors.ThumbnailOwnerMismatch);
            file = MediaFile.CreateThumbnailPending(
                original, fileName, contentType, command.SizeInBytes, FileOwnerTypes.NewStorageKey(tenant, owner), ownerType.Policy, clock);
        }
        else
        {
            file = MediaFile.CreatePending(
                tenant, owner, fileName, contentType, command.SizeInBytes, FileOwnerTypes.NewStorageKey(tenant, owner), ownerType.Policy, clock);
        }

        workspace.Files.Add(file);
        await workspace.SaveAsync(cancellationToken);

        var lifetime = workspace.Settings.UploadUrlLifetime;
        var url = workspace.Storage.PresignUpload(file.StorageKey, file.ContentType, file.SizeInBytes, lifetime);
        return new UploadTicket(
            file.Id.Value,
            url,
            "PUT",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = file.ContentType.Value },
            clock.UtcNow + lifetime);
    }
}

public enum UploadOutcome
{
    Available = 1,
    Quarantined = 2,
}

public sealed record UploadResult(Guid FileId, UploadOutcome Outcome, string? Reason);

/// <summary>
/// Le navigateur a envoyé les octets : le serveur mesure l'objet stocké (taille), reconnaît son type à ses premiers
/// octets et le fait analyser. Le domaine tranche : conforme -> disponible ; sinon -> quarantaine avec le code de
/// l'écart (le fichier n'est jamais servi). Rejouer sur un fichier déjà traité renvoie son état.
/// </summary>
public sealed record CompleteUploadCommand(Guid FileId) : ICommand<UploadResult>;

public sealed class CompleteUploadHandler(FilesWorkspace workspace, IClock clock) : ICommandHandler<CompleteUploadCommand, UploadResult>
{
    public async Task<UploadResult> HandleAsync(CompleteUploadCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var file = await workspace.LoadAsync(command.FileId, cancellationToken);
        switch (file.Status)
        {
            case MediaFileStatus.Available:
                return new UploadResult(file.Id.Value, UploadOutcome.Available, null);
            case MediaFileStatus.Quarantined:
                return new UploadResult(file.Id.Value, UploadOutcome.Quarantined, file.QuarantineReason);
            case MediaFileStatus.Deleted:
                throw new NotFoundException(FilesApplicationErrors.FileNotFound);
        }

        var stored = await workspace.Storage.InspectAsync(file.StorageKey, ContentSniffer.HeadLength, cancellationToken)
            ?? throw new DomainException(FilesApplicationErrors.NotUploadedYet);
        var outcome = await StoredFileVerification.VerifyAsync(workspace, file, stored, clock, cancellationToken);
        await workspace.SaveAsync(cancellationToken);
        return outcome;
    }
}

/// <summary>
/// Fichier produit PAR LE SERVEUR (export de Reporting...) : appelé par l'hôte, jamais par une route. Les octets sont
/// écrits dans le stockage puis vérifiés comme un envoi du personnel. Renvoie l'id du fichier disponible.
/// </summary>
public sealed record StoreGeneratedFileCommand(string OwnerType, Guid OwnerId, string FileName, string ContentType, byte[] Content)
    : ICommand<Guid>;

public sealed class StoreGeneratedFileHandler(FilesWorkspace workspace, IClock clock) : ICommandHandler<StoreGeneratedFileCommand, Guid>
{
    public async Task<Guid> HandleAsync(StoreGeneratedFileCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Content);
        var tenant = workspace.CurrentTenant;
        var owner = FileOwner.Create(command.OwnerType, command.OwnerId);
        var ownerType = FileOwnerTypes.Find(owner.OwnerType) ?? throw new DomainException(FilesApplicationErrors.OwnerTypeNotUploadable);
        var existing = (await workspace.Files.ListByOwnerAsync(tenant, owner, cancellationToken))
            .FirstOrDefault(candidate => candidate.CanBeServed);
        if (existing is not null)
        {
            return existing.Id.Value;
        }

        var file = MediaFile.CreatePending(
            tenant,
            owner,
            Domain.FileName.Create(command.FileName),
            Domain.ContentType.Create(command.ContentType),
            command.Content.LongLength,
            FileOwnerTypes.NewStorageKey(tenant, owner),
            ownerType.Policy,
            clock);

        await workspace.Storage.PutAsync(file.StorageKey, file.ContentType, command.Content, cancellationToken);
        var head = command.Content.AsSpan(0, Math.Min(command.Content.Length, ContentSniffer.HeadLength)).ToArray();
        var outcome = await StoredFileVerification.VerifyAsync(
            workspace, file, new StoredObject(command.Content.LongLength, head), clock, cancellationToken);
        workspace.Files.Add(file);
        await workspace.SaveAsync(cancellationToken);
        return outcome.Outcome == UploadOutcome.Available
            ? file.Id.Value
            : throw new DomainException(new DomainError(outcome.Reason ?? "files.media_file.quarantined", "The generated file was rejected."));
    }
}

/// <summary>La vérification commune : antivirus, puis faits observés confrontés par le domaine ; tout écart = quarantaine.</summary>
internal static class StoredFileVerification
{
    public static async Task<UploadResult> VerifyAsync(
        FilesWorkspace workspace, MediaFile file, StoredObject stored, IClock clock, CancellationToken cancellationToken)
    {
        // L'antivirus d'abord : un fichier infecté ne devient jamais disponible, même un instant.
        if (await workspace.Scanner.ScanAsync(file.StorageKey, cancellationToken) is { } threat)
        {
            file.Quarantine(threat, clock);
            return new UploadResult(file.Id.Value, UploadOutcome.Quarantined, file.QuarantineReason);
        }

        var detected = ContentSniffer.Detect(stored.Head, file.ContentType);
        try
        {
            // Le domaine refuse sans rien changer : taille nulle, au-delà de la limite, différente de la déclarée, type différent.
            file.MarkAvailable(stored.SizeInBytes, detected, clock);
        }
        catch (DomainException exception)
        {
            file.Quarantine(exception.Code, clock);
            return new UploadResult(file.Id.Value, UploadOutcome.Quarantined, exception.Code);
        }

        return new UploadResult(file.Id.Value, UploadOutcome.Available, null);
    }
}
