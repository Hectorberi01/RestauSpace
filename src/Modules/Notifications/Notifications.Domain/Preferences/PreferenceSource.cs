using System.Text.RegularExpressions;

namespace Notifications.Domain;

/// <summary>
/// Où un choix de préférence a été fait (« unsubscribe-link », « app-settings », « back-office », « provider-webhook ») :
/// un code court en minuscules, jamais une phrase, pour qu'il ne puisse pas porter de données personnelles.
/// </summary>
public sealed partial class PreferenceSource : ValueObject
{
    public const int MaxLength = 100;
    private PreferenceSource(string value) => Value = value;
    public string Value { get; }

    public static PreferenceSource Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        DomainException.ThrowIf(
            normalized is null || normalized.Length > MaxLength || !Pattern().IsMatch(normalized),
            NotificationPreferenceErrors.SourceInvalid);
        return new PreferenceSource(normalized!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z0-9]+([._\-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
