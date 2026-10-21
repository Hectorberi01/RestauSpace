using Storefront.Application.Abstractions;
using Storefront.Domain;

namespace Storefront.Application.Queries;

/// <summary>Console : les options ouvertes, les applications et les réglages de chaque établissement non archivé.</summary>
public sealed record GetStorefrontOverviewQuery : IQuery<StorefrontOverviewDto>;

public sealed class GetStorefrontOverviewHandler(StorefrontWorkspace workspace, IClock clock) : IQueryHandler<GetStorefrontOverviewQuery, StorefrontOverviewDto>
{
    public async Task<StorefrontOverviewDto> HandleAsync(GetStorefrontOverviewQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var access = new StorefrontAccessDto(
            await workspace.Entitlements.AllowsAsync(tenant, SalesAppKind.Web, cancellationToken),
            await workspace.Entitlements.AllowsAsync(tenant, SalesAppKind.MobileApp, cancellationToken),
            await workspace.Entitlements.AllowsAsync(tenant, SalesAppKind.Kiosk, cancellationToken));
        var apps = (await workspace.Apps.ListAsync(tenant, cancellationToken)).Select(SalesAppDto.From).ToList();
        var saved = (await workspace.Settings.ListAsync(tenant, cancellationToken)).ToDictionary(settings => settings.LocationId);
        var locations = (await workspace.Locations.ListAsync(tenant, clock.UtcNow, cancellationToken))
            .OrderBy(location => location.Name, StringComparer.CurrentCulture)
            .Select(location => saved.TryGetValue(location.LocationId, out var settings)
                ? OnlineOrderingSettingsDto.From(location, settings, true)
                : OnlineOrderingSettingsDto.From(location, OnlineOrderingSettings.Default(new LocationReference(tenant, location.LocationId), clock), false))
            .ToList();
        return new StorefrontOverviewDto(access, apps, locations);
    }
}

/// <summary>Une commande en ligne qui attend encore quelque chose : son paiement, ou d'être acceptée par le personnel.</summary>
/// <param name="Deadline">Au-delà, elle est annulée (impayée) ou refusée (non acceptée).</param>
public sealed record WaitingOnlineOrderDto(Guid OrderId, OnlineOrderState State, SalesAppKind Channel, PaymentMode Payment, DateTimeOffset Deadline);

/// <summary>Console : les commandes en ligne de l'établissement en attente de paiement ou d'acceptation.</summary>
public sealed record ListWaitingOnlineOrdersQuery(Guid LocationId) : IQuery<IReadOnlyList<WaitingOnlineOrderDto>>;

public sealed class ListWaitingOnlineOrdersHandler(StorefrontWorkspace workspace, IOnlineOrderRepository onlineOrders)
    : IQueryHandler<ListWaitingOnlineOrdersQuery, IReadOnlyList<WaitingOnlineOrderDto>>
{
    public async Task<IReadOnlyList<WaitingOnlineOrderDto>> HandleAsync(ListWaitingOnlineOrdersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        return (await onlineOrders.ListWaitingAsync(workspace.CurrentTenant, LocationId.From(query.LocationId), cancellationToken))
            .Select(order => new WaitingOnlineOrderDto(order.OrderId.Value, order.State, order.Channel, order.PaymentMode, order.NextDeadlineUtc!.Value))
            .ToList();
    }
}

/// <summary>
/// Requête publique authentifiée par clé : retrouve l'application par l'empreinte de sa clé, refuse une application
/// désactivée, une origine non déclarée ou un restaurant qui n'accepte plus d'écritures. Null : refusée (une seule
/// réponse, sans dire pourquoi). La clé n'apparaît jamais dans un journal ni une exception.
/// </summary>
/// <param name="DeviceToken">Borne : son jeton d'appareil (en-tête X-Kiosk-Device). Faux ou révoqué : la clé est refusée.</param>
public sealed record AuthenticatePublishableKeyQuery(string? Key, string? Origin, string? DeviceToken = null, string? CustomerToken = null)
    : IQuery<StorefrontKeyPrincipal?>
{
    public override string ToString() => "AuthenticatePublishableKeyQuery (***)";
}

/// <param name="DeviceId">La borne appairée ; null pour un site, une application ou une borne qui s'appaire.</param>
/// <param name="CustomerSessionId">Client connecté : sa session active, de cette application ; null pour un invité.</param>
public sealed record StorefrontKeyPrincipal(
    Guid TenantId, Guid AppId, SalesAppKind Kind, Guid? DeviceId = null, Guid? DeviceLocationId = null, Guid? CustomerSessionId = null, Guid? CustomerId = null);

public sealed class AuthenticatePublishableKeyHandler(
    ISalesAppKeyLookup lookup, IStorefrontTenants tenants, IKioskDeviceLookup devices, ICustomerSessionLookup sessions, IClock clock)
    : IQueryHandler<AuthenticatePublishableKeyQuery, StorefrontKeyPrincipal?>
{
    public async Task<StorefrontKeyPrincipal?> HandleAsync(AuthenticatePublishableKeyQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!PublishableKeys.LooksValid(query.Key))
        {
            return null;
        }

        var app = await lookup.FindByKeyHashAsync(PublishableKeys.Hash(query.Key!), cancellationToken);
        if (app is not { IsActive: true } || !app.AcceptsOrigin(query.Origin)
            || await tenants.FindAsync(app.TenantId, cancellationToken) is not { AcceptsBusinessWrites: true })
        {
            return null;
        }

        if (query.DeviceToken is null)
        {
            // Un jeton client faux, expiré, révoqué ou d'une autre application n'empêche pas de commander en invité.
            var session = app.Kind != SalesAppKind.Kiosk && Customers.CustomerSecrets.LooksLikeToken(query.CustomerToken)
                ? await sessions.FindByTokenHashAsync(Customers.CustomerSecrets.Hash(query.CustomerToken!), cancellationToken)
                : null;
            return session is not null && session.AppId == app.Id && session.TenantId == app.TenantId && session.IsActive(clock.UtcNow)
                ? new StorefrontKeyPrincipal(app.TenantId.Value, app.Id.Value, app.Kind, CustomerSessionId: session.Id.Value, CustomerId: session.CustomerId.Value)
                : new StorefrontKeyPrincipal(app.TenantId.Value, app.Id.Value, app.Kind);
        }

        // Un jeton de borne n'est accepté qu'avec la clé de SON application, et tant qu'elle n'est pas révoquée.
        if (app.Kind != SalesAppKind.Kiosk || !Kiosks.KioskSecrets.LooksLikeToken(query.DeviceToken)
            || await devices.FindByTokenHashAsync(Kiosks.KioskSecrets.Hash(query.DeviceToken), cancellationToken) is not { IsActive: true } device
            || device.AppId != app.Id || !app.Serves(device.LocationId))
        {
            return null;
        }

        return new StorefrontKeyPrincipal(app.TenantId.Value, app.Id.Value, app.Kind, device.Id.Value, device.LocationId.Value);
    }
}

/// <summary>Application de vente : le restaurant, l'application et ses établissements, avec ce qu'ils proposent pour son canal.</summary>
public sealed record GetStorefrontQuery : IQuery<StorefrontDto>;

public sealed class GetStorefrontHandler(
    StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontDelivery delivery, IClock clock, IHumanCheck? humanCheck = null)
    : IQueryHandler<GetStorefrontQuery, StorefrontDto>
{
    public async Task<StorefrontDto> HandleAsync(GetStorefrontQuery query, CancellationToken cancellationToken = default)
    {
        var (app, restaurant, enabled) = await StorefrontReader.ContextAsync(workspace, client, cancellationToken);
        var locations = await StorefrontReader.LocationsAsync(workspace, client, delivery, app, enabled, clock.UtcNow, cancellationToken);
        return new StorefrontDto(
            new StorefrontRestaurantDto(
                restaurant.Name,
                restaurant.LogoUrl,
                restaurant.PrimaryColor,
                restaurant.CurrencyCode,
                restaurant.PricesIncludeTax,
                restaurant.Profile?.Tagline,
                restaurant.Profile?.Description,
                restaurant.Profile?.CoverImageUrl,
                restaurant.Profile?.Links ?? []),
            new StorefrontAppDto(app.Id.Value, app.Name, app.Kind, app.RequiresHumanCheck && humanCheck is { IsAvailable: true } ? humanCheck.SiteKey : null),
            enabled,
            locations);
    }
}

/// <summary>Application de vente : un établissement qu'elle sert (404 sinon, même s'il existe ailleurs).</summary>
public sealed record GetStorefrontLocationQuery(Guid LocationId) : IQuery<StorefrontLocationDto>;

public sealed class GetStorefrontLocationHandler(StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontDelivery delivery, IClock clock)
    : IQueryHandler<GetStorefrontLocationQuery, StorefrontLocationDto>
{
    public async Task<StorefrontLocationDto> HandleAsync(GetStorefrontLocationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (app, _, enabled) = await StorefrontReader.ContextAsync(workspace, client, cancellationToken);
        return (await StorefrontReader.LocationsAsync(workspace, client, delivery, app, enabled, clock.UtcNow, cancellationToken))
            .SingleOrDefault(location => location.Id == query.LocationId)
            ?? throw new NotFoundException(StorefrontApplicationErrors.LocationNotFound);
    }
}

internal static class StorefrontReader
{
    public static async Task<(SalesApp App, StorefrontRestaurant Restaurant, bool Enabled)> ContextAsync(
        StorefrontWorkspace workspace, IStorefrontClient client, CancellationToken cancellationToken)
    {
        var appId = client.AppId ?? throw new DomainException(StorefrontApplicationErrors.ClientRequired);
        var tenant = workspace.CurrentTenant;
        var app = await workspace.Apps.FindAsync(tenant, appId, cancellationToken)
            ?? throw new NotFoundException(StorefrontApplicationErrors.AppNotFound);
        DomainException.ThrowIf(app.Kind == SalesAppKind.Kiosk && client.DeviceId is null, Kiosks.KioskApplicationErrors.DeviceRequired);
        var restaurant = await workspace.Tenants.FindAsync(tenant, cancellationToken)
            ?? throw new NotFoundException(StorefrontApplicationErrors.AppNotFound);
        var enabled = app.IsActive && restaurant.AcceptsBusinessWrites && await workspace.Entitlements.AllowsAsync(tenant, app.Kind, cancellationToken);
        return (app, restaurant, enabled);
    }

    /// <summary>L'application de la clé (sans exiger le jeton d'une borne).</summary>
    public static async Task<SalesApp> AppAsync(StorefrontWorkspace workspace, IStorefrontClient client, CancellationToken cancellationToken)
    {
        var appId = client.AppId ?? throw new DomainException(StorefrontApplicationErrors.ClientRequired);
        return await workspace.Apps.FindAsync(workspace.CurrentTenant, appId, cancellationToken)
            ?? throw new NotFoundException(StorefrontApplicationErrors.AppNotFound);
    }

    /// <summary>L'application sert cet établissement ; une borne, seulement le sien.</summary>
    public static bool Serves(SalesApp app, IStorefrontClient client, LocationId locationId) =>
        app.Serves(locationId) && (client.DeviceLocationId is not { } own || own == locationId);

    public static async Task<IReadOnlyList<StorefrontLocationDto>> LocationsAsync(
        StorefrontWorkspace workspace,
        IStorefrontClient client,
        IStorefrontDelivery delivery,
        SalesApp app,
        bool enabled,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var tenant = workspace.CurrentTenant;
        var saved = (await workspace.Settings.ListAsync(tenant, cancellationToken)).ToDictionary(settings => settings.LocationId);
        var clock = new FixedInstant(nowUtc);
        var served = (await workspace.Locations.ListAsync(tenant, nowUtc, cancellationToken))
            .Where(location => Serves(app, client, location.LocationId))
            .OrderBy(location => location.Name, StringComparer.CurrentCulture)
            .ToList();
        var result = new List<StorefrontLocationDto>(served.Count);
        foreach (var location in served)
        {
            var settings = saved.GetValueOrDefault(location.LocationId)
                ?? OnlineOrderingSettings.Default(new LocationReference(tenant, location.LocationId), clock);
            var policy = settings.PolicyFor(app.Kind);
            var offer = await Checkout.CheckoutPricing.OfferAsync(tenant, app.Kind, settings, location.LocationId, delivery, cancellationToken);
            var modes = offer is null ? settings.ServiceModes.Where(mode => mode != ServiceMode.Delivery).ToList() : settings.ServiceModes.ToList();
            result.Add(new StorefrontLocationDto(
                location.LocationId.Value,
                location.Name,
                location.Street,
                location.City,
                location.TimeZoneId,
                location.IsOpenNow,
                location.NextOpeningUtc,
                location.WeeklyHours,
                settings.IsPaused,
                enabled && location.IsOperational && location.IsOpenNow && !settings.IsPaused,
                modes,
                settings.PreparationMinutes,
                settings.MinimumOrderAmount,
                new StorefrontPaymentDto(policy.PayOnline, policy.PayAtCounter, policy.CounterLimit),
                offer is null
                    ? null
                    : new StorefrontDeliveryDto(
                        offer.LeadMinutes,
                        offer.Zones.Select(zone => new StorefrontDeliveryZoneDto(zone.ZoneId, zone.Name, zone.Fee, zone.MinimumOrder, zone.EstimatedMinutes)).ToList()),
                settings.SlotCapacity,
                StorefrontPlaceDto.From(location.Place),
                location.SpecialDays ?? [],
                location.TemporaryClosureReason));
        }

        return result;
    }

    private sealed class FixedInstant(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
