namespace Reporting.Domain;

/// <summary>
/// Liste blanche explicite de ce qu'un rapport enregistré peut référencer : les métriques que les read models savent
/// calculer et les champs qu'ils acceptent comme filtres. Fournie par l'Application (à partir de ses projections) ; un
/// rapport ne peut jamais référencer une métrique ou un champ hors de cette liste, ce qui écarte des requêtes tout nom
/// de colonne arbitraire.
/// </summary>
public sealed class ReportCatalog : ValueObject
{
    private readonly HashSet<string> _metricCodes;
    private readonly HashSet<string> _filterFields;

    private ReportCatalog(HashSet<string> metricCodes, HashSet<string> filterFields)
    {
        _metricCodes = metricCodes;
        _filterFields = filterFields;
    }

    public IReadOnlyCollection<string> MetricCodes => _metricCodes.Order(StringComparer.Ordinal).ToList().AsReadOnly();

    public IReadOnlyCollection<string> FilterFields => _filterFields.Order(StringComparer.Ordinal).ToList().AsReadOnly();

    public static ReportCatalog Create(IEnumerable<string> metricCodes, IEnumerable<string> filterFields)
    {
        ArgumentNullException.ThrowIfNull(metricCodes);
        ArgumentNullException.ThrowIfNull(filterFields);

        var metrics = metricCodes.Select(ReportCode.Normalize).ToHashSet(StringComparer.Ordinal);
        DomainException.ThrowIf(metrics.Count == 0, ReportErrors.CatalogMetricsEmpty);

        return new ReportCatalog(metrics, filterFields.Select(ReportCode.Normalize).ToHashSet(StringComparer.Ordinal));
    }

    public bool ContainsMetric(string metricCode) => _metricCodes.Contains(metricCode);

    public bool AllowsFilterField(string field) => _filterFields.Contains(field);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var metric in MetricCodes)
        {
            yield return metric;
        }

        yield return "|";

        foreach (var field in FilterFields)
        {
            yield return field;
        }
    }
}
