using System.Globalization;
using System.Text;

namespace Staff.Domain;

/// <summary>
/// Nom affiché d'un rôle, stocké sous une graphie canonique unique : normalisé Unicode (NFKC), sans caractères de
/// contrôle ni de format, suites d'espaces réduites à une seule, sans espaces en bordure. Deux noms qui ne diffèrent
/// que par la casse sont le même nom.
/// </summary>
public sealed class RoleName : ValueObject
{
    public const int MaxLength = 80;

    private RoleName(string value) => Value = value;

    public string Value { get; }

    public static RoleName Create(string? value)
    {
        // Vacuité et longueur sont testées sur la graphie canonique : un nom fait uniquement de caractères
        // invisibles est vide, et « Chef  de rang » est le même nom que « Chef de rang ».
        var normalized = Normalize(value);
        DomainException.ThrowIf(normalized.Length == 0, RoleErrors.NameRequired);
        DomainException.ThrowIf(normalized.Length > MaxLength, RoleErrors.NameTooLong);

        return new RoleName(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value.ToUpperInvariant();
    }

    public override string ToString() => Value;

    private static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Composition de compatibilité d'abord : elle change les espaces insécables et autres espaces exotiques en
        // espaces simples et unifie les graphies similaires (lettres pleine chasse, ligatures) avant toute comparaison
        // ou mesure.
        var composed = WithoutLoneSurrogates(value).Normalize(NormalizationForm.FormKC);

        var builder = new StringBuilder(composed.Length);
        var pendingSpace = false;
        foreach (var character in composed)
        {
            // Les espaces sont testés avant la catégorie contrôle : une tabulation ou un saut de ligne sépare deux mots.
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (char.GetUnicodeCategory(character) is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    // string.Normalize échoue sur un UTF-16 mal formé ; un surrogate isolé n'est pas un caractère, il est retiré.
    private static string WithoutLoneSurrogates(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                builder.Append(character).Append(value[++index]);
            }
            else if (!char.IsSurrogate(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
