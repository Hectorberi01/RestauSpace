using System.Text.RegularExpressions;

namespace Notifications.Domain;

/// <summary>
/// Les variables d'un contenu de template : <c>{{ nom.pointé }}</c>, rien d'autre. Pas d'expression, de filtre, de boucle
/// ni d'appel : un template ne peut RIEN exécuter. Toute accolade double hors d'une variable bien formée est refusée, pour
/// qu'un contenu ambigu ne passe jamais tel quel chez le destinataire.
/// </summary>
public static partial class TemplateVariables
{
    public const int MaxNameLength = 64;
    public const int MaxVariablesPerContent = 100;

    /// <summary>Les noms de variables utilisés, dans l'ordre de première apparition, sans doublon.</summary>
    public static IReadOnlyList<string> Extract(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        var matches = Placeholder().Matches(content);
        var rest = Placeholder().Replace(content, string.Empty);
        DomainException.ThrowIf(
            rest.Contains("{{", StringComparison.Ordinal) || rest.Contains("}}", StringComparison.Ordinal),
            TemplateErrors.VariableSyntaxInvalid);
        DomainException.ThrowIf(matches.Count > MaxVariablesPerContent, TemplateErrors.TooManyVariables);

        var names = matches.Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToList();
        DomainException.ThrowIf(names.Any(name => name.Length > MaxNameLength), TemplateErrors.VariableSyntaxInvalid);
        return names;
    }

    /// <summary>Les variables de ces contenus que le type n'autorise pas.</summary>
    public static IReadOnlyList<string> NotAllowed(NotificationTypeDefinition type, params string?[] contents)
    {
        ArgumentNullException.ThrowIfNull(type);
        return contents.SelectMany(Extract).Distinct(StringComparer.Ordinal).Where(name => !type.Allows(name)).ToList();
    }

    /// <summary>Remplace chaque variable de <paramref name="content"/> par sa valeur (rendu).</summary>
    internal static string? Replace(string? content, Func<string, string> valueOf) =>
        content is null ? null : Placeholder().Replace(content, match => valueOf(match.Groups["name"].Value));

    [GeneratedRegex(@"\{\{\s*(?<name>[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)*)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
