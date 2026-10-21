using Files.Domain;

namespace Files.Application.Storage;

/// <summary>
/// Un type de propriétaire connu : sa politique d'upload, s'il peut être visé par une route du personnel, et si ses
/// fichiers disponibles peuvent être servis par un lien public stable (photos affichées aux clients).
/// </summary>
public sealed record FileOwnerType(string Code, FileUploadPolicy Policy, bool UploadableByStaff, bool IsPublic = false);

/// <summary>
/// Le vocabulaire des propriétaires (README §3) et la politique de chacun. Un code absent d'ici est refusé : Files ne
/// stocke rien pour un propriétaire qu'il ne connaît pas.
/// </summary>
public static class FileOwnerTypes
{
    public const string CatalogProduct = "catalog.product";
    public const string LocationsLocation = "locations.location";
    public const string TenantsTenant = "tenants.tenant";
    public const string ReportingExportJob = "reporting.export_job";

    private static readonly ContentType[] Images =
        [ContentType.Create("image/jpeg"), ContentType.Create("image/png"), ContentType.Create("image/webp")];

    private static readonly Dictionary<string, FileOwnerType> ByCode = new FileOwnerType[]
    {
        // Photos des produits et des établissements, logo du restaurant.
        new(CatalogProduct, FileUploadPolicy.Create(5L * 1024 * 1024, Images), true, IsPublic: true),
        new(LocationsLocation, FileUploadPolicy.Create(5L * 1024 * 1024, Images), true, IsPublic: true),
        new(TenantsTenant, FileUploadPolicy.Create(2L * 1024 * 1024, Images), true, IsPublic: true),

        // Exports de Reporting : produits par le serveur, jamais téléversés par une personne.
        new(ReportingExportJob, FileUploadPolicy.Create(
            50L * 1024 * 1024,
            [ContentType.Create("text/csv"), ContentType.Create("application/pdf"), ContentType.Create("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]), false),
    }.ToDictionary(type => type.Code, StringComparer.Ordinal);

    public static FileOwnerType? Find(string code) => ByCode.GetValueOrDefault(code);

    /// <summary>Les fichiers de ce type peuvent-ils être servis par un lien public ? Jamais un export ni un type inconnu.</summary>
    public static bool IsPublic(string? code) => code is not null && Find(code) is { IsPublic: true };

    /// <summary>Un propriétaire que le personnel peut viser ; sinon <c>files.owner.type_not_uploadable</c>.</summary>
    public static FileOwnerType RequireUploadable(FileOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return Find(owner.OwnerType) is { UploadableByStaff: true } type
            ? type
            : throw new DomainException(FilesApplicationErrors.OwnerTypeNotUploadable);
    }

    /// <summary>
    /// Clé de stockage : JAMAIS dérivée du nom de fichier. Préfixe du tenant (isolation lisible dans le bucket, suppression
    /// en masse possible), type de propriétaire, puis un identifiant aléatoire.
    /// </summary>
    public static StorageKey NewStorageKey(TenantId tenantId, FileOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return StorageKey.Create($"tenants/{tenantId.Value:N}/{owner.OwnerType}/{Guid.NewGuid():N}");
    }
}
