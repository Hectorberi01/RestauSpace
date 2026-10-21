using Floor.Application.Abstractions;
using Floor.Domain;
using Npgsql;

namespace Floor.Infrastructure.Persistence;

internal sealed class SessionOrder : ITenantOwned
{
    public const string Table = "session_orders";

    public TenantId TenantId { get; set; }

    public Guid OrderId { get; set; }

    public TableSessionId? TableSessionId { get; set; }

    public bool IsClosed { get; set; }
}

internal sealed class SessionOrderLedger(FloorDbContext context) : ITableSessionOrderLedger, ITableSessionOrdersChecker
{
    private static readonly string Opened =
        $"""
        INSERT INTO {DependencyInjection.Schema}.{SessionOrder.Table} (tenant_id, order_id, table_session_id, is_closed)
        VALUES (@tenant, @order, @session, FALSE)
        ON CONFLICT (tenant_id, order_id) DO UPDATE SET table_session_id = excluded.table_session_id
        """;

    private static readonly string Closed =
        $"""
        INSERT INTO {DependencyInjection.Schema}.{SessionOrder.Table} (tenant_id, order_id, table_session_id, is_closed)
        VALUES (@tenant, @order, NULL, TRUE)
        ON CONFLICT (tenant_id, order_id) DO UPDATE SET is_closed = TRUE
        """;

    public Task<bool> HasOpenOrdersAsync(TenantId tenantId, TableSessionId tableSessionId, CancellationToken cancellationToken = default) =>
        context.SessionOrders.AsNoTracking().AnyAsync(
            order => order.TenantId == tenantId && order.TableSessionId == tableSessionId && !order.IsClosed, cancellationToken);

    public Task RecordOpenedAsync(TenantId tenantId, Guid orderId, TableSessionId tableSessionId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(Opened, tenantId, orderId, tableSessionId.Value, cancellationToken);

    public Task RecordClosedAsync(TenantId tenantId, Guid orderId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(Closed, tenantId, orderId, null, cancellationToken);

    private async Task ExecuteAsync(string sql, TenantId tenantId, Guid orderId, Guid? sessionId, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("tenant", tenantId.Value));
            command.Parameters.Add(new NpgsqlParameter("order", orderId));
            if (sessionId is { } session)
            {
                command.Parameters.Add(new NpgsqlParameter("session", session));
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
