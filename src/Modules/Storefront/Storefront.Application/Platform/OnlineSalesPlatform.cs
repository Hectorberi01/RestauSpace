using Storefront.Application.Abstractions;
using Storefront.Domain;

namespace Storefront.Application.Platform;

// --- Back-office de la plateforme : la vente en ligne de tous les restaurants. Lecture seule, des comptages ; aucune
// clé, aucun jeton, aucune donnée personnelle de client. Le nom du restaurant vient de Tenants, côté back-office.

public static class OnlineSalesPlatformErrors
{
    public const int MaxPeriodDays = 366;

    public static readonly DomainError PeriodInvalid =
        new("storefront.platform.period_invalid", $"The period must start before it ends and cover at most {MaxPeriodDays} days.");

    public static readonly DomainError TenantRequired = new("storefront.platform.tenant_required", "A restaurant is required.");
}

/// <summary>Commandes en ligne d'un canal (site, application, borne) sur la période.</summary>
/// <param name="Placed">Toutes les commandes passées.</param>
/// <param name="Handled">Prises en charge (acceptées automatiquement ou par le personnel).</param>
/// <param name="Waiting">En attente d'acceptation.</param>
/// <param name="Expired">Refusées faute d'acceptation avant l'échéance.</param>
/// <param name="CancelledByCustomer">Annulées par le client.</param>
/// <param name="PaidOnline">Payées en ligne (ou à payer en ligne) ; les autres se paient au comptoir.</param>
public sealed record OnlineChannelStatsDto(
    SalesAppKind Channel, int Placed, int Handled, int Waiting, int Expired, int CancelledByCustomer, int PaidOnline);

public sealed record SalesAppCountDto(SalesAppKind Kind, int Active, int Disabled);

/// <summary>Vue d'ensemble de la vente en ligne sur la plateforme.</summary>
/// <param name="TenantsWithApps">Restaurants ayant au moins une application de vente active.</param>
/// <param name="TenantsWithOrders">Restaurants ayant reçu au moins une commande en ligne sur la période.</param>
/// <param name="PausedLocations">Établissements dont la vente en ligne est en pause en ce moment.</param>
/// <param name="ActiveCustomerSessions">Clients connectés (sessions non expirées ni révoquées).</param>
/// <param name="CustomerSignIns">Connexions de clients sur la période.</param>
public sealed record OnlineSalesOverviewDto(
    DateOnly From,
    DateOnly To,
    int TenantsWithApps,
    int TenantsWithOrders,
    IReadOnlyList<SalesAppCountDto> Apps,
    int ActiveKiosks,
    int RevokedKiosks,
    int ConfiguredLocations,
    int PausedLocations,
    IReadOnlyList<OnlineChannelStatsDto> Channels,
    int ActiveCustomerSessions,
    int CustomerSignIns);

/// <summary>Une ligne par restaurant ayant au moins une application de vente (active ou non).</summary>
public sealed record OnlineSalesTenantDto(
    Guid TenantId,
    int WebApps,
    int MobileApps,
    int KioskApps,
    int DisabledApps,
    int ActiveKiosks,
    int ConfiguredLocations,
    int PausedLocations,
    int OrdersPlaced,
    int OrdersExpired,
    DateTimeOffset? LastOrderAtUtc,
    int ActiveCustomerSessions);

public sealed record PlatformSalesAppDto(Guid Id, string Name, SalesAppKind Kind, SalesAppStatus Status, DateTimeOffset CreatedAtUtc);

public sealed record PlatformKioskDto(
    Guid Id, Guid AppId, Guid LocationId, string Name, KioskDeviceStatus Status, DateTimeOffset PairedAtUtc, DateTimeOffset? RevokedAtUtc);

public sealed record PlatformLocationSettingsDto(
    Guid LocationId, bool IsPaused, IReadOnlyList<ServiceMode> ServiceModes, int? SlotCapacity, DateTimeOffset UpdatedAtUtc);

/// <summary>La vente en ligne d'un restaurant, vue par la plateforme.</summary>
public sealed record OnlineSalesTenantDetailDto(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<PlatformSalesAppDto> Apps,
    IReadOnlyList<PlatformKioskDto> Kiosks,
    IReadOnlyList<PlatformLocationSettingsDto> Locations,
    IReadOnlyList<OnlineChannelStatsDto> Channels,
    DateTimeOffset? LastOrderAtUtc,
    int ActiveCustomerSessions,
    int CustomerSignIns);

internal static class PlatformPeriod
{
    public static (DateTimeOffset From, DateTimeOffset To) Of(DateOnly from, DateOnly to)
    {
        DomainException.ThrowIf(to < from || to.DayNumber - from.DayNumber + 1 > OnlineSalesPlatformErrors.MaxPeriodDays, OnlineSalesPlatformErrors.PeriodInvalid);
        return (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    }

    private static readonly SalesAppKind[] Kinds = [SalesAppKind.Web, SalesAppKind.MobileApp, SalesAppKind.Kiosk];

    public static IReadOnlyList<OnlineChannelStatsDto> Channels(IEnumerable<PlatformOrderCount> counts)
    {
        var rows = counts.ToList();
        int Sum(SalesAppKind kind, Func<PlatformOrderCount, bool> filter) => rows.Where(row => row.Channel == kind && filter(row)).Sum(row => row.Count);
        return Kinds.Select(kind => new OnlineChannelStatsDto(
                kind,
                Sum(kind, _ => true),
                Sum(kind, row => row.State == OnlineOrderState.Handled),
                Sum(kind, row => row.State == OnlineOrderState.AwaitingAcceptance),
                Sum(kind, row => row.State == OnlineOrderState.Expired),
                Sum(kind, row => row.State == OnlineOrderState.CancelledByCustomer),
                Sum(kind, row => row.Payment == PaymentMode.Online)))
            .ToList();
    }
}

/// <summary>Plateforme : la vente en ligne de tous les restaurants (applications, bornes, établissements, commandes, clients).</summary>
public sealed record GetOnlineSalesOverviewQuery(DateOnly From, DateOnly To) : IQuery<OnlineSalesOverviewDto>;

public sealed class GetOnlineSalesOverviewHandler(IOnlineSalesPlatform platform, IClock clock)
    : IQueryHandler<GetOnlineSalesOverviewQuery, OnlineSalesOverviewDto>
{
    public async Task<OnlineSalesOverviewDto> HandleAsync(GetOnlineSalesOverviewQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (from, to) = PlatformPeriod.Of(query.From, query.To);
        var apps = await platform.AppsAsync(null, cancellationToken);
        var kiosks = await platform.KiosksAsync(null, cancellationToken);
        var settings = await platform.SettingsAsync(null, cancellationToken);
        var orders = await platform.OrdersAsync(null, from, to, cancellationToken);
        var sessions = await platform.CustomerSessionsAsync(null, clock.UtcNow, from, to, cancellationToken);
        return new OnlineSalesOverviewDto(
            query.From,
            query.To,
            apps.Where(app => app.Status == SalesAppStatus.Active).Select(app => app.TenantId).Distinct().Count(),
            orders.Where(row => row.Count > 0).Select(row => row.TenantId).Distinct().Count(),
            new[] { SalesAppKind.Web, SalesAppKind.MobileApp, SalesAppKind.Kiosk }
                .Select(kind => new SalesAppCountDto(
                    kind,
                    apps.Count(app => app.Kind == kind && app.Status == SalesAppStatus.Active),
                    apps.Count(app => app.Kind == kind && app.Status == SalesAppStatus.Disabled)))
                .ToList(),
            kiosks.Count(kiosk => kiosk.Status == KioskDeviceStatus.Active),
            kiosks.Count(kiosk => kiosk.Status == KioskDeviceStatus.Revoked),
            settings.Count,
            settings.Count(location => location.IsPaused),
            PlatformPeriod.Channels(orders),
            sessions.Sum(row => row.Active),
            sessions.Sum(row => row.Opened));
    }
}

/// <summary>Plateforme : les restaurants qui ont une application de vente, du plus actif au moins actif sur la période.</summary>
public sealed record ListOnlineSalesTenantsQuery(DateOnly From, DateOnly To, int Skip = 0, int Take = 50) : IQuery<IReadOnlyList<OnlineSalesTenantDto>>;

public sealed class ListOnlineSalesTenantsHandler(IOnlineSalesPlatform platform, IClock clock)
    : IQueryHandler<ListOnlineSalesTenantsQuery, IReadOnlyList<OnlineSalesTenantDto>>
{
    public async Task<IReadOnlyList<OnlineSalesTenantDto>> HandleAsync(ListOnlineSalesTenantsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1);
        var (from, to) = PlatformPeriod.Of(query.From, query.To);
        var apps = await platform.AppsAsync(null, cancellationToken);
        var kiosks = await platform.KiosksAsync(null, cancellationToken);
        var settings = await platform.SettingsAsync(null, cancellationToken);
        var orders = await platform.OrdersAsync(null, from, to, cancellationToken);
        var last = (await platform.LastOrdersAsync(null, cancellationToken)).ToDictionary(row => row.TenantId, row => row.PlacedAtUtc);
        var sessions = (await platform.CustomerSessionsAsync(null, clock.UtcNow, from, to, cancellationToken)).ToDictionary(row => row.TenantId, row => row.Active);

        return apps.GroupBy(app => app.TenantId)
            .Select(group =>
            {
                var tenant = group.Key;
                var active = group.Where(app => app.Status == SalesAppStatus.Active).ToList();
                var placed = orders.Where(row => row.TenantId == tenant).ToList();
                var locations = settings.Where(row => row.TenantId == tenant).ToList();
                return new OnlineSalesTenantDto(
                    tenant.Value,
                    active.Count(app => app.Kind == SalesAppKind.Web),
                    active.Count(app => app.Kind == SalesAppKind.MobileApp),
                    active.Count(app => app.Kind == SalesAppKind.Kiosk),
                    group.Count(app => app.Status == SalesAppStatus.Disabled),
                    kiosks.Count(kiosk => kiosk.TenantId == tenant && kiosk.Status == KioskDeviceStatus.Active),
                    locations.Count,
                    locations.Count(location => location.IsPaused),
                    placed.Sum(row => row.Count),
                    placed.Where(row => row.State == OnlineOrderState.Expired).Sum(row => row.Count),
                    last.TryGetValue(tenant, out var at) ? at : null,
                    sessions.GetValueOrDefault(tenant));
            })
            .OrderByDescending(row => row.OrdersPlaced)
            .ThenByDescending(row => row.LastOrderAtUtc)
            .ThenBy(row => row.TenantId)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToList();
    }
}

/// <summary>Plateforme : la vente en ligne d'un restaurant (applications, bornes, réglages par établissement, commandes).</summary>
public sealed record GetOnlineSalesTenantQuery(Guid TenantId, DateOnly From, DateOnly To) : IQuery<OnlineSalesTenantDetailDto>;

public sealed class GetOnlineSalesTenantHandler(IOnlineSalesPlatform platform, IClock clock)
    : IQueryHandler<GetOnlineSalesTenantQuery, OnlineSalesTenantDetailDto>
{
    public async Task<OnlineSalesTenantDetailDto> HandleAsync(GetOnlineSalesTenantQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        DomainException.ThrowIf(query.TenantId == Guid.Empty, OnlineSalesPlatformErrors.TenantRequired);
        var (from, to) = PlatformPeriod.Of(query.From, query.To);
        var tenant = TenantId.From(query.TenantId);
        var apps = await platform.AppsAsync(tenant, cancellationToken);
        var kiosks = await platform.KiosksAsync(tenant, cancellationToken);
        var settings = await platform.SettingsAsync(tenant, cancellationToken);
        var orders = await platform.OrdersAsync(tenant, from, to, cancellationToken);
        var last = (await platform.LastOrdersAsync(tenant, cancellationToken)).FirstOrDefault();
        var sessions = (await platform.CustomerSessionsAsync(tenant, clock.UtcNow, from, to, cancellationToken)).FirstOrDefault();
        return new OnlineSalesTenantDetailDto(
            query.TenantId,
            query.From,
            query.To,
            apps.OrderBy(app => app.Kind).ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(app => new PlatformSalesAppDto(app.Id.Value, app.Name, app.Kind, app.Status, app.CreatedAtUtc)).ToList(),
            kiosks.OrderBy(kiosk => kiosk.Status).ThenBy(kiosk => kiosk.PairedAtUtc)
                .Select(kiosk => new PlatformKioskDto(
                    kiosk.Id.Value, kiosk.AppId.Value, kiosk.LocationId.Value, kiosk.Name, kiosk.Status, kiosk.PairedAtUtc, kiosk.RevokedAtUtc))
                .ToList(),
            settings.OrderBy(location => location.LocationId.Value)
                .Select(location => new PlatformLocationSettingsDto(
                    location.LocationId.Value, location.IsPaused, location.ServiceModes, location.SlotCapacity, location.UpdatedAtUtc))
                .ToList(),
            PlatformPeriod.Channels(orders),
            last?.PlacedAtUtc,
            sessions?.Active ?? 0,
            sessions?.Opened ?? 0);
    }
}
