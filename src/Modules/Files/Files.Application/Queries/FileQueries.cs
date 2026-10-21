using Files.Application.Abstractions;
using Files.Application.Storage;
using Files.Domain;

namespace Files.Application.Queries;

public sealed record MediaFileDto(
    Guid Id,
    string OwnerType,
    Guid OwnerId,
    string FileName,
    string ContentType,
    long SizeInBytes,
    MediaFileStatus Status,
    string? QuarantineReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AvailableAtUtc,
    Guid? ThumbnailOf = null,
    Guid? ThumbnailId = null)
{
    // La clé de stockage n'est jamais exposée : elle n'apporte rien au client et décrit l'organisation du bucket.
    public static MediaFileDto Of(MediaFile file, MediaFile? thumbnail = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new(
            file.Id.Value, file.Owner.OwnerType, file.Owner.OwnerId, file.FileName.Value, file.ContentType.Value, file.SizeInBytes,
            file.Status, file.QuarantineReason, file.CreatedAtUtc, file.AvailableAtUtc, file.ThumbnailOf?.Value, thumbnail?.Id.Value);
    }
}

internal static class Thumbnails
{
    /// <summary>La miniature disponible de chaque photo, parmi les fichiers donnés.</summary>
    public static Dictionary<FileId, MediaFile> Index(IEnumerable<MediaFile> files) =>
        files.Where(file => file.ThumbnailOf is not null && file.CanBeServed)
            .GroupBy(file => file.ThumbnailOf!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(file => file.CreatedAtUtc).First());
}

/// <summary>Un lien de téléchargement direct, court.</summary>
public sealed record DownloadLinkDto(Uri Url, DateTimeOffset ExpiresAtUtc);

/// <summary>Le contenu d'un fichier, relu dans le stockage (pour l'hôte : export de Reporting téléchargé par l'API).</summary>
public sealed record FileContentDto(string FileName, string ContentType, byte[] Content);

public sealed record GetFileQuery(Guid FileId) : IQuery<MediaFileDto>;

public sealed class GetFileHandler(FilesWorkspace workspace) : IQueryHandler<GetFileQuery, MediaFileDto>
{
    public async Task<MediaFileDto> HandleAsync(GetFileQuery query, CancellationToken cancellationToken = default) =>
        MediaFileDto.Of(await workspace.LoadAsync(query.FileId, cancellationToken));
}

/// <summary>Les fichiers (non supprimés) d'un propriétaire du restaurant du contexte, chaque photo avec sa miniature.</summary>
public sealed record ListOwnerFilesQuery(string? OwnerType, Guid OwnerId) : IQuery<IReadOnlyList<MediaFileDto>>;

public sealed class ListOwnerFilesHandler(FilesWorkspace workspace) : IQueryHandler<ListOwnerFilesQuery, IReadOnlyList<MediaFileDto>>
{
    public async Task<IReadOnlyList<MediaFileDto>> HandleAsync(ListOwnerFilesQuery query, CancellationToken cancellationToken = default)
    {
        var owner = FileOwner.Create(query.OwnerType, query.OwnerId);
        var files = await workspace.Files.ListByOwnerAsync(workspace.CurrentTenant, owner, cancellationToken);
        var thumbnails = Thumbnails.Index(files);
        return files.Where(file => !file.IsThumbnail).Select(file => MediaFileDto.Of(file, thumbnails.GetValueOrDefault(file.Id))).ToList();
    }
}

/// <summary>Un lien de téléchargement signé, seulement pour un fichier disponible (jamais en attente ni en quarantaine).</summary>
public sealed record GetDownloadLinkQuery(Guid FileId) : IQuery<DownloadLinkDto>;

public sealed class GetDownloadLinkHandler(FilesWorkspace workspace, IClock clock) : IQueryHandler<GetDownloadLinkQuery, DownloadLinkDto>
{
    public async Task<DownloadLinkDto> HandleAsync(GetDownloadLinkQuery query, CancellationToken cancellationToken = default)
    {
        var file = await workspace.LoadAsync(query.FileId, cancellationToken);
        DomainException.ThrowIf(!file.CanBeServed, FilesApplicationErrors.NotServable);
        var lifetime = workspace.Settings.DownloadUrlLifetime;
        return new DownloadLinkDto(workspace.Storage.PresignDownload(file.StorageKey, file.FileName, lifetime), clock.UtcNow + lifetime);
    }
}

/// <summary>
/// Le lien public stable d'un fichier disponible d'un type public (logo, photos) : à recopier là où un lien est attendu
/// (identité du restaurant). Refusé pour un type privé ou sans liens publics configurés.
/// </summary>
public sealed record GetPublicLinkQuery(Guid FileId) : IQuery<PublicImageDto>;

public sealed class GetPublicLinkHandler(FilesWorkspace workspace, PublicMediaLinks links) : IQueryHandler<GetPublicLinkQuery, PublicImageDto>
{
    public async Task<PublicImageDto> HandleAsync(GetPublicLinkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var file = await workspace.LoadAsync(query.FileId, cancellationToken);
        DomainException.ThrowIf(!file.CanBeServed, FilesApplicationErrors.NotServable);
        DomainException.ThrowIf(!links.IsEnabled || !FileOwnerTypes.IsPublic(file.Owner.OwnerType), FilesApplicationErrors.NotPublic);
        return new PublicImageDto(file.Owner.OwnerId, file.Id.Value, links.UrlFor(workspace.CurrentTenant, file.Id)!);
    }
}

/// <summary>Le contenu d'un fichier disponible du restaurant du contexte (pour l'hôte ; aucune route).</summary>
public sealed record ReadFileContentQuery(Guid FileId) : IQuery<FileContentDto>;

public sealed class ReadFileContentHandler(FilesWorkspace workspace) : IQueryHandler<ReadFileContentQuery, FileContentDto>
{
    public async Task<FileContentDto> HandleAsync(ReadFileContentQuery query, CancellationToken cancellationToken = default)
    {
        var file = await workspace.LoadAsync(query.FileId, cancellationToken);
        DomainException.ThrowIf(!file.CanBeServed, FilesApplicationErrors.NotServable);
        var content = await workspace.Storage.GetAsync(file.StorageKey, cancellationToken)
            ?? throw new NotFoundException(FilesApplicationErrors.FileNotFound);
        return new FileContentDto(file.FileName.Value, file.ContentType.Value, content);
    }
}

/// <summary>Une photo publique d'un propriétaire : son lien stable, utilisable tel quel par un navigateur ou un CDN.</summary>
/// <param name="ThumbnailUrl">Lien public de la miniature, pour les listes ; null si la photo n'en a pas.</param>
public sealed record PublicImageDto(Guid OwnerId, Guid FileId, Uri Url, Uri? ThumbnailUrl = null);

/// <summary>
/// Les photos disponibles de plusieurs propriétaires du restaurant du contexte, chacune avec son lien public stable,
/// de la plus ancienne (photo principale) à la plus récente. Vide pour un type non public ou sans liens configurés.
/// </summary>
public sealed record ListPublicImagesQuery(string OwnerType, IReadOnlyCollection<Guid> OwnerIds) : IQuery<IReadOnlyList<PublicImageDto>>;

public sealed class ListPublicImagesHandler(FilesWorkspace workspace, PublicMediaLinks links) : IQueryHandler<ListPublicImagesQuery, IReadOnlyList<PublicImageDto>>
{
    public async Task<IReadOnlyList<PublicImageDto>> HandleAsync(ListPublicImagesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!links.IsEnabled || !FileOwnerTypes.IsPublic(query.OwnerType) || query.OwnerIds is not { Count: > 0 })
        {
            return [];
        }

        var tenant = workspace.CurrentTenant;
        var files = await workspace.Files.ListAvailableByOwnersAsync(tenant, query.OwnerType, query.OwnerIds.Where(id => id != Guid.Empty).ToList(), cancellationToken);
        var thumbnails = Thumbnails.Index(files);
        return files
            .Where(file => file.CanBeServed && !file.IsThumbnail)
            .Select(file => new PublicImageDto(
                file.Owner.OwnerId,
                file.Id.Value,
                links.UrlFor(tenant, file.Id)!,
                thumbnails.TryGetValue(file.Id, out var thumbnail) ? links.UrlFor(tenant, thumbnail.Id) : null))
            .ToList();
    }
}

/// <summary>Le contenu servi par un lien public : jamais le nom du fichier ni sa clé de stockage.</summary>
public sealed record PublicMediaDto(Guid FileId, string ContentType, byte[] Content);

/// <summary>
/// Requête anonyme : le tenant et le fichier viennent du jeton signé, jamais d'une autre valeur de la requête. Null
/// (404) pour un jeton faux, un fichier non disponible, d'un type non public, ou d'un restaurant suspendu ou fermé.
/// </summary>
public sealed record ReadPublicMediaQuery(string? Token) : IQuery<PublicMediaDto?>
{
    public override string ToString() => "ReadPublicMediaQuery (***)";
}

public sealed class ReadPublicMediaHandler(IMediaFileRepository files, IObjectStorage storage, IFileTenants tenants, PublicMediaLinks links)
    : IQueryHandler<ReadPublicMediaQuery, PublicMediaDto?>
{
    public async Task<PublicMediaDto?> HandleAsync(ReadPublicMediaQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (links.Read(query.Token) is not { } reference)
        {
            return null;
        }

        var file = await files.FindForPublicLinkAsync(reference.TenantId, reference.FileId, cancellationToken);
        if (file is not { CanBeServed: true } || !FileOwnerTypes.IsPublic(file.Owner.OwnerType)
            || !await tenants.AcceptsBusinessWritesAsync(reference.TenantId, cancellationToken))
        {
            return null;
        }

        var content = await storage.GetAsync(file.StorageKey, cancellationToken);
        return content is null ? null : new PublicMediaDto(file.Id.Value, file.ContentType.Value, content);
    }
}
