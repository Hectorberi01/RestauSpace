namespace Files.Application;

/// <summary>Erreurs des cas d'usage de Files (les erreurs des règles métier sont dans Files.Domain).</summary>
public static class FilesApplicationErrors
{
    public static readonly DomainError ThumbnailOwnerMismatch = new("files.media_file.thumbnail_owner_mismatch", "A thumbnail belongs to the owner of its photo.");

    public static readonly DomainError ConcurrentUpdate =
        new("files.concurrent_update", "The resource was changed by someone else; reload it and try again.", DomainErrorKind.Conflict);

    public static readonly DomainError TenantNotWritable =
        new("files.tenant.not_writable", "The restaurant account is suspended or closed: ask the platform to restore it.");

    // Aussi la réponse pour un fichier d'un autre tenant : on ne révèle jamais qu'il existe.
    public static readonly DomainError FileNotFound = new("files.media_file.not_found", "The file does not exist.");
    public static readonly DomainError OwnerNotFound = new("files.owner.not_found", "The owner of the file does not exist in this restaurant.");
    public static readonly DomainError OwnerTypeNotUploadable =
        new("files.owner.type_not_uploadable", "Files cannot be uploaded for this kind of owner.");
    public static readonly DomainError NotUploadedYet =
        new("files.media_file.not_uploaded", "The file has not been uploaded to the storage yet.");
    public static readonly DomainError NotPublic = new("files.media_file.not_public", "This file cannot be shared by a public link.");
    public static readonly DomainError NotServable = new("files.media_file.not_servable", "The file is not available.");
}

/// <summary>Codes de permission déclarés par le module Files pour les restaurants (accordés via les rôles Staff).</summary>
public static class FilesPermissions
{
    /// <summary>Lire les métadonnées et obtenir un lien de téléchargement.</summary>
    public const string View = "files.view";

    /// <summary>Téléverser un fichier (demande d'URL d'envoi, confirmation).</summary>
    public const string Upload = "files.upload";

    /// <summary>Supprimer un fichier.</summary>
    public const string Manage = "files.manage";

    public static IReadOnlyList<string> All { get; } = [View, Upload, Manage];
}

/// <summary>Réglages des cas d'usage (section <c>Files</c> de l'hôte).</summary>
public sealed class FilesSettings
{
    /// <summary>Durée de validité d'une URL d'envoi.</summary>
    public TimeSpan UploadUrlLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Durée de validité d'un lien de téléchargement.</summary>
    public TimeSpan DownloadUrlLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Un fichier jamais confirmé est supprimé après ce délai.</summary>
    public TimeSpan AbandonedUploadAfter { get; set; } = TimeSpan.FromHours(24);
}
