using System.Text;
using Files.Domain;

namespace Files.Application.Storage;

/// <summary>
/// Le type RÉEL d'un fichier, d'après ses premiers octets (signatures), jamais d'après l'en-tête de l'émetteur ni le
/// nom. Les formats texte (CSV) n'ont pas de signature : un contenu UTF-8 sans octet de contrôle binaire est reconnu
/// comme le type texte déclaré ; tout le reste est <c>application/octet-stream</c>, que le domaine refuse.
/// </summary>
public static class ContentSniffer
{
    /// <summary>Nombre d'octets à lire pour reconnaître un type.</summary>
    public const int HeadLength = 512;

    public static readonly ContentType Unknown = ContentType.Create("application/octet-stream");

    private static readonly (byte[] Signature, int Offset, string Type)[] Signatures =
    [
        ([0xFF, 0xD8, 0xFF], 0, "image/jpeg"),
        ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], 0, "image/png"),
        ("%PDF-"u8.ToArray(), 0, "application/pdf"),
    ];

    public static ContentType Detect(ReadOnlySpan<byte> head, ContentType declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        foreach (var (signature, offset, type) in Signatures)
        {
            if (head.Length >= offset + signature.Length && head.Slice(offset, signature.Length).SequenceEqual(signature))
            {
                return ContentType.Create(type);
            }
        }

        // WebP : "RIFF" <taille sur 4 octets> "WEBP".
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return ContentType.Create("image/webp");
        }

        return declared.Type == "text" && IsText(head) ? declared : Unknown;
    }

    // Texte : UTF-8 valide (une séquence coupée en fin d'extrait est tolérée), sans NUL ni contrôle hors tab, CR, LF.
    private static bool IsText(ReadOnlySpan<byte> head)
    {
        if (head.IsEmpty)
        {
            return false;
        }

        foreach (var value in head)
        {
            if (value < 0x20 && value is not (0x09 or 0x0A or 0x0D))
            {
                return false;
            }
        }

        var decoder = new UTF8Encoding(false, true).GetDecoder();
        var chars = new char[head.Length];
        try
        {
            _ = decoder.GetChars(head, chars, flush: false);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
