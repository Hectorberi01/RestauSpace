using System.Collections.Frozen;

namespace Customers.Domain;

/// <summary>
/// Souhaits non sensibles du client. Un canal marketing listé ici est une préférence,
/// pas une autorisation : la preuve légale est l'agrégat <see cref="Consent"/>.
/// </summary>
public sealed class CustomerPreference : ValueObject
{
    /// <summary>Sous-étiquettes de langue principale dans lesquelles le produit est traduit.</summary>
    public static readonly FrozenSet<string> SupportedLanguages =
        FrozenSet.ToFrozenSet(["fr", "en", "es", "pt", "de", "it", "nl", "ar"], StringComparer.Ordinal);

    private readonly CommunicationChannel[] _marketingChannels;

    private CustomerPreference(string language, CommunicationChannel[] marketingChannels)
    {
        Language = language;
        _marketingChannels = marketingChannels;
    }

    /// <summary>Étiquette normalisée de style BCP-47 : "fr" ou "fr-FR".</summary>
    public string Language { get; }

    public IReadOnlyCollection<CommunicationChannel> MarketingChannels => Array.AsReadOnly(_marketingChannels);

    public static CustomerPreference Create(string? language, IEnumerable<CommunicationChannel>? marketingChannels = null)
    {
        var normalizedLanguage = NormalizeLanguage(language);

        var channels = (marketingChannels ?? []).Distinct().Order().ToArray();
        DomainException.ThrowIf(channels.Any(channel => !Enum.IsDefined(channel)), CustomerErrors.ChannelNotSupported);

        return new CustomerPreference(normalizedLanguage, channels);
    }

    public bool WantsMarketingOn(CommunicationChannel channel) => _marketingChannels.Contains(channel);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Language;
        foreach (var channel in _marketingChannels)
        {
            yield return channel;
        }
    }

    private static string NormalizeLanguage(string? language)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(language), CustomerErrors.LanguageInvalid);

        var parts = language!.Trim().Split('-');
        DomainException.ThrowIf(
            parts.Length > 2 || !IsAsciiLetters(parts[0], 2) || (parts.Length == 2 && !IsAsciiLetters(parts[1], 2)),
            CustomerErrors.LanguageInvalid);

        var primary = parts[0].ToLowerInvariant();
        DomainException.ThrowIf(!SupportedLanguages.Contains(primary), CustomerErrors.LanguageNotSupported);

        return parts.Length == 2 ? $"{primary}-{parts[1].ToUpperInvariant()}" : primary;
    }

    private static bool IsAsciiLetters(string value, int length) =>
        value.Length == length && value.All(char.IsAsciiLetter);
}
