namespace Files.Infrastructure;

/// <summary>Où sont les octets : Cloudflare R2 en production, en mémoire pour les tests et le développement.</summary>
public enum FileStorageProvider
{
    R2 = 1,

    /// <summary>En mémoire du processus : perdu au redémarrage. Développement et tests seulement.</summary>
    Memory = 2,

    /// <summary>
    /// Disque local, servi par l'API elle-même avec des URLs signées : le navigateur peut envoyer et afficher les fichiers
    /// sans compte R2. Développement seulement.
    /// </summary>
    Local = 3,
}

/// <summary>
/// Stockage <see cref="FileStorageProvider.Local"/> : dossier des octets, adresse publique de l'API (le navigateur y envoie
/// les fichiers) et clé de signature des URLs (Base64, 32 octets au moins), jamais journalisée.
/// </summary>
public sealed class LocalStorageOptions
{
    public string Directory { get; set; } = "data/files";

    public Uri? PublicBaseUrl { get; set; }

    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Lève si un réglage manque ou si la clé est trop courte (sans jamais citer sa valeur).</summary>
    public byte[] Validate()
    {
        if (string.IsNullOrWhiteSpace(Directory) || PublicBaseUrl is null || !PublicBaseUrl.IsAbsoluteUri)
        {
            throw new InvalidOperationException("Files:Local is incomplete: Directory and an absolute PublicBaseUrl are required.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(SigningKey);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Files:Local:SigningKey must be Base64.");
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException("Files:Local:SigningKey must be at least 32 bytes.");
        }

        return key;
    }
}

/// <summary>
/// Compte Cloudflare R2 (API compatible S3). La clé secrète ne sort jamais de <c>R2ObjectStorage</c> : ni journal, ni
/// exception, ni réponse. À fournir par variables d'environnement ou coffre de secrets, jamais dans un fichier versionné.
/// </summary>
public sealed class R2Options
{
    /// <summary>Identifiant du compte Cloudflare (sous-domaine de l'endpoint <c>{AccountId}.r2.cloudflarestorage.com</c>).</summary>
    public string AccountId { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    /// <summary>Facultatif : un autre endpoint compatible S3 (juridiction UE de R2, MinIO en local...).</summary>
    public Uri? Endpoint { get; set; }

    public Uri ResolveEndpoint() => Endpoint ?? new Uri($"https://{AccountId}.r2.cloudflarestorage.com");

    /// <summary>Lève si un réglage manque (sans jamais citer la valeur de la clé secrète).</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessKeyId) || string.IsNullOrWhiteSpace(SecretAccessKey) || string.IsNullOrWhiteSpace(Bucket)
            || (Endpoint is null && string.IsNullOrWhiteSpace(AccountId)))
        {
            throw new InvalidOperationException("Files:R2 is incomplete: AccountId (or Endpoint), AccessKeyId, SecretAccessKey and Bucket are required.");
        }
    }
}

/// <summary>
/// Liens publics stables des photos (catalogue en ligne) : adresse publique de l'API ou du CDN placé devant elle, et
/// clé de signature (Base64, 32 octets au moins), jamais journalisée. Sans clé, aucun lien public n'est produit.
/// </summary>
public sealed class PublicMediaOptions
{
    public Uri? BaseUrl { get; set; }

    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Null quand la clé n'est pas fournie ; lève si elle est invalide (sans jamais citer sa valeur).</summary>
    public byte[]? Key()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            return null;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(SigningKey);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Files:PublicMedia:SigningKey must be Base64.");
        }

        if (key.Length < 32 || BaseUrl is not { IsAbsoluteUri: true })
        {
            throw new InvalidOperationException("Files:PublicMedia needs an absolute BaseUrl and a SigningKey of at least 32 bytes.");
        }

        return key;
    }
}

/// <summary>Réglages techniques du module (section <c>Files</c> de l'hôte).</summary>
public sealed class FilesOptions
{
    public FileStorageProvider Storage { get; set; } = FileStorageProvider.R2;

    public R2Options R2 { get; } = new();

    public LocalStorageOptions Local { get; } = new();

    public PublicMediaOptions PublicMedia { get; } = new();

    /// <summary>Faux : le job de nettoyage ne tourne pas seul (tests).</summary>
    public bool RunWorkers { get; set; } = true;

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    public int CleanupBatchSize { get; set; } = 200;
}
