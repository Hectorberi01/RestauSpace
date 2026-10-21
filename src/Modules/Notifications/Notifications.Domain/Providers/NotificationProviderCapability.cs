namespace Notifications.Domain;

/// <summary>
/// Ce qu'un prestataire sait faire, déclaré par la plateforme : son canal, ses fonctionnalités
/// (<see cref="NotificationFeature"/>), les pays où il délivre (vide = tous) et la longueur maximale d'un contenu (null =
/// pas de limite connue). Le métier interroge CE modèle, jamais le code du prestataire. Remplacé en bloc (valeur immuable).
/// </summary>
public sealed class NotificationProviderCapability : ValueObject
{
    public const int MaxCountries = 100;
    public const int MaxContentLengthLimit = 1_000_000;

    private NotificationProviderCapability(
        NotificationChannel channel,
        IReadOnlyList<NotificationFeature> features,
        IReadOnlyList<CountryCode> countries,
        int? maxContentLength)
    {
        Channel = channel;
        Features = features;
        SupportedCountries = countries;
        MaxContentLength = maxContentLength;
    }

    public NotificationChannel Channel { get; }

    /// <summary>Au moins une fonctionnalité de contenu (texte ou HTML) ; toutes disponibles sur <see cref="Channel"/> ; triées.</summary>
    public IReadOnlyList<NotificationFeature> Features { get; }

    /// <summary>Vide = tous les pays ; triés.</summary>
    public IReadOnlyList<CountryCode> SupportedCountries { get; }

    /// <summary>En caractères ; null = pas de limite connue.</summary>
    public int? MaxContentLength { get; }

    /// <summary>Le prestataire confirme la remise : une notification peut atteindre le statut Delivered.</summary>
    public bool ReportsDelivery => Supports(NotificationFeature.DeliveryStatus);

    public static NotificationProviderCapability Create(
        NotificationChannel channel,
        IEnumerable<NotificationFeature> features,
        IEnumerable<CountryCode>? countries = null,
        int? maxContentLength = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        NotificationChannels.Require(channel);

        var featureList = features.Distinct().Order().ToArray();
        DomainException.ThrowIf(featureList.Any(feature => !Enum.IsDefined(feature)), NotificationProviderErrors.FeatureUnknown);
        DomainException.ThrowIf(!featureList.Any(feature => feature.CarriesContent()), NotificationProviderErrors.ContentFeatureMissing);
        DomainException.ThrowIf(
            featureList.Any(feature => !feature.IsAvailableOn(channel)),
            NotificationProviderErrors.FeatureNotForChannel);

        var countryList = (countries ?? Enumerable.Empty<CountryCode>())
            .Distinct()
            .OrderBy(country => country.Value, StringComparer.Ordinal)
            .ToArray();
        DomainException.ThrowIf(countryList.Length > MaxCountries, NotificationProviderErrors.TooManyCountries);
        DomainException.ThrowIf(maxContentLength is < 1 or > MaxContentLengthLimit, NotificationProviderErrors.MaxContentLengthInvalid);

        return new NotificationProviderCapability(channel, Array.AsReadOnly(featureList), Array.AsReadOnly(countryList), maxContentLength);
    }

    public bool Supports(NotificationFeature feature) => Features.Contains(feature);

    public bool Supports(CountryCode country)
    {
        ArgumentNullException.ThrowIfNull(country);
        return SupportedCountries.Count == 0 || SupportedCountries.Contains(country);
    }

    /// <summary>Un contenu de cette longueur passe-t-il chez le prestataire ?</summary>
    public bool Accepts(int contentLength) => contentLength >= 0 && (MaxContentLength is null || contentLength <= MaxContentLength);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Channel;
        foreach (var feature in Features)
        {
            yield return feature;
        }

        yield return "|";
        foreach (var country in SupportedCountries)
        {
            yield return country;
        }

        yield return "|";
        yield return MaxContentLength;
    }
}
