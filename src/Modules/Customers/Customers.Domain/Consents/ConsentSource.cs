using System.Text.RegularExpressions;

namespace Customers.Domain;

/// <summary>
/// Où un changement de consentement a été recueilli (ex. "web-signup-form", "pos", "unsubscribe-link") : fait partie
/// de la preuve. Un code court en minuscules, jamais une phrase, pour qu'il ne puisse pas porter de données personnelles.
/// </summary>
public sealed partial class ConsentSource : ValueObject
{
    public const int MaxLength = 100;

    private ConsentSource(string value) => Value = value;

    /// <summary>Source système des retraits de consentement qui suivent l'anonymisation du client.</summary>
    public static ConsentSource CustomerAnonymization { get; } = new("system.customer-anonymization");

    public string Value { get; }

    public static ConsentSource Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), ConsentErrors.SourceRequired);

        var normalized = value!.Trim().ToLowerInvariant();
        DomainException.ThrowIf(normalized.Length > MaxLength, ConsentErrors.SourceTooLong);
        DomainException.ThrowIf(!Pattern().IsMatch(normalized), ConsentErrors.SourceInvalid);

        return new ConsentSource(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z0-9]+([._\-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
