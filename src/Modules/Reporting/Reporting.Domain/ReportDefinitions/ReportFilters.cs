namespace Reporting.Domain;

/// <summary>Filtres dont tous les champs sont autorisés par un <see cref="ReportCatalog"/>. Peut être vide.</summary>
public sealed class ReportFilters : ValueObject
{
    public const int MaxFilters = 20;

    private readonly List<ReportFilter> _items;

    private ReportFilters(List<ReportFilter> items) => _items = items;

    public IReadOnlyCollection<ReportFilter> Items => _items.AsReadOnly();

    public bool IsEmpty => _items.Count == 0;

    public static ReportFilters Create(IEnumerable<ReportFilter>? filters, ReportCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var items = new List<ReportFilter>();
        foreach (var filter in filters ?? [])
        {
            ArgumentNullException.ThrowIfNull(filter, nameof(filters));
            DomainException.ThrowIf(!catalog.AllowsFilterField(filter.Field), ReportErrors.FilterFieldNotAllowed);
            DomainException.ThrowIf(items.Contains(filter), ReportErrors.FilterDuplicated);
            DomainException.ThrowIf(items.Count == MaxFilters, ReportErrors.TooManyFilters);
            items.Add(filter);
        }

        return new ReportFilters(items);
    }

    protected override IEnumerable<object?> GetEqualityComponents() =>
        _items.OrderBy(f => f.Field, StringComparer.Ordinal).ThenBy(f => f.Operator).ThenBy(f => f.Value, StringComparer.Ordinal);
}
