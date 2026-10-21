using System.Text.RegularExpressions;

namespace Reporting.Domain;

/// <summary>Format partagé des codes de métrique et de filtre : minuscules et points, ex. <c>sales.net_revenue</c>.</summary>
internal static partial class ReportCode
{
    public const int MaxLength = 64;

    public static string Normalize(string? value) => Normalize(value, ReportErrors.CodeInvalid);

    public static string Normalize(string? value, DomainError error)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), error);

        var normalized = value!.Trim().ToLowerInvariant();
        DomainException.ThrowIf(normalized.Length > MaxLength || !Pattern().IsMatch(normalized), error);

        return normalized;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
