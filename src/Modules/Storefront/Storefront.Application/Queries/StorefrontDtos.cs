using Storefront.Application.Abstractions;
using Storefront.Domain;

namespace Storefront.Application.Queries;

/// <summary>Une application de vente vue dans la console : jamais sa clé, seulement ses 4 derniers caractères.</summary>
public sealed record SalesAppDto(
    Guid Id,
    string Name,
    SalesAppKind Kind,
    SalesAppStatus Status,
    string KeyPrefix,
    string KeyHint,
    DateTimeOffset KeyIssuedAtUtc,
    IReadOnlyList<string> AllowedOrigins,
    IReadOnlyList<Guid> LocationIds,
    IReadOnlyList<string> ReturnUrls,
    DateTimeOffset CreatedAtUtc,
    bool RequiresHumanCheck = false)
{
    public static SalesAppDto From(SalesApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return new SalesAppDto(
            app.Id.Value, app.Name, app.Kind, app.Status, PublishableKeys.Prefix, app.KeyHint, app.KeyIssuedAtUtc, app.AllowedOrigins,
            app.LocationIds.Select(id => id.Value).ToList(), app.ReturnUrls, app.CreatedAtUtc, app.RequiresHumanCheck);
    }
}

public sealed record ChannelPolicyDto(SalesAppKind Channel, bool PayOnline, bool PayAtCounter, decimal? CounterLimit, OrderAcceptance Acceptance)
{
    public static ChannelPolicyDto From(ChannelPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new ChannelPolicyDto(policy.Channel, policy.PayOnline, policy.PayAtCounter, policy.CounterLimit, policy.Acceptance);
    }
}

/// <summary>Les réglages de vente en ligne d'un établissement (ceux par défaut tant que rien n'est enregistré).</summary>
public sealed record OnlineOrderingSettingsDto(
    Guid LocationId,
    string LocationName,
    bool IsOperational,
    bool IsSaved,
    bool IsPaused,
    IReadOnlyList<ServiceMode> ServiceModes,
    int PreparationMinutes,
    decimal? MinimumOrderAmount,
    int ManualAcceptanceMinutes,
    int UnpaidExpiryMinutes,
    IReadOnlyList<ChannelPolicyDto> Policies,
    int? SlotCapacity)
{
    public static OnlineOrderingSettingsDto From(StorefrontLocation location, OnlineOrderingSettings settings, bool isSaved)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(settings);
        return new OnlineOrderingSettingsDto(
            location.LocationId.Value, location.Name, location.IsOperational, isSaved, settings.IsPaused, settings.ServiceModes,
            settings.PreparationMinutes, settings.MinimumOrderAmount, settings.ManualAcceptanceMinutes, settings.UnpaidExpiryMinutes,
            settings.Policies.Select(ChannelPolicyDto.From).ToList(), settings.SlotCapacity);
    }
}

/// <summary>Les canaux ouverts par les options du restaurant.</summary>
public sealed record StorefrontAccessDto(bool Web, bool MobileApp, bool Kiosk);

/// <summary>Tout l'écran « Vente en ligne » de la console en une lecture.</summary>
public sealed record StorefrontOverviewDto(
    StorefrontAccessDto Access, IReadOnlyList<SalesAppDto> Apps, IReadOnlyList<OnlineOrderingSettingsDto> Locations);

// ---- Vu par une application de vente (clé publique)

public sealed record StorefrontPaymentDto(bool PayOnline, bool PayAtCounter, decimal? CounterLimit);

/// <summary>Une zone de livraison : frais, minimum de commande (hors frais) et durée du trajet.</summary>
public sealed record StorefrontDeliveryZoneDto(Guid Id, string Name, decimal Fee, decimal MinimumOrder, int EstimatedMinutes);

/// <param name="LeadMinutes">Préparation avant le départ du livreur ; l'heure de livraison ajoute le trajet de la zone.</param>
public sealed record StorefrontDeliveryDto(int LeadMinutes, IReadOnlyList<StorefrontDeliveryZoneDto> Zones);

/// <summary>
/// Un établissement servi par l'application : adresse publique, ouverture (maintenant, prochaine), horaires, et ce
/// qu'il propose en ligne POUR CE CANAL. <see cref="AcceptsOrdersNow"/> : option active, établissement opérationnel et
/// ouvert, pas en pause. La livraison n'est dans <see cref="ServiceModes"/> que si l'établissement livre vraiment
/// (zones actives) ; ses zones sont dans <see cref="Delivery"/>. <see cref="Place"/> : code postal, pays, position et
/// coordonnées publiques ; <see cref="SpecialDays"/> : horaires exceptionnels des 60 prochains jours ;
/// <see cref="ClosureReason"/> : motif d'une fermeture temporaire en cours.
/// </summary>
public sealed record StorefrontLocationDto(
    Guid Id,
    string Name,
    string Street,
    string City,
    string TimeZoneId,
    bool IsOpenNow,
    DateTimeOffset? NextOpeningUtc,
    IReadOnlyList<StorefrontOpeningSlot> WeeklyHours,
    bool IsPaused,
    bool AcceptsOrdersNow,
    IReadOnlyList<ServiceMode> ServiceModes,
    int PreparationMinutes,
    decimal? MinimumOrderAmount,
    StorefrontPaymentDto Payment,
    StorefrontDeliveryDto? Delivery = null,
    int? SlotCapacity = null,
    StorefrontPlaceDto? Place = null,
    IReadOnlyList<StorefrontSpecialDay>? SpecialDays = null,
    string? ClosureReason = null);

/// <summary>Position de l'établissement (degrés décimaux, WGS 84).</summary>
public sealed record StorefrontCoordinatesDto(decimal Latitude, decimal Longitude);

/// <summary>Coordonnées publiques : téléphones au format international, chacune null si le restaurant ne l'a pas renseignée.</summary>
public sealed record StorefrontContactDto(string? Phone, string? WhatsApp, string? Email)
{
    public override string ToString() => "StorefrontContactDto(***)";
}

public sealed record StorefrontPlaceDto(string? PostalCode, string CountryCode, StorefrontCoordinatesDto? Coordinates, StorefrontContactDto Contact)
{
    public static StorefrontPlaceDto? From(StorefrontPlace? place) => place is null
        ? null
        : new StorefrontPlaceDto(
            place.PostalCode,
            place.CountryCode,
            place is {Latitude: { } latitude, Longitude: { } longitude} ? new StorefrontCoordinatesDto(latitude, longitude) : null,
            new StorefrontContactDto(place.Phone, place.WhatsApp, place.Email));
}

/// <param name="Tagline">Accroche courte (120 caractères au plus).</param>
/// <param name="Description">Présentation en texte brut ; les sauts de ligne séparent les paragraphes.</param>
/// <param name="CoverImageUrl">Photo de couverture (lien public).</param>
/// <param name="Links">Site et réseaux sociaux, au plus un par réseau.</param>
public sealed record StorefrontRestaurantDto(
    string Name,
    string? LogoUrl,
    string? PrimaryColor,
    string CurrencyCode,
    bool PricesIncludeTax,
    string? Tagline = null,
    string? Description = null,
    string? CoverImageUrl = null,
    IReadOnlyList<StorefrontLink>? Links = null);

/// <param name="HumanCheckSiteKey">Clé de site Turnstile quand l'application exige le contrôle anti-robot ; sinon null.</param>
public sealed record StorefrontAppDto(Guid Id, string Name, SalesAppKind Kind, string? HumanCheckSiteKey = null);

/// <summary>Ce qu'une application de vente lit au démarrage : le restaurant, elle-même, et ses établissements.</summary>
public sealed record StorefrontDto(
    StorefrontRestaurantDto Restaurant, StorefrontAppDto App, bool OrderingEnabled, IReadOnlyList<StorefrontLocationDto> Locations);
