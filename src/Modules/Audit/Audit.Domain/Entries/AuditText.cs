using System.Globalization;

namespace Audit.Domain;

internal static class AuditText
{
    /// <summary>
    /// Caractères qui permettraient à un appelant de forger une seconde ligne dans un journal exporté ou affiché :
    /// caractères de contrôle et séparateurs Unicode de ligne et de paragraphe.
    /// </summary>
    public static bool IsLineBreakingOrControl(char character) =>
        char.IsControl(character)
        || char.GetUnicodeCategory(character) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
