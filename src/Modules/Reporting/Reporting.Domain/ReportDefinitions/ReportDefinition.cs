namespace Reporting.Domain;

/// <summary>Définition enregistrée d'un rapport : quoi mesurer, comment filtrer et, éventuellement, quand l'envoyer.</summary>
public sealed class ReportDefinition : AggregateRoot<ReportDefinitionId>, ITenantOwned
{
    public const int MaxNameLength = 100;

    private ReportDefinition(
        ReportDefinitionId id,
        TenantId tenantId,
        string name,
        MetricSet metrics,
        ReportFilters filters,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Metrics = metrics;
        Filters = filters;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private ReportDefinition()
    {
        Name = null!;
        Metrics = null!;
        Filters = null!;
    }

    public TenantId TenantId { get; private set; }

    public string Name { get; private set; }

    public MetricSet Metrics { get; private set; }

    public ReportFilters Filters { get; private set; }

    public ReportSchedule? Schedule { get; private set; }

    public bool IsScheduled => Schedule is not null;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ReportDefinition Create(
        TenantId tenantId,
        string? name,
        IEnumerable<string> metricCodes,
        IEnumerable<ReportFilter>? filters,
        ReportCatalog catalog,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        return new ReportDefinition(
            ReportDefinitionId.New(),
            tenantId,
            ValidateName(name),
            MetricSet.Create(metricCodes, catalog),
            ReportFilters.Create(filters, catalog),
            clock.UtcNow);
    }

    public void Rename(string? name, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var validName = ValidateName(name);
        if (validName == Name)
        {
            return;
        }

        Name = validName;
        UpdatedAtUtc = clock.UtcNow;
    }

    /// <summary>Remplace tout le jeu de filtres ; chaque champ est revérifié contre <paramref name="catalog"/>.</summary>
    public void ChangeFilters(IEnumerable<ReportFilter>? filters, ReportCatalog catalog, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var validFilters = ReportFilters.Create(filters, catalog);
        if (validFilters == Filters)
        {
            return;
        }

        Filters = validFilters;
        UpdatedAtUtc = clock.UtcNow;
    }

    /// <summary>
    /// Le catalogue évolue après l'enregistrement d'une définition : rejette une définition dont les métriques ou
    /// champs de filtre stockés ne font plus tous partie de <paramref name="catalog"/>.
    /// </summary>
    public void EnsureCompatibleWith(ReportCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        DomainException.ThrowIf(
            !Metrics.Codes.All(catalog.ContainsMetric) || !Filters.Items.All(f => catalog.AllowsFilterField(f.Field)),
            ReportErrors.CatalogIncompatible);
    }

    public void ScheduleDelivery(ReportSchedule schedule, ReportCatalog catalog, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureCompatibleWith(catalog);

        if (Schedule == schedule)
        {
            return;
        }

        var now = clock.UtcNow;
        Schedule = schedule;
        UpdatedAtUtc = now;

        Raise(new ReportScheduled(
            TenantId,
            Id,
            schedule.Frequency,
            schedule.DayOfWeek,
            schedule.DayOfMonth,
            schedule.LocalTimeOfDay,
            schedule.TimeZone,
            now));
    }

    public void Unschedule(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Schedule is null, ReportErrors.NotScheduled);

        var now = clock.UtcNow;
        Schedule = null;
        UpdatedAtUtc = now;

        Raise(new ReportUnscheduled(TenantId, Id, now));
    }

    /// <summary>Seul moyen d'obtenir une référence à cette définition : son tenant est le vrai propriétaire.</summary>
    public ReportDefinitionReference ToReference() => new(TenantId, Id);

    private static string ValidateName(string? name)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), ReportErrors.NameEmpty);

        var trimmed = name!.Trim();
        DomainException.ThrowIf(trimmed.Length > MaxNameLength, ReportErrors.NameTooLong);

        return trimmed;
    }
}
