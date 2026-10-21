using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

/// <summary>
/// Photo de profil d'un compte, rangée à part du compte (qui n'en garde que la date) pour ne pas la charger à chaque
/// connexion. Les écritures sont enregistrées avec l'unité de travail du module.
/// </summary>
public interface IUserAvatars
{
    Task<AvatarImage?> FindAsync(UserId userId, CancellationToken cancellationToken = default);

    Task SaveAsync(UserId userId, AvatarImage image, CancellationToken cancellationToken = default);

    Task RemoveAsync(UserId userId, CancellationToken cancellationToken = default);
}

/// <summary>Image JPEG, PNG ou WebP de 512 Ko au plus. Le type est reconnu aux octets, jamais pris de la requête.</summary>
public sealed record AvatarImage
{
    public const int MaxBytes = 512 * 1024;

    private AvatarImage(byte[] content, string contentType)
    {
        Content = content;
        ContentType = contentType;
    }

    public byte[] Content { get; }

    public string ContentType { get; }

    public static AvatarImage Create(byte[]? content)
    {
        DomainException.ThrowIf(content is null || content.Length is 0 or > MaxBytes, UserErrors.AvatarInvalid);
        var type = Detect(content!);
        DomainException.ThrowIf(type is null, UserErrors.AvatarInvalid);
        return new AvatarImage(content!, type!);
    }

    /// <summary>Pour l'Infrastructure : une image déjà contrôlée à l'enregistrement.</summary>
    public static AvatarImage Restore(byte[] content, string contentType) => new(content, contentType);

    private static string? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
