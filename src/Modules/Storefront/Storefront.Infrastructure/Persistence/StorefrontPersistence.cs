using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;
using Storefront.Application;
using Storefront.Application.Abstractions;
using Storefront.Domain;

namespace Storefront.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte. La seule lecture sans filtre de tenant
// est l'authentification par clé publique, jeton de borne ou jeton client (SalesAppKeyLookup, KioskDeviceLookup,
// CustomerSessionLookup), et elle le déclare.

internal sealed class SalesAppRepository(StorefrontDbContext context) : ISalesAppRepository
{
    public Task<SalesApp?> FindAsync(TenantId tenantId, SalesAppId id, CancellationToken cancellationToken = default) =>
        context.SalesApps.SingleOrDefaultAsync(app => app.TenantId == tenantId && app.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SalesApp>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.SalesApps.AsNoTracking().Where(app => app.TenantId == tenantId).OrderBy(app => app.Name).ToListAsync(cancellationToken);

    public void Add(SalesApp app) => context.SalesApps.Add(app);
}

/// <summary>Tous tenants (la requête publique n'a pas encore de tenant), lecture seule, par l'empreinte unique de la clé.</summary>
internal sealed class SalesAppKeyLookup(StorefrontDbContext context) : ISalesAppKeyLookup
{
    public Task<SalesApp?> FindByKeyHashAsync(PublishableKeyHash keyHash, CancellationToken cancellationToken = default) =>
        context.SalesApps.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(app => app.KeyHash == keyHash, cancellationToken);
}

internal sealed class OnlineOrderingSettingsRepository(StorefrontDbContext context) : IOnlineOrderingSettingsRepository
{
    public async Task<OnlineOrderingSettings?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        context.OnlineOrderingSettings.Local.FirstOrDefault(settings => settings.TenantId == tenantId && settings.LocationId == locationId)
        ?? await context.OnlineOrderingSettings.SingleOrDefaultAsync(
            settings => settings.TenantId == tenantId && settings.LocationId == locationId, cancellationToken);

    public async Task<IReadOnlyList<OnlineOrderingSettings>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.OnlineOrderingSettings.AsNoTracking().Where(settings => settings.TenantId == tenantId).ToListAsync(cancellationToken);

    public void Add(OnlineOrderingSettings settings) => context.OnlineOrderingSettings.Add(settings);
}

internal sealed class OnlineOrderRepository(StorefrontDbContext context) : IOnlineOrderRepository
{
    public Task<OnlineOrder?> FindAsync(TenantId tenantId, OnlineOrderId id, CancellationToken cancellationToken = default) =>
        context.OnlineOrders.SingleOrDefaultAsync(order => order.TenantId == tenantId && order.Id == id, cancellationToken);

    public Task<OnlineOrder?> FindByOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
        context.OnlineOrders.SingleOrDefaultAsync(order => order.TenantId == tenantId && order.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<OnlineOrder>> ListWaitingAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        await context.OnlineOrders.AsNoTracking()
            .Where(order => order.TenantId == tenantId && order.LocationId == locationId && order.NextDeadlineUtc != null)
            .OrderBy(order => order.NextDeadlineUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

    public void Add(OnlineOrder order) => context.OnlineOrders.Add(order);

    public Task<int> CountInSlotAsync(TenantId tenantId, LocationId locationId, DateTimeOffset slotStartUtc, CancellationToken cancellationToken = default) =>
        HoldingSlots(tenantId, locationId).CountAsync(order => order.SlotStartUtc == slotStartUtc, cancellationToken);

    public async Task<IReadOnlyDictionary<DateTimeOffset, int>> CountBySlotAsync(
        TenantId tenantId, LocationId locationId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
        (await HoldingSlots(tenantId, locationId)
            .Where(order => order.SlotStartUtc >= fromUtc && order.SlotStartUtc < toUtc)
            .GroupBy(order => order.SlotStartUtc!.Value)
            .Select(group => new { Slot = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken))
        .ToDictionary(row => row.Slot, row => row.Count);

    // Refusées faute d'acceptation ou annulées par le client : elles libèrent leur créneau.
    private IQueryable<OnlineOrder> HoldingSlots(TenantId tenantId, LocationId locationId) =>
        context.OnlineOrders.AsNoTracking().Where(order =>
            order.TenantId == tenantId && order.LocationId == locationId && order.SlotStartUtc != null
            && order.State != OnlineOrderState.Expired && order.State != OnlineOrderState.CancelledByCustomer);
}

/// <summary>Le job : tous tenants, déclaré (IgnoreQueryFilters). Ne renvoie que des (tenant, id).</summary>
internal sealed class OverdueOnlineOrders(StorefrontDbContext context) : IOverdueOnlineOrders
{
    public async Task<IReadOnlyList<OverdueOnlineOrder>> ListAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default) =>
        (await context.OnlineOrders.IgnoreQueryFilters().AsNoTracking()
            .Where(order => order.NextDeadlineUtc != null && order.NextDeadlineUtc <= nowUtc)
            .OrderBy(order => order.NextDeadlineUtc)
            .Select(order => new { order.TenantId, order.Id })
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(row => new OverdueOnlineOrder(row.TenantId, row.Id))
        .ToList();
}

internal sealed class KioskDeviceRepository(StorefrontDbContext context) : IKioskDeviceRepository
{
    public Task<KioskDevice?> FindAsync(TenantId tenantId, KioskDeviceId id, CancellationToken cancellationToken = default) =>
        context.KioskDevices.SingleOrDefaultAsync(device => device.TenantId == tenantId && device.Id == id, cancellationToken);

    public async Task<IReadOnlyList<KioskDevice>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.KioskDevices.AsNoTracking().Where(device => device.TenantId == tenantId).OrderBy(device => device.PairedAtUtc).ToListAsync(cancellationToken);

    public Task<int> CountActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        context.KioskDevices.CountAsync(device => device.TenantId == tenantId && device.Status == KioskDeviceStatus.Active, cancellationToken);

    public void Add(KioskDevice device) => context.KioskDevices.Add(device);
}

/// <summary>Tous tenants (la requête de la borne n'a pas encore de tenant), lecture seule, par l'empreinte unique du jeton.</summary>
internal sealed class KioskDeviceLookup(StorefrontDbContext context) : IKioskDeviceLookup
{
    public Task<KioskDevice?> FindByTokenHashAsync(PublishableKeyHash tokenHash, CancellationToken cancellationToken = default) =>
        context.KioskDevices.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(device => device.TokenHash == tokenHash, cancellationToken);
}

internal sealed class KioskPairingRepository(StorefrontDbContext context) : IKioskPairingRepository
{
    public Task<KioskPairing?> FindAsync(TenantId tenantId, KioskPairingId id, CancellationToken cancellationToken = default) =>
        context.KioskPairings.SingleOrDefaultAsync(pairing => pairing.TenantId == tenantId && pairing.Id == id, cancellationToken);

    public Task<KioskPairing?> FindPendingByCodeAsync(TenantId tenantId, PublishableKeyHash codeHash, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        context.KioskPairings
            .Where(pairing => pairing.TenantId == tenantId && pairing.CodeHash == codeHash && pairing.Status == KioskPairingStatus.Pending && pairing.ExpiresAtUtc > nowUtc)
            .OrderByDescending(pairing => pairing.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountApprovedAsync(TenantId tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        context.KioskPairings.CountAsync(
            pairing => pairing.TenantId == tenantId && pairing.Status == KioskPairingStatus.Approved && pairing.ExpiresAtUtc > nowUtc, cancellationToken);

    public void Add(KioskPairing pairing) => context.KioskPairings.Add(pairing);
}

internal sealed class CustomerSessionRepository(StorefrontDbContext context) : ICustomerSessionRepository
{
    public Task<CustomerSession?> FindAsync(TenantId tenantId, CustomerSessionId id, CancellationToken cancellationToken = default) =>
        context.CustomerSessions.SingleOrDefaultAsync(session => session.TenantId == tenantId && session.Id == id, cancellationToken);

    public void Add(CustomerSession session) => context.CustomerSessions.Add(session);
}

/// <summary>Tous tenants (la requête du client n'a pas encore de tenant), lecture seule, par l'empreinte unique du jeton.</summary>
internal sealed class CustomerSessionLookup(StorefrontDbContext context) : ICustomerSessionLookup
{
    public Task<CustomerSession?> FindByTokenHashAsync(PublishableKeyHash tokenHash, CancellationToken cancellationToken = default) =>
        context.CustomerSessions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(session => session.TokenHash == tokenHash, cancellationToken);
}

internal sealed class StorefrontUnitOfWork(StorefrontDbContext context) : OutboxUnitOfWork<StorefrontDbContext>(context), IStorefrontUnitOfWork
{
    protected override DomainError ConcurrentUpdate => StorefrontApplicationErrors.ConcurrentUpdate;

    /// <summary>Deux clés identiques (impossible en pratique) ou deux créations simultanées des réglages d'un établissement.</summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: SalesAppConfiguration.KeyHashIndex or KioskDeviceConfiguration.TokenHashIndex or CustomerSessionConfiguration.TokenHashIndex or OnlineOrderingSettingsConfiguration.LocationIndex or OnlineOrderConfiguration.OrderIndex,
        }
            ? new ConcurrencyException(StorefrontApplicationErrors.ConcurrentUpdate)
            : null;
}

/// <summary>
/// Back-office de la plateforme : tous tenants, déclaré (IgnoreQueryFilters), lecture seule. Projette des comptages et
/// des noms : jamais une empreinte de clé ou de jeton, jamais une donnée personnelle.
/// </summary>
internal sealed class OnlineSalesPlatform(StorefrontDbContext context) : IOnlineSalesPlatform
{
    public async Task<IReadOnlyList<PlatformSalesApp>> AppsAsync(TenantId? tenantId, CancellationToken cancellationToken = default)
    {
        var query = context.SalesApps.IgnoreQueryFilters().AsNoTracking();
        if (tenantId is { } tenant)
        {
            query = query.Where(app => app.TenantId == tenant);
        }

        return (await query.Select(app => new { app.TenantId, app.Id, app.Name, app.Kind, app.Status, app.CreatedAtUtc }).ToListAsync(cancellationToken))
            .Select(app => new PlatformSalesApp(app.TenantId, app.Id, app.Name, app.Kind, app.Status, app.CreatedAtUtc))
            .ToList();
    }

    public async Task<IReadOnlyList<PlatformKiosk>> KiosksAsync(TenantId? tenantId, CancellationToken cancellationToken = default)
    {
        var query = context.KioskDevices.IgnoreQueryFilters().AsNoTracking();
        if (tenantId is { } tenant)
        {
            query = query.Where(kiosk => kiosk.TenantId == tenant);
        }

        return (await query
                .Select(kiosk => new { kiosk.TenantId, kiosk.Id, kiosk.AppId, kiosk.LocationId, kiosk.Name, kiosk.Status, kiosk.PairedAtUtc, kiosk.RevokedAtUtc })
                .ToListAsync(cancellationToken))
            .Select(kiosk => new PlatformKiosk(
                kiosk.TenantId, kiosk.Id, kiosk.AppId, kiosk.LocationId, kiosk.Name, kiosk.Status, kiosk.PairedAtUtc, kiosk.RevokedAtUtc))
            .ToList();
    }

    public async Task<IReadOnlyList<PlatformOrderCount>> OrdersAsync(
        TenantId? tenantId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var query = context.OnlineOrders.IgnoreQueryFilters().AsNoTracking().Where(order => order.PlacedAtUtc >= fromUtc && order.PlacedAtUtc < toUtc);
        if (tenantId is { } tenant)
        {
            query = query.Where(order => order.TenantId == tenant);
        }

        return (await query
                .GroupBy(order => new { order.TenantId, order.Channel, order.PaymentMode, order.State })
                .Select(group => new { group.Key.TenantId, group.Key.Channel, group.Key.PaymentMode, group.Key.State, Count = group.Count() })
                .ToListAsync(cancellationToken))
            .Select(row => new PlatformOrderCount(row.TenantId, row.Channel, row.PaymentMode, row.State, row.Count))
            .ToList();
    }

    public async Task<IReadOnlyList<PlatformLastOrder>> LastOrdersAsync(TenantId? tenantId, CancellationToken cancellationToken = default)
    {
        var query = context.OnlineOrders.IgnoreQueryFilters().AsNoTracking();
        if (tenantId is { } tenant)
        {
            query = query.Where(order => order.TenantId == tenant);
        }

        return (await query
                .GroupBy(order => order.TenantId)
                .Select(group => new { TenantId = group.Key, Last = group.Max(order => order.PlacedAtUtc) })
                .ToListAsync(cancellationToken))
            .Select(row => new PlatformLastOrder(row.TenantId, row.Last))
            .ToList();
    }

    public async Task<IReadOnlyList<PlatformLocationSettings>> SettingsAsync(TenantId? tenantId, CancellationToken cancellationToken = default)
    {
        var query = context.OnlineOrderingSettings.IgnoreQueryFilters().AsNoTracking();
        if (tenantId is { } tenant)
        {
            query = query.Where(settings => settings.TenantId == tenant);
        }

        return (await query.ToListAsync(cancellationToken))
            .Select(settings => new PlatformLocationSettings(
                settings.TenantId, settings.LocationId, settings.IsPaused, settings.ServiceModes.ToList(), settings.SlotCapacity, settings.UpdatedAtUtc))
            .ToList();
    }

    public async Task<IReadOnlyList<PlatformCustomerSessions>> CustomerSessionsAsync(
        TenantId? tenantId, DateTimeOffset nowUtc, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var query = context.CustomerSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(session => (session.RevokedAtUtc == null && session.ExpiresAtUtc > nowUtc) || (session.CreatedAtUtc >= fromUtc && session.CreatedAtUtc < toUtc));
        if (tenantId is { } tenant)
        {
            query = query.Where(session => session.TenantId == tenant);
        }

        return (await query
                .GroupBy(session => session.TenantId)
                .Select(group => new
                {
                    TenantId = group.Key,
                    Active = group.Count(session => session.RevokedAtUtc == null && session.ExpiresAtUtc > nowUtc),
                    Opened = group.Count(session => session.CreatedAtUtc >= fromUtc && session.CreatedAtUtc < toUtc),
                })
                .ToListAsync(cancellationToken))
            .Select(row => new PlatformCustomerSessions(row.TenantId, row.Active, row.Opened))
            .ToList();
    }
}
