using System.Text.RegularExpressions;

namespace Notifications.Domain;

/// <summary>
/// Langue d'un message : une sous-étiquette de langue principale, 2 ou 3 lettres minuscules (<c>fr</c>, <c>en</c>).
/// Sans région : un appelant qui détient <c>fr-FR</c> passe <c>fr</c>.
/// </summary>
public sealed partial class NotificationLanguage : ValueObject
{
    private NotificationLanguage(string value) => Value = value;

    public string Value { get; }

    public static NotificationLanguage Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), TemplateErrors.LanguageInvalid);

        var normalized = value!.Trim().ToLowerInvariant();
        DomainException.ThrowIf(!Pattern().IsMatch(normalized), TemplateErrors.LanguageInvalid);

        return new NotificationLanguage(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z]{2,3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
