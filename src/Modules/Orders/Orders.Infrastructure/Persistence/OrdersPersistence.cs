using Npgsql;
using Orders.Application.Abstractions;
using Orders.Domain;
using Orders.Infrastructure.Persistence.Configurations;

namespace Orders.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.

internal sealed class OrderRepository(OrdersDbContext context) : IOrderRepository
{
    // Les lignes sont toujours chargées dans leur ordre d'ajout.
    private IQueryable<Order> Orders => context.Orders
        .Include(order => order.Items.OrderBy(item => EF.Property<int>(item, OrderItemConfiguration.PositionProperty)));

    public Task<Order?> FindAsync(TenantId tenantId, OrderId id, CancellationToken cancellationToken = default) =>
        Orders.SingleOrDefaultAsync(order => order.TenantId == tenantId && order.Id == id, cancellationToken);

    public async Task<OrderPage> ListAsync(TenantId tenantId, OrderSearch search, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = Orders.AsNoTracking().Where(order => order.TenantId == tenantId);
        if (search.CustomerId is { } customer)
        {
            query = query.Where(order => order.CustomerId == customer);
        }

        if (search.LocationId is { } location)
        {
            query = query.Where(order => order.LocationId == location);
        }

        if (search.Status is { } wanted)
        {
            query = query.Where(order => order.Status == wanted);
        }

        if (search.TableSessionId is { } session)
        {
            query = query.Where(order => order.TableSessionId == session);
        }

        if (search.ExcludeDrafts)
        {
            query = query.Where(order => order.Status != OrderStatus.Draft);
        }

        if (search.Type is { } type)
        {
            query = query.Where(order => order.Type == type);
        }

        if (search.Channel is { } channel)
        {
            query = query.Where(order => order.Channel == channel);
        }

        if (search.FromUtc is { } from)
        {
            query = query.Where(order => order.CreatedAtUtc >= from);
        }

        if (search.ToUtc is { } to)
        {
            query = query.Where(order => order.CreatedAtUtc < to);
        }

        if (search.MinTotal is { } min)
        {
            query = query.Where(order => order.Total.Amount >= min);
        }

        if (search.MaxTotal is { } max)
        {
            query = query.Where(order => order.Total.Amount <= max);
        }

        if (search.Number is { IsSuffix: false } exact)
        {
            var number = OrderNumber.Create(exact.Value);
            query = query.Where(order => order.Number == number);
        }
        else if (search.Number is { } suffix)
        {
            var candidates = NumbersEndingWith(suffix.Value, search.FromUtc, search.ToUtc);
            query = query.Where(order => candidates.Contains(order.Number));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new OrderPage(items, total);
    }

    /// <summary>
    /// Les numéros « AAAAMMJJ-NNN » de chaque jour de la période (un jour de marge de chaque côté pour les fuseaux), au
    /// plus les <see cref="MaxNumberDays"/> derniers jours : la recherche reste exacte et passe par l'index du numéro.
    /// </summary>
    private static List<OrderNumber> NumbersEndingWith(string suffix, DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        var last = DateOnly.FromDateTime((toUtc ?? DateTimeOffset.UtcNow).UtcDateTime).AddDays(1);
        var first = DateOnly.FromDateTime((fromUtc ?? (toUtc ?? DateTimeOffset.UtcNow).AddDays(-MaxNumberDays)).UtcDateTime).AddDays(-1);
        if (last.DayNumber - first.DayNumber > MaxNumberDays)
        {
            first = last.AddDays(-MaxNumberDays);
        }

        var numbers = new List<OrderNumber>(last.DayNumber - first.DayNumber + 1);
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            numbers.Add(OrderNumber.Create($"{day:yyyyMMdd}{suffix}"));
        }

        return numbers;
    }

    private const int MaxNumberDays = 400;

    public Task<bool> HasOpenOrdersAsync(TenantId tenantId, TableSessionId tableSessionId, CancellationToken cancellationToken = default) =>
        context.Orders.AnyAsync(
            order => order.TenantId == tenantId
                && order.TableSessionId == tableSessionId
                && order.Status != OrderStatus.Completed
                && order.Status != OrderStatus.Cancelled,
            cancellationToken);

    public async Task<IReadOnlyList<Order>> ListOpenByTableSessionAsync(
        TenantId tenantId, TableSessionId tableSessionId, CancellationToken cancellationToken = default) =>
        await context.Orders
            .Where(order => order.TenantId == tenantId
                && order.TableSessionId == tableSessionId
                && order.Status != OrderStatus.Completed
                && order.Status != OrderStatus.Cancelled)
            .ToListAsync(cancellationToken);

    public Task<int> CountPlacedAsync(TenantId tenantId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
        context.Orders.CountAsync(
            order => order.TenantId == tenantId && order.PlacedAtUtc >= fromUtc && order.PlacedAtUtc < toUtc,
            cancellationToken);

    public async Task<IReadOnlyList<CustomerId>> ListCustomerIdsAsync(TenantId tenantId, LocationId locationId, int max, CancellationToken cancellationToken = default) =>
        (await context.Orders.AsNoTracking()
            .Where(order => order.TenantId == tenantId && order.LocationId == locationId && order.CustomerId != null && order.Status != OrderStatus.Cancelled)
            .Select(order => order.CustomerId)
            .Distinct()
            .Take(max)
            .ToListAsync(cancellationToken))
        .OfType<CustomerId>()
        .ToList();

    public void Add(Order order) => context.Orders.Add(order);
}

/// <summary>
/// Compteur par (tenant, établissement, jour) incrémenté en une seule instruction (INSERT ... ON CONFLICT DO UPDATE ...
/// RETURNING) : deux prises de commande simultanées reçoivent toujours deux numéros différents.
/// </summary>
internal sealed class OrderNumberGenerator(OrdersDbContext context) : IOrderNumberGenerator
{
    private static readonly string Sql =
        $"""
        INSERT INTO {DependencyInjection.Schema}.{OrderNumberCounter.Table} AS counter (tenant_id, location_id, day, last_value)
        VALUES (@tenant, @location, @day, 1)
        ON CONFLICT (tenant_id, location_id, day)
        DO UPDATE SET last_value = counter.last_value + 1
        RETURNING last_value
        """;

    public async Task<OrderNumber> NextAsync(TenantId tenantId, LocationId locationId, DateOnly day, CancellationToken cancellationToken = default)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = Sql;
            command.Parameters.Add(new NpgsqlParameter("tenant", tenantId.Value));
            command.Parameters.Add(new NpgsqlParameter("location", locationId.Value));
            command.Parameters.Add(new NpgsqlParameter("day", day));
            var next = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            return OrderNumber.Create($"{day:yyyyMMdd}-{next:D3}");
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
