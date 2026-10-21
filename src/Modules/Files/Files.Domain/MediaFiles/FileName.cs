using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Files.Domain;

/// <summary>
/// Nom d'affichage d'un fichier téléversé. Il ne sert jamais à construire un chemin de stockage (voir
/// <see cref="StorageKey"/>), mais les séparateurs de chemin sont tout de même refusés pour qu'un nom hostile ne puisse
/// pas y être réinjecté. Les caractères invisibles ou changeant le sens d'écriture sont aussi refusés : un forçage
/// droite-à-gauche fait lire <c>invoice[RLO]gnp.exe</c> comme un <c>.png</c>. Les noms de périphériques Windows sont
/// refusés car le nom peut finir sur le disque d'un utilisateur.
/// </summary>
public sealed class FileName : ValueObject
{
    public const int MaxLength = 255;

    private static readonly FrozenSet<string> ReservedDeviceNames = FrozenSet.ToFrozenSet(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ],
        StringComparer.OrdinalIgnoreCase);

    private FileName(string value) => Value = value;

    public string Value { get; }

    public string Extension
    {
        get
        {
            var dot = Value.LastIndexOf('.');
            return dot <= 0 || dot == Value.Length - 1 ? string.Empty : Value[(dot + 1)..].ToLowerInvariant();
        }
    }

    public static FileName Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), FileErrors.FileNameEmpty);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(trimmed.Length > MaxLength, FileErrors.FileNameTooLong);
        DomainException.ThrowIf(
            trimmed.All(c => c == '.') || HasForbiddenCharacter(trimmed) || IsReservedDeviceName(trimmed),
            FileErrors.FileNameInvalid);

        return new FileName(trimmed);
    }

    private static bool HasForbiddenCharacter(string name)
    {
        var remaining = name.AsSpan();
        while (!remaining.IsEmpty)
        {
            // Décodage par point de code : des caractères de format existent aussi hors du BMP, et un surrogate
            // isolé n'est pas du texte.
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done)
            {
                return true;
            }

            if (rune.Value is '/' or '\\' || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control
                or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                return true;
            }

            remaining = remaining[consumed..];
        }

        return false;
    }

    /// <summary>Windows réserve le nom de périphérique quoi qui suive son premier point : <c>NUL</c>, <c>nul.txt</c>,
    /// <c>Com1.tar.gz</c>.</summary>
    private static bool IsReservedDeviceName(string name)
    {
        var dot = name.IndexOf('.');
        var stem = (dot < 0 ? name : name[..dot]).TrimEnd();

        return ReservedDeviceNames.Contains(stem);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
