using Delivery.Application;
using Delivery.Application.Abstractions;
using Delivery.Domain;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Delivery.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.

internal sealed class DeliverySettingsRepository(DeliveryDbContext context) : IDeliverySettingsRepository
{
    public Task<DeliverySettings?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        context.Settings
            .Include(settings => settings.Zones)
            .SingleOrDefaultAsync(settings => settings.TenantId == tenantId && settings.LocationId == locationId, cancellationToken);

    public void Add(DeliverySettings settings) => context.Settings.Add(settings);
}

internal sealed class FulfillmentRepository(DeliveryDbContext context) : IFulfillmentRepository
{
    public Task<Fulfillment?> FindAsync(TenantId tenantId, FulfillmentId id, CancellationToken cancellationToken = default) =>
        context.Fulfillments.SingleOrDefaultAsync(fulfillment => fulfillment.TenantId == tenantId && fulfillment.Id == id, cancellationToken);

    public Task<Fulfillment?> FindActiveForOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
        context.Fulfillments.SingleOrDefaultAsync(
            fulfillment => fulfillment.TenantId == tenantId && fulfillment.OrderId == orderId && fulfillment.Status != FulfillmentStatus.Cancelled,
            cancellationToken);

    public async Task<IReadOnlyList<Fulfillment>> SearchAsync(
        TenantId tenantId, FulfillmentSearch search, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = context.Fulfillments.AsNoTracking().Where(fulfillment => fulfillment.TenantId == tenantId);
        if (search.LocationId is { } location)
        {
            query = query.Where(fulfillment => fulfillment.LocationId == location);
        }

        if (search.Mode is { } mode)
        {
            query = query.Where(fulfillment => fulfillment.Mode == mode);
        }

        if (search.Status is { } status)
        {
            query = query.Where(fulfillment => fulfillment.Status == status);
        }

        if (search.OpenOnly)
        {
            query = query.Where(fulfillment => fulfillment.Status != FulfillmentStatus.Completed && fulfillment.Status != FulfillmentStatus.Cancelled);
        }

        if (search.FromUtc is { } from)
        {
            query = query.Where(fulfillment => fulfillment.CreatedAtUtc >= from);
        }

        if (search.ToUtc is { } to)
        {
            query = query.Where(fulfillment => fulfillment.CreatedAtUtc < to);
        }

        return await query
            .OrderBy(fulfillment => fulfillment.PromisedAtUtc)
            .ThenBy(fulfillment => fulfillment.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Fulfillment>> ListAwaitingRemittanceAsync(
        TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default)
    {
        var query = context.Fulfillments.AsNoTracking().Where(fulfillment =>
            fulfillment.TenantId == tenantId
            && fulfillment.Mode == FulfillmentMode.Delivery
            && fulfillment.Status == FulfillmentStatus.Completed
            && fulfillment.RemittanceId == null
            && ((fulfillment.CollectionMethod == CollectionMethod.Cash && fulfillment.AmountCollected > 0m) || fulfillment.CourierFeeAmount > 0m));
        if (locationId is { } location)
        {
            query = query.Where(fulfillment => fulfillment.LocationId == location);
        }

        return await query.OrderBy(fulfillment => fulfillment.CompletedAtUtc).Take(1000).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Fulfillment>> ListForUpdateAsync(
        TenantId tenantId, IReadOnlyCollection<FulfillmentId> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.ToList();
        return await context.Fulfillments.Where(fulfillment => fulfillment.TenantId == tenantId && wanted.Contains(fulfillment.Id)).ToListAsync(cancellationToken);
    }

    public void Add(Fulfillment fulfillment) => context.Fulfillments.Add(fulfillment);
}

internal sealed class CourierRepository(DeliveryDbContext context) : ICourierRepository
{
    public Task<Courier?> FindAsync(TenantId tenantId, CourierId id, CancellationToken cancellationToken = default) =>
        context.Couriers.SingleOrDefaultAsync(courier => courier.TenantId == tenantId && courier.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Courier>> ListAsync(TenantId tenantId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = context.Couriers.AsNoTracking().Where(courier => courier.TenantId == tenantId);
        if (!includeInactive)
        {
            query = query.Where(courier => courier.IsActive);
        }

        return await query.OrderBy(courier => courier.Name).ThenBy(courier => courier.Id).Take(500).ToListAsync(cancellationToken);
    }

    public void Add(Courier courier) => context.Couriers.Add(courier);
}

internal sealed class CourierRemittanceRepository(DeliveryDbContext context) : ICourierRemittanceRepository
{
    public Task<CourierRemittance?> FindAsync(TenantId tenantId, CourierRemittanceId id, CancellationToken cancellationToken = default) =>
        context.Remittances.AsNoTracking().SingleOrDefaultAsync(remittance => remittance.TenantId == tenantId && remittance.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CourierRemittance>> SearchAsync(
        TenantId tenantId,
        LocationId? locationId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = context.Remittances.AsNoTracking().Where(remittance => remittance.TenantId == tenantId);
        if (locationId is { } location)
        {
            query = query.Where(remittance => remittance.LocationId == location);
        }

        if (fromUtc is { } from)
        {
            query = query.Where(remittance => remittance.RecordedAtUtc >= from);
        }

        if (toUtc is { } to)
        {
            query = query.Where(remittance => remittance.RecordedAtUtc < to);
        }

        return await query
            .OrderByDescending(remittance => remittance.RecordedAtUtc)
            .ThenByDescending(remittance => remittance.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public void Add(CourierRemittance remittance) => context.Remittances.Add(remittance);
}

internal sealed class DeliveryUnitOfWork(DeliveryDbContext context) : OutboxUnitOfWork<DeliveryDbContext>(context), IDeliveryUnitOfWork
{
    protected override DomainError ConcurrentUpdate => DeliveryApplicationErrors.ConcurrentUpdate;

    /// <summary>
    /// Deux fiches simultanées pour la même commande, deux créations simultanées des réglages d'un établissement, ou deux
    /// remises de la même course : la seconde est refusée comme si elle avait vu la première.
    /// </summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        (exception.InnerException as PostgresException) switch
        {
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: FulfillmentConfiguration.OrderIndex } =>
                new DomainException(DeliveryApplicationErrors.OrderAlreadyHasFulfillment),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CourierRemittanceConfiguration.FulfillmentIndex } =>
                new DomainException(RemittanceErrors.NotAwaitingRemittance),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: DeliverySettingsConfiguration.LocationIndex } =>
                new DomainException(DeliveryApplicationErrors.ConcurrentUpdate),
            _ => null,
        };
}
