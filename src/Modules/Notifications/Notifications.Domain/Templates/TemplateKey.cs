using System.Text.RegularExpressions;

namespace Notifications.Domain;

/// <summary>Nom fonctionnel d'un modèle, p. ex. <c>reservation.confirmed</c>. Normalisé en minuscules.</summary>
public sealed partial class TemplateKey : ValueObject
{
    public const int MaxLength = 100;

    private TemplateKey(string value) => Value = value;

    public string Value { get; }

    public static TemplateKey Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), TemplateErrors.KeyInvalid);

        var normalized = value!.Trim().ToLowerInvariant();
        DomainException.ThrowIf(normalized.Length > MaxLength || !Pattern().IsMatch(normalized), TemplateErrors.KeyInvalid);

        return new TemplateKey(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
