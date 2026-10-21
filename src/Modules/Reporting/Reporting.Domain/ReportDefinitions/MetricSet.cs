namespace Reporting.Domain;

/// <summary>Ensemble non vide de codes de métriques, tous issus d'un <see cref="ReportCatalog"/>.</summary>
public sealed class MetricSet : ValueObject
{
    public const int MaxMetrics = 30;

    private readonly List<string> _codes;

    private MetricSet(List<string> codes) => _codes = codes;

    public IReadOnlyCollection<string> Codes => _codes.AsReadOnly();

    public static MetricSet Create(IEnumerable<string> metricCodes, ReportCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(metricCodes);
        ArgumentNullException.ThrowIfNull(catalog);

        var codes = new List<string>();
        foreach (var code in metricCodes.Select(ReportCode.Normalize))
        {
            DomainException.ThrowIf(!catalog.ContainsMetric(code), ReportErrors.MetricUnknown);
            if (!codes.Contains(code))
            {
                codes.Add(code);
            }
        }

        DomainException.ThrowIf(codes.Count == 0, ReportErrors.MetricsEmpty);
        DomainException.ThrowIf(codes.Count > MaxMetrics, ReportErrors.TooManyMetrics);

        return new MetricSet(codes);
    }

    public bool Contains(string metricCode) => _codes.Contains(metricCode);

    protected override IEnumerable<object?> GetEqualityComponents() => _codes.Order(StringComparer.Ordinal);
}
