using Reporting.Application.Abstractions;
using Reporting.Application.Metrics;

namespace Reporting.Infrastructure.Persistence;

/// <summary>
/// Les compteurs du jour. L'écriture est un upsert SQL (incrément atomique en base, sans lecture préalable) précédé de
/// l'inscription de la source, dans UNE transaction : une source déjà inscrite n'incrémente rien. Toutes les valeurs
/// sont passées en paramètres ; seuls les noms de tables, constants, sont dans le texte SQL.
/// </summary>
internal sealed class MetricsStore(ReportingDbContext context, IClock clock) : IMetricsStore
{
    public const int ValuePrecision = 20;
    public const int ValueScale = 4;

    public async Task<bool> RecordAsync(TenantId tenantId, string sourceKey, DailyFacts facts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var database = context.Database;
        await using var transaction = database.CurrentTransaction is null ? await database.BeginTransactionAsync(cancellationToken) : null;

        var tenant = tenantId.Value;
        var now = clock.UtcNow;
        var inserted = await database.ExecuteSqlAsync(
            $"""
            INSERT INTO reporting.processed_sources (source_key, tenant_id, processed_at_utc)
            VALUES ({sourceKey}, {tenant}, {now})
            ON CONFLICT (source_key) DO NOTHING
            """,
            cancellationToken);
        if (inserted == 0)
        {
            return false;
        }

        var location = facts.Location?.Value ?? Guid.Empty;
        var day = facts.Day;
        foreach (var fact in facts.Facts)
        {
            var (metric, dimension, currency, value) = (fact.Metric, fact.Dimension, fact.Currency, fact.Value);
            await database.ExecuteSqlAsync(
                $"""
                INSERT INTO reporting.daily_metrics (tenant_id, location_id, day, metric, dimension, currency, value)
                VALUES ({tenant}, {location}, {day}, {metric}, {dimension}, {currency}, {value})
                ON CONFLICT (tenant_id, location_id, day, metric, dimension, currency)
                DO UPDATE SET value = daily_metrics.value + EXCLUDED.value
                """,
                cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return true;
    }

    public async Task<IReadOnlyList<MetricRow>> QueryAsync(TenantId tenantId, MetricQuery query, CancellationToken cancellationToken = default) =>
        await Filter(context.DailyMetrics.AsNoTracking().Where(row => row.TenantId == tenantId), query)
            .Select(row => new MetricRow(row.Day, row.LocationId, row.Metric, row.Dimension, row.Currency, row.Value))
            .ToListAsync(cancellationToken);

    /// <summary>PLATEFORME : tous tenants, déclaré (IgnoreQueryFilters), agrégé en base par jour (sans établissement).</summary>
    public async Task<IReadOnlyList<MetricRow>> QueryAllTenantsAsync(MetricQuery query, CancellationToken cancellationToken = default) =>
        await Filter(context.DailyMetrics.IgnoreQueryFilters().AsNoTracking(), query)
            .GroupBy(row => new { row.Day, row.Metric, row.Dimension, row.Currency })
            .Select(group => new MetricRow(group.Key.Day, Guid.Empty, group.Key.Metric, group.Key.Dimension, group.Key.Currency, group.Sum(row => row.Value)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TenantMetricTotal>> TotalsByTenantAsync(string metric, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        (await context.DailyMetrics.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Metric == metric && row.Day >= from && row.Day <= to && row.Value != 0)
            .GroupBy(row => new { row.TenantId, row.Currency })
            .Select(group => new { group.Key.TenantId, group.Key.Currency, Value = group.Sum(row => row.Value), LastDay = group.Max(row => row.Day) })
            .ToListAsync(cancellationToken))
        .Select(total => new TenantMetricTotal(total.TenantId, total.Currency, total.Value, total.LastDay))
        .ToList();

    private static IQueryable<DailyMetricRecord> Filter(IQueryable<DailyMetricRecord> rows, MetricQuery query)
    {
        var metrics = query.Metrics.ToArray();
        var (from, to) = (query.From, query.To);
        rows = rows.Where(row => row.Day >= from && row.Day <= to && metrics.Contains(row.Metric));

        if (query.Locations.Include is { } locationsIn)
        {
            var values = locationsIn.ToArray();
            rows = rows.Where(row => values.Contains(row.LocationId));
        }

        if (query.Locations.Exclude.Count > 0)
        {
            var values = query.Locations.Exclude.ToArray();
            rows = rows.Where(row => !values.Contains(row.LocationId));
        }

        if (query.Currencies.Include is { } currenciesIn)
        {
            var values = currenciesIn.ToArray();
            rows = rows.Where(row => values.Contains(row.Currency));
        }

        if (query.Currencies.Exclude.Count > 0)
        {
            var values = query.Currencies.Exclude.ToArray();
            rows = rows.Where(row => !values.Contains(row.Currency));
        }

        if (query.Dimensions.Include is { } dimensionsIn)
        {
            var values = dimensionsIn.ToArray();
            rows = rows.Where(row => values.Contains(row.Dimension));
        }

        if (query.Dimensions.Exclude.Count > 0)
        {
            var values = query.Dimensions.Exclude.ToArray();
            rows = rows.Where(row => !values.Contains(row.Dimension));
        }

        return rows;
    }
}
