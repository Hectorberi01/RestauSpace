using Npgsql;
using Orders.Application.Abstractions;
using Orders.Domain;

namespace Orders.Infrastructure.Persistence;

internal sealed class OrderPaymentSettlement : ITenantOwned
{
    public const string Table = "order_payment_settlements";

    public TenantId TenantId { get; set; }

    public Guid PaymentId { get; set; }

    public OrderId OrderId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal SettledAmount { get; set; }

    public long Version { get; set; }
}

internal sealed class OrderPaymentSettlements(OrdersDbContext context) : IOrderPayments, IOrderPaymentSettlements
{
    private static readonly string Upsert =
        $"""
        INSERT INTO {DependencyInjection.Schema}.{OrderPaymentSettlement.Table} AS settlement
            (tenant_id, payment_id, order_id, currency_code, settled_amount, version)
        VALUES (@tenant, @payment, @order, @currency, @amount, @version)
        ON CONFLICT (tenant_id, payment_id)
        DO UPDATE SET order_id = excluded.order_id, currency_code = excluded.currency_code,
            settled_amount = excluded.settled_amount, version = excluded.version
        WHERE settlement.version < excluded.version
        """;

    public async Task<Money> SettledAmountAsync(TenantId tenantId, OrderId orderId, Currency currency, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currency);
        var amount = await context.PaymentSettlements.AsNoTracking()
            .Where(settlement => settlement.TenantId == tenantId && settlement.OrderId == orderId && settlement.CurrencyCode == currency.Code)
            .SumAsync(settlement => settlement.SettledAmount, cancellationToken);
        return Money.Of(amount, currency);
    }

    public async Task RecordAsync(
        TenantId tenantId, Guid paymentId, OrderId orderId, Money settledAmount, long version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settledAmount);
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = Upsert;
            command.Parameters.Add(new NpgsqlParameter("tenant", tenantId.Value));
            command.Parameters.Add(new NpgsqlParameter("payment", paymentId));
            command.Parameters.Add(new NpgsqlParameter("order", orderId.Value));
            command.Parameters.Add(new NpgsqlParameter("currency", settledAmount.Currency.Code));
            command.Parameters.Add(new NpgsqlParameter("amount", settledAmount.Amount));
            command.Parameters.Add(new NpgsqlParameter("version", version));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
