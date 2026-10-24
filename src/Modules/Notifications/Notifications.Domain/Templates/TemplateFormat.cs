using System.Text.RegularExpressions;

namespace Notifications.Domain;

/// <summary>Format d'un modèle : HTML si le corps DU MODÈLE commence par une balise courante (Email seulement).</summary>
public static partial class TemplateFormat
{
    public static bool IsHtml(NotificationChannel channel, string? body) =>
        channel == NotificationChannel.Email && body is not null && HtmlStart().IsMatch(body);

    [GeneratedRegex(@"^\s*<(!doctype|html|body|div|p|table|h[1-6]|br|span|a)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlStart();
}
