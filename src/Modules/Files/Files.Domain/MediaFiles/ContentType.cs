using System.Text.RegularExpressions;

namespace Files.Domain;

/// <summary>Type MIME normalisé en <c>type/subtype</c> minuscule ; les paramètres (<c>; charset=...</c>) sont retirés.</summary>
public sealed partial class ContentType : ValueObject
{
    public const int MaxLength = 255;

    private ContentType(string value) => Value = value;

    public string Value { get; }

    public string Type => Value[..Value.IndexOf('/')];

    public string Subtype => Value[(Value.IndexOf('/') + 1)..];

    public static ContentType Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), FileErrors.ContentTypeInvalid);

        var normalized = value!.Split(';', 2)[0].Trim().ToLowerInvariant();
        DomainException.ThrowIf(
            normalized.Length > MaxLength || !Pattern().IsMatch(normalized),
            FileErrors.ContentTypeInvalid);

        return new ContentType(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    // restricted-name de la RFC 6838 des deux côtés ; les jokers ne sont volontairement pas un content type valide.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9!#$&^_.+\-]*/[a-z0-9][a-z0-9!#$&^_.+\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
