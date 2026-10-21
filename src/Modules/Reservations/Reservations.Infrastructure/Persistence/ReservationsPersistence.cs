using Reservations.Application.Abstractions;
using Reservations.Domain;

namespace Reservations.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.

internal sealed class ReservationRepository(ReservationsDbContext context) : IReservationRepository
{
    private static readonly ReservationStatus[] HoldingStatuses = [.. ReservationStatusExtensions.CapacityHoldingStatuses];

    public Task<Reservation?> FindAsync(TenantId tenantId, ReservationId id, CancellationToken cancellationToken = default) =>
        context.Reservations.SingleOrDefaultAsync(reservation => reservation.TenantId == tenantId && reservation.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Reservation>> ListHoldingCapacityAsync(
        TenantId tenantId, LocationId locationId, Period window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        // Lecture par le début (index) : le chevauchement exact est calculé par le domaine.
        return await context.Reservations.AsNoTracking()
            .Where(reservation => reservation.TenantId == tenantId
                && reservation.LocationId == locationId
                && HoldingStatuses.Contains(reservation.Status)
                && reservation.StartAtUtc >= window.StartUtc
                && reservation.StartAtUtc < window.EndUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Reservation>> ListAsync(
        TenantId tenantId, LocationId? locationId, Period window, ReservationStatus? status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        var query = context.Reservations.AsNoTracking()
            .Where(reservation => reservation.TenantId == tenantId
                && reservation.StartAtUtc >= window.StartUtc
                && reservation.StartAtUtc < window.EndUtc);
        if (locationId is { } location)
        {
            query = query.Where(reservation => reservation.LocationId == location);
        }

        if (status is { } wanted)
        {
            query = query.Where(reservation => reservation.Status == wanted);
        }

        return await query.OrderBy(reservation => reservation.StartAtUtc).ThenBy(reservation => reservation.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Reservation>> ListForCustomerAsync(
        TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        await context.Reservations
            .Where(reservation => reservation.TenantId == tenantId && reservation.CustomerId == customerId)
            .ToListAsync(cancellationToken);

    public async Task<ReservationPage> ListPageForCustomerAsync(
        TenantId tenantId, CustomerId customerId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Reservations.AsNoTracking()
            .Where(reservation => reservation.TenantId == tenantId && reservation.CustomerId == customerId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(reservation => reservation.StartAtUtc)
            .ThenByDescending(reservation => reservation.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new ReservationPage(items, total);
    }

    public void Add(Reservation reservation) => context.Reservations.Add(reservation);
}

internal sealed class ReservationPolicyStore(ReservationsDbContext context) : IReservationPolicyStore
{
    public async Task<ReservationPolicy> GetAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        (await FindAsync(tenantId, locationId, cancellationToken))?.ToPolicy() ?? ReservationPolicy.Default;

    public async Task SetAsync(TenantId tenantId, LocationId locationId, ReservationPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var row = await FindAsync(tenantId, locationId, cancellationToken);
        if (row is null)
        {
            row = new ReservationPolicyRow { TenantId = tenantId, LocationId = locationId };
            context.Policies.Add(row);
        }

        row.CopyFrom(policy);
    }

    public async Task<DepositRule> GetDepositRuleAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        (await FindAsync(tenantId, locationId, cancellationToken))?.ToDepositRule() ?? DepositRule.Off;

    public async Task SetDepositRuleAsync(TenantId tenantId, LocationId locationId, DepositRule rule, CancellationToken cancellationToken = default) =>
        (await RowAsync(tenantId, locationId, cancellationToken)).CopyFrom(rule);

    public async Task<WaitlistSettings> GetWaitlistSettingsAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        (await FindAsync(tenantId, locationId, cancellationToken))?.ToWaitlistSettings() ?? WaitlistSettings.Default;

    public async Task SetWaitlistSettingsAsync(TenantId tenantId, LocationId locationId, WaitlistSettings settings, CancellationToken cancellationToken = default) =>
        (await RowAsync(tenantId, locationId, cancellationToken)).CopyFrom(settings);

    /// <summary>La ligne de l'établissement, créée avec la politique par défaut si elle n'existe pas encore.</summary>
    private async Task<ReservationPolicyRow> RowAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(tenantId, locationId, cancellationToken);
        if (row is null)
        {
            row = new ReservationPolicyRow { TenantId = tenantId, LocationId = locationId };
            row.CopyFrom(ReservationPolicy.Default);
            context.Policies.Add(row);
        }

        return row;
    }

    private Task<ReservationPolicyRow?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        context.Policies.SingleOrDefaultAsync(row => row.TenantId == tenantId && row.LocationId == locationId, cancellationToken);
}

/// <summary>
/// Verrou consultatif PostgreSQL de transaction, propre à (tenant, établissement) : relâché tout seul au commit ou à
/// l'abandon. La transaction est ouverte ici si besoin et validée par l'unit of work.
/// </summary>
internal sealed class ReservationCapacityLock(ReservationsDbContext context) : IReservationCapacityLock
{
    public async Task AcquireAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
        {
            await context.Database.BeginTransactionAsync(cancellationToken);
        }

        var key = $"reservations:capacity:{tenantId.Value}:{locationId.Value}";
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
}

internal sealed class WaitlistRepository(ReservationsDbContext context) : IWaitlistRepository
{
    private static readonly WaitlistStatus[] OpenStatuses = [WaitlistStatus.Waiting, WaitlistStatus.Offered];

    public Task<WaitlistEntry?> FindAsync(TenantId tenantId, WaitlistEntryId id, CancellationToken cancellationToken = default) =>
        context.WaitlistEntries.SingleOrDefaultAsync(entry => entry.TenantId == tenantId && entry.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WaitlistEntry>> ListOpenAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        await context.WaitlistEntries
            .Where(entry => entry.TenantId == tenantId && entry.LocationId == locationId && OpenStatuses.Contains(entry.Status))
            .OrderBy(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WaitlistEntry>> ListAsync(TenantId tenantId, LocationId? locationId, Period window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        var query = context.WaitlistEntries.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId && entry.DesiredStartUtc >= window.StartUtc && entry.DesiredStartUtc < window.EndUtc);
        if (locationId is { } location)
        {
            query = query.Where(entry => entry.LocationId == location);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WaitlistEntry>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        await context.WaitlistEntries.Where(entry => entry.TenantId == tenantId && entry.CustomerId == customerId).ToListAsync(cancellationToken);

    public void Add(WaitlistEntry entry) => context.WaitlistEntries.Add(entry);
}

/// <summary>Le job : tous tenants, déclaré (IgnoreQueryFilters). Ne renvoie que des (tenant, établissement, id).</summary>
internal sealed class ReservationsDueWork(ReservationsDbContext context) : IReservationsDueWork
{
    public async Task<IReadOnlyList<DueItem>> UnpaidDepositsAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default) =>
        (await context.Reservations.IgnoreQueryFilters().AsNoTracking()
            .Where(reservation => reservation.Status == ReservationStatus.Pending
                && reservation.DepositStatus == DepositStatus.AwaitingPayment
                && reservation.DepositDueBeforeUtc <= nowUtc)
            .OrderBy(reservation => reservation.DepositDueBeforeUtc)
            .Select(reservation => new { reservation.TenantId, reservation.LocationId, reservation.Id })
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(row => new DueItem(row.TenantId, row.LocationId, row.Id.Value))
        .ToList();

    public async Task<IReadOnlyList<DueItem>> WaitlistAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default)
    {
        // Dernière heure acceptée = souhaitée + souplesse : toute inscription ouverte dont l'heure souhaitée est passée est
        // candidate (index), le calcul exact se fait ensuite.
        var rows = await context.WaitlistEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(entry => (entry.Status == WaitlistStatus.Offered && entry.OfferExpiresAtUtc <= nowUtc)
                || ((entry.Status == WaitlistStatus.Waiting || entry.Status == WaitlistStatus.Offered) && entry.DesiredStartUtc <= nowUtc))
            .OrderBy(entry => entry.DesiredStartUtc)
            .Select(entry => new { entry.TenantId, entry.LocationId, entry.Id, entry.Status, entry.OfferExpiresAtUtc, entry.DesiredStartUtc, entry.FlexibilityMinutes })
            .Take(take * 4)
            .ToListAsync(cancellationToken);
        return rows
            .Where(row => (row.Status == WaitlistStatus.Offered && row.OfferExpiresAtUtc <= nowUtc) || row.DesiredStartUtc.AddMinutes(row.FlexibilityMinutes) <= nowUtc)
            .Take(take)
            .Select(row => new DueItem(row.TenantId, row.LocationId, row.Id.Value))
            .ToList();
    }
}
