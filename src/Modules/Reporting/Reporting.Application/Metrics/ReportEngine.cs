namespace Reporting.Application.Metrics;

/// <summary>Une ligne de résultat : période (jours locaux, bornes comprises), établissement si ventilé, mesure.</summary>
public sealed record ReportRow(
    DateOnly PeriodStart, DateOnly PeriodEnd, Guid? LocationId, string Metric, string Dimension, string Currency, decimal Value);

/// <summary>
/// Agrège les compteurs du jour par période et calcule les mesures dérivées APRÈS agrégation (un panier moyen sur un
/// mois n'est pas la moyenne des paniers moyens du jour). Une mesure dérivée est calculée par période, établissement et
/// devise, toutes dimensions confondues ; elle est omise quand son dénominateur est nul.
/// </summary>
public static class ReportEngine
{
    public const int Decimals = 4;

    public static IReadOnlyList<ReportRow> Compute(
        IReadOnlyList<string> metrics,
        IEnumerable<MetricRow> rows,
        DateOnly from,
        DateOnly to,
        ReportGranularity granularity,
        bool byLocation)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(rows);

        var sums = rows
            .Where(row => row.Day >= from && row.Day <= to)
            .GroupBy(row => (Period: Bucket(row.Day, from, to, granularity), Location: byLocation ? row.LocationId : (Guid?)null, row.Metric, row.Dimension, row.Currency))
            .Select(group => (group.Key.Period, group.Key.Location, group.Key.Metric, group.Key.Dimension, group.Key.Currency, Value: group.Sum(row => row.Value)))
            .ToList();

        var result = new List<ReportRow>();
        foreach (var code in metrics)
        {
            var definition = ReportingMetrics.Require(code);
            if (!definition.IsDerived)
            {
                result.AddRange(sums
                    .Where(sum => sum.Metric == code)
                    .Select(sum => new ReportRow(sum.Period.Start, sum.Period.End, sum.Location, code, sum.Dimension, sum.Currency, sum.Value)));
                continue;
            }

            var numerators = Totals(sums, definition.Numerator!);
            var denominators = Totals(sums, definition.Denominator!);
            foreach (var (key, denominator) in denominators.Where(pair => pair.Value != 0m))
            {
                var numerator = numerators.GetValueOrDefault(key);
                var value = Math.Round(numerator / denominator * definition.Scale, Decimals, MidpointRounding.AwayFromZero);
                result.Add(new ReportRow(key.Period.Start, key.Period.End, key.Location, code, string.Empty, key.Currency, value));
            }
        }

        var position = metrics.Select((code, index) => (code, index)).ToDictionary(pair => pair.code, pair => pair.index, StringComparer.Ordinal);
        return result
            .OrderBy(row => row.PeriodStart)
            .ThenBy(row => row.LocationId)
            .ThenBy(row => position.GetValueOrDefault(row.Metric))
            .ThenBy(row => row.Dimension, StringComparer.Ordinal)
            .ThenBy(row => row.Currency, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>La période d'un jour : le jour, sa semaine ISO, son mois ou toute la période, bornés à [from, to].</summary>
    public static (DateOnly Start, DateOnly End) Bucket(DateOnly day, DateOnly from, DateOnly to, ReportGranularity granularity)
    {
        (DateOnly Start, DateOnly End) bucket = granularity switch
        {
            ReportGranularity.Day => (day, day),
            ReportGranularity.Week => (day.AddDays(-(((int)day.DayOfWeek + 6) % 7)), day.AddDays(-(((int)day.DayOfWeek + 6) % 7) + 6)),
            ReportGranularity.Month => (new DateOnly(day.Year, day.Month, 1), new DateOnly(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month))),
            _ => (from, to),
        };
        return (bucket.Start < from ? from : bucket.Start, bucket.End > to ? to : bucket.End);
    }

    private static Dictionary<((DateOnly Start, DateOnly End) Period, Guid? Location, string Currency), decimal> Totals(
        IEnumerable<((DateOnly Start, DateOnly End) Period, Guid? Location, string Metric, string Dimension, string Currency, decimal Value)> sums,
        string metric) =>
        sums.Where(sum => sum.Metric == metric)
            .GroupBy(sum => (sum.Period, sum.Location, sum.Currency))
            .ToDictionary(group => group.Key, group => group.Sum(sum => sum.Value));
}
