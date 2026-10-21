using System.Globalization;
using System.Text;

namespace Floor.Domain;

internal static class DisplayText
{
    /// <summary>
    /// Forme canonique d'un libellé saisi par un utilisateur, pour que deux textes qu'un humain lit à l'identique
    /// soient identiques : composé par compatibilité (NFKC), sans caractères de contrôle ni de format invisibles,
    /// suites d'espaces réduites à une seule, sans espaces autour. Le vide et la longueur se testent sur le résultat.
    /// </summary>
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Runes d'abord : string.Normalize lève sur un surrogate isolé, EnumerateRunes le remplace par U+FFFD.
        var wellFormed = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            wellFormed.Append(rune.ToString());
        }

        var result = new StringBuilder(wellFormed.Length);
        var pendingSpace = false;
        foreach (var rune in wellFormed.ToString().Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            // Tabulations et sauts de ligne sont à la fois des espaces et des caractères de contrôle :
            // ils séparent les mots.
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            result.Append(rune.ToString());
        }

        return result.ToString();
    }
}
