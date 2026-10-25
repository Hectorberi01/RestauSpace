using Reporting.Application.Abstractions;
using Reporting.Application.Commands;
using Reporting.Application.Exports;
using Reporting.Application.Metrics;
using Reporting.Application.Platform;
using Reporting.Domain;

namespace Reporting.Application.Queries;

// Lectures du restaurant du contexte : possibles même quand il est suspendu. Les chiffres viennent des projections,
// jamais des agrégats des autres modules.

public sealed record GetReportCatalogQuery : IQuery<ReportCatalogDto>;

public sealed class GetReportCatalogHandler : IQueryHandler<GetReportCatalogQuery, ReportCatalogDto>
{
    private static readonly ReportCatalogDto Catalog = new(
        ReportingMetrics.All.Select(metric => new MetricDto(metric.Code, metric.Source, metric.Monetary, metric.IsDerived)).ToList(),
        ReportingMetrics.FilterFields,
        [FilterOperator.EqualTo, FilterOperator.NotEqualTo, FilterOperator.In]);

    public Task<ReportCatalogDto> HandleAsync(GetReportCatalogQuery query, CancellationToken cancellationToken = default) => Task.FromResult(Catalog);
}

/// <summary>Exécute un rapport ponctuel (mesures et filtres validés par le catalogue).</summary>
public sealed record RunReportQuery(
    IReadOnlyList<string>? Metrics,
    IReadOnlyList<ReportFilterInput>? Filters,
    DateOnly From,
    DateOnly To,
    ReportGranularity Granularity = ReportGranularity.Day,
    bool ByLocation = false) : IQuery<ReportResultDto>;

public sealed class RunReportHandler(ReportingWorkspace workspace) : IQueryHandler<RunReportQuery, ReportResultDto>
{
    public async Task<ReportResultDto> HandleAsync(RunReportQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var metrics = MetricSet.Create(query.Metrics ?? Array.Empty<string>(), ReportingMetrics.Catalog).Codes.ToList();
        var filters = ReportFilters.Create(ReportFilterInput.ToFilters(query.Filters), ReportingMetrics.Catalog).Items;
        await workspace.RequireConsolidatedAllowedAsync(filters, cancellationToken);
        var rows = await workspace.RunAsync(metrics, filters, query.From, query.To, query.Granularity, query.ByLocation, cancellationToken);
        return new ReportResultDto(query.From, query.To, query.Granularity, rows.Select(ReportRowDto.From).ToList());
    }
}

/// <summary>Exécute un rapport enregistré sur une période (refusé s'il ne passe plus le catalogue).</summary>
public sealed record RunSavedReportQuery(
    Guid DefinitionId, DateOnly From, DateOnly To, ReportGranularity Granularity = ReportGranularity.Day, bool ByLocation = false)
    : IQuery<ReportResultDto>;

public sealed class RunSavedReportHandler(ReportingWorkspace workspace) : IQueryHandler<RunSavedReportQuery, ReportResultDto>
{
    public async Task<ReportResultDto> HandleAsync(RunSavedReportQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var definition = await workspace.LoadDefinitionAsync(query.DefinitionId, cancellationToken);
        definition.EnsureCompatibleWith(ReportingMetrics.Catalog);
        await workspace.RequireConsolidatedAllowedAsync(definition.Filters.Items, cancellationToken);
        var rows = await workspace.RunAsync(
            definition.Metrics.Codes.ToList(), definition.Filters.Items, query.From, query.To, query.Granularity, query.ByLocation, cancellationToken);
        return new ReportResultDto(query.From, query.To, query.Granularity, rows.Select(ReportRowDto.From).ToList());
    }
}

/// <summary>Le tableau de bord : indicateurs clés de la période, pour tout le restaurant ou un établissement.</summary>
public sealed record GetDashboardQuery(DateOnly From, DateOnly To, Guid? LocationId = null) : IQuery<DashboardDto>;

public sealed class GetDashboardHandler(ReportingWorkspace workspace) : IQueryHandler<GetDashboardQuery, DashboardDto>
{
    public static readonly IReadOnlyList<string> Indicators =
    [
        ReportingMetrics.Revenue,
        ReportingMetrics.OrdersCompleted,
        ReportingMetrics.AverageTicket,
        ReportingMetrics.CancellationRate,
        ReportingMetrics.DiscountAmount,
        ReportingMetrics.CapturedAmount,
        ReportingMetrics.RefundedAmount,
        ReportingMetrics.CoversSeated,
        ReportingMetrics.NoShowRate,
        ReportingMetrics.AveragePreparationMinutes,
        ReportingMetrics.WasteRecords,
        ReportingMetrics.PromotionDiscountAmount,
        ReportingMetrics.PointsEarned,
    ];

    public async Task<DashboardDto> HandleAsync(GetDashboardQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filters = query.LocationId is { } location
            ? ReportFilterInput.ToFilters([new ReportFilterInput(ReportingMetrics.LocationField, FilterOperator.EqualTo, location.ToString())])
            : Array.Empty<ReportFilter>();
        await workspace.RequireConsolidatedAllowedAsync(filters, cancellationToken);
        var totals = await workspace.RunAsync(Indicators, filters, query.From, query.To, ReportGranularity.Total, false, cancellationToken);
        var daily = await workspace.RunAsync([ReportingMetrics.Revenue], filters, query.From, query.To, ReportGranularity.Day, false, cancellationToken);
        return new DashboardDto(query.From, query.To, totals.Select(ReportRowDto.From).ToList(), daily.Select(ReportRowDto.From).ToList());
    }
}

public sealed record ListReportDefinitionsQuery : IQuery<IReadOnlyList<ReportDefinitionDto>>;

public sealed class ListReportDefinitionsHandler(ReportingWorkspace workspace) : IQueryHandler<ListReportDefinitionsQuery, IReadOnlyList<ReportDefinitionDto>>
{
    public async Task<IReadOnlyList<ReportDefinitionDto>> HandleAsync(ListReportDefinitionsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var result = new List<ReportDefinitionDto>();
        foreach (var definition in await workspace.Definitions.ListAsync(tenant, cancellationToken))
        {
            var delivery = definition.IsScheduled ? await workspace.Deliveries.FindAsync(tenant, definition.Id, cancellationToken) : null;
            result.Add(ReportDefinitionDto.From(definition, delivery));
        }

        return result;
    }
}

public sealed record GetReportDefinitionQuery(Guid DefinitionId) : IQuery<ReportDefinitionDto>;

public sealed class GetReportDefinitionHandler(ReportingWorkspace workspace) : IQueryHandler<GetReportDefinitionQuery, ReportDefinitionDto>
{
    public async Task<ReportDefinitionDto> HandleAsync(GetReportDefinitionQuery query, CancellationToken cancellationToken = default)
    {
        var definition = await workspace.LoadDefinitionAsync(query.DefinitionId, cancellationToken);
        var delivery = await workspace.Deliveries.FindAsync(definition.TenantId, definition.Id, cancellationToken);
        return ReportDefinitionDto.From(definition, delivery);
    }
}

public sealed record ListExportsQuery(int Skip = 0, int Take = 50) : IQuery<IReadOnlyList<ExportJobDto>>;

public sealed class ListExportsHandler(ReportingWorkspace workspace) : IQueryHandler<ListExportsQuery, IReadOnlyList<ExportJobDto>>
{
    public async Task<IReadOnlyList<ExportJobDto>> HandleAsync(ListExportsQuery query, CancellationToken cancellationToken = default)
    {
        ReportingWorkspace.EnsurePage(query.Skip, query.Take);
        var tenant = workspace.CurrentTenant;
        var result = new List<ExportJobDto>();
        foreach (var job in await workspace.Exports.ListAsync(tenant, query.Skip, query.Take, cancellationToken))
        {
            result.Add(ExportJobDto.Create(job, await workspace.ExportRequests.FindAsync(tenant, job.Id, cancellationToken)));
        }

        return result;
    }
}

public sealed record GetExportQuery(Guid ExportJobId) : IQuery<ExportJobDto>;

public sealed class GetExportHandler(ReportingWorkspace workspace) : IQueryHandler<GetExportQuery, ExportJobDto>
{
    public async Task<ExportJobDto> HandleAsync(GetExportQuery query, CancellationToken cancellationToken = default)
    {
        var job = await workspace.LoadExportAsync(query.ExportJobId, cancellationToken);
        return ExportJobDto.Create(job, await workspace.ExportRequests.FindAsync(job.TenantId, job.Id, cancellationToken));
    }
}

/// <summary>Le fichier d'un export terminé du restaurant du contexte (refusé s'il n'est pas prêt, a échoué ou a expiré).</summary>
public sealed record GetExportFileQuery(Guid ExportJobId) : IQuery<ExportFileDto>;

public sealed class GetExportFileHandler(ReportingWorkspace workspace) : IQueryHandler<GetExportFileQuery, ExportFileDto>
{
    public async Task<ExportFileDto> HandleAsync(GetExportFileQuery query, CancellationToken cancellationToken = default)
    {
        var job = await workspace.LoadExportAsync(query.ExportJobId, cancellationToken);
        DomainException.ThrowIf(!job.IsDownloadable || job.FileId is null, ReportingApplicationErrors.ExportNotDownloadable);
        var file = await workspace.Files.OpenAsync(job.TenantId, job.FileId!.Value, cancellationToken)
            ?? throw new NotFoundException(ReportingApplicationErrors.ExportNotFound);
        return new ExportFileDto(file.FileName, file.ContentType, file.Content);
    }
}

/// <summary>Pour l'hôte, à la fin d'un export : son titre, sa période, son demandeur et ses destinataires.</summary>
public sealed record GetExportDeliveryQuery(Guid ExportJobId) : IQuery<ExportDeliveryDto>;

public sealed class GetExportDeliveryHandler(ReportingWorkspace workspace) : IQueryHandler<GetExportDeliveryQuery, ExportDeliveryDto>
{
    public async Task<ExportDeliveryDto> HandleAsync(GetExportDeliveryQuery query, CancellationToken cancellationToken = default)
    {
        var job = await workspace.LoadExportAsync(query.ExportJobId, cancellationToken);
        var request = await workspace.ExportRequests.FindAsync(job.TenantId, job.Id, cancellationToken)
            ?? throw new NotFoundException(ReportingApplicationErrors.ExportNotFound);
        return new ExportDeliveryDto(job.Id.Value, request.Title, request.From, request.To, job.RequestedBy.User?.Value, request.Recipients);
    }
}

// --- Plateforme (routes d'administration) : tous les restaurants, lectures déclarées.

/// <summary>Le tableau de bord de la plateforme : restaurants, abonnements, MRR, et activité de tous les restaurants.</summary>
public sealed record GetPlatformDashboardQuery(DateOnly From, DateOnly To) : IQuery<PlatformDashboardDto>;

public sealed class GetPlatformDashboardHandler(IPlatformDirectory directory, IMetricsStore metrics, ReportingSettings settings)
    : IQueryHandler<GetPlatformDashboardQuery, PlatformDashboardDto>
{
    public static readonly IReadOnlyList<string> Activity =
    [
        ReportingMetrics.OrdersCompleted,
        ReportingMetrics.Revenue,
        ReportingMetrics.OnlineOrdersCompleted,
        ReportingMetrics.OnlineRevenue,
        ReportingMetrics.CapturedAmount,
        ReportingMetrics.ReservationsCreated,
        ReportingMetrics.CoversSeated,
        ReportingMetrics.TicketsReady,
    ];

    public async Task<PlatformDashboardDto> HandleAsync(GetPlatformDashboardQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        PlatformQueries.EnsurePeriod(query.From, query.To, settings);
        var summary = await directory.SummarizeAsync(
            new DateTimeOffset(query.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(query.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            cancellationToken);
        var rows = await metrics.QueryAllTenantsAsync(PlatformQueries.Query(query.From, query.To, Activity), cancellationToken);
        var activity = ReportEngine.Compute(Activity, rows, query.From, query.To, ReportGranularity.Total, false);
        return new PlatformDashboardDto(
            query.From, query.To, summary.TenantsByStatus, summary.NewTenants, summary.SubscriptionsByStatus, summary.MonthlyRecurringRevenue,
            activity.Select(ReportRowDto.From).ToList());
    }
}

/// <summary>Plateforme : l'activité de tous les restaurants par période, pour les mesures du catalogue (sans filtre).</summary>
public sealed record GetPlatformActivityQuery(
    IReadOnlyList<string>? Metrics, DateOnly From, DateOnly To, ReportGranularity Granularity = ReportGranularity.Day) : IQuery<ReportResultDto>;

public sealed class GetPlatformActivityHandler(IMetricsStore metrics, ReportingSettings settings) : IQueryHandler<GetPlatformActivityQuery, ReportResultDto>
{
    public async Task<ReportResultDto> HandleAsync(GetPlatformActivityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        PlatformQueries.EnsurePeriod(query.From, query.To, settings);
        ReportingWorkspace.EnsureGranularity(query.Granularity);
        var codes = MetricSet.Create(query.Metrics ?? Array.Empty<string>(), ReportingMetrics.Catalog).Codes.ToList();
        var rows = await metrics.QueryAllTenantsAsync(PlatformQueries.Query(query.From, query.To, codes), cancellationToken);
        var result = ReportEngine.Compute(codes, rows, query.From, query.To, query.Granularity, false);
        return new ReportResultDto(query.From, query.To, query.Granularity, result.Select(ReportRowDto.From).ToList());
    }
}

/// <summary>Plateforme : les restaurants et leur abonnement, filtrés par statut, par nom.</summary>
public sealed record ListPlatformTenantsQuery(PlatformTenantStatus? Status = null, int Skip = 0, int Take = 50)
    : IQuery<IReadOnlyList<PlatformTenantRow>>;

public sealed class ListPlatformTenantsHandler(IPlatformDirectory directory) : IQueryHandler<ListPlatformTenantsQuery, IReadOnlyList<PlatformTenantRow>>
{
    public Task<IReadOnlyList<PlatformTenantRow>> HandleAsync(ListPlatformTenantsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ReportingWorkspace.EnsurePage(query.Skip, query.Take);
        return directory.ListTenantsAsync(query.Status, query.Skip, query.Take, cancellationToken);
    }
}

/// <summary>Un restaurant et son activité : commandes terminées, chiffre d'affaires et dernier jour de vente.</summary>
public sealed record TenantActivityRow(
    Guid TenantId,
    string Name,
    PlatformTenantStatus Status,
    DateTimeOffset CreatedAtUtc,
    decimal Orders,
    decimal Revenue,
    string? Currency,
    DateOnly? LastOrderDay);

/// <param name="MostActive">Les restaurants qui ont terminé le plus de commandes sur la période.</param>
/// <param name="Inactive">Restaurants ouverts (actifs ou en essai) sans commande terminée depuis <see cref="GetPlatformTenantActivityQuery.InactiveDays"/> jours.</param>
public sealed record PlatformTenantActivityDto(DateOnly From, DateOnly To, IReadOnlyList<TenantActivityRow> MostActive, IReadOnlyList<TenantActivityRow> Inactive);

/// <summary>Plateforme : les restaurants les plus actifs sur la période, et ceux qui ne vendent plus.</summary>
public sealed record GetPlatformTenantActivityQuery(DateOnly From, DateOnly To, int Top = 10, int InactiveDays = 14) : IQuery<PlatformTenantActivityDto>;

public sealed class GetPlatformTenantActivityHandler(IPlatformDirectory directory, IMetricsStore metrics, ReportingSettings settings, IClock clock)
    : IQueryHandler<GetPlatformTenantActivityQuery, PlatformTenantActivityDto>
{
    public const int MaxTop = 50;
    public const int LookbackDays = 90;
    private const int Page = 200;
    private const int MaxTenants = 5000;

    public async Task<PlatformTenantActivityDto> HandleAsync(GetPlatformTenantActivityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        PlatformQueries.EnsurePeriod(query.From, query.To, settings);
        DomainException.ThrowIf(query.Top is < 1 or > MaxTop || query.InactiveDays is < 1 or > LookbackDays, ReportingApplicationErrors.PeriodInvalid);

        var tenants = new List<PlatformTenantRow>();
        for (var skip = 0; skip < MaxTenants; skip += Page)
        {
            var page = await directory.ListTenantsAsync(null, skip, Page, cancellationToken);
            tenants.AddRange(page);
            if (page.Count < Page)
            {
                break;
            }
        }

        var byId = tenants.ToDictionary(tenant => tenant.TenantId);
        var orders = (await metrics.TotalsByTenantAsync(ReportingMetrics.OrdersCompleted, query.From, query.To, cancellationToken))
            .GroupBy(total => total.TenantId.Value)
            .ToDictionary(group => group.Key, group => group.Sum(total => total.Value));
        var revenue = (await metrics.TotalsByTenantAsync(ReportingMetrics.Revenue, query.From, query.To, cancellationToken))
            .GroupBy(total => total.TenantId.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(total => total.Value).First());
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var last = (await metrics.TotalsByTenantAsync(ReportingMetrics.OrdersCompleted, today.AddDays(-LookbackDays), today, cancellationToken))
            .GroupBy(total => total.TenantId.Value)
            .ToDictionary(group => group.Key, group => group.Max(total => total.LastDay));

        TenantActivityRow Row(PlatformTenantRow tenant) => new(
            tenant.TenantId, tenant.Name, tenant.Status, tenant.CreatedAtUtc, orders.GetValueOrDefault(tenant.TenantId),
            revenue.GetValueOrDefault(tenant.TenantId)?.Value ?? 0, revenue.GetValueOrDefault(tenant.TenantId)?.Currency,
            last.TryGetValue(tenant.TenantId, out var day) ? day : null);

        var mostActive = orders
            .Where(entry => entry.Value > 0 && byId.ContainsKey(entry.Key))
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => byId[entry.Key].Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(query.Top)
            .Select(entry => Row(byId[entry.Key]))
            .ToList();
        var threshold = today.AddDays(-query.InactiveDays);
        var inactive = tenants
            .Where(tenant => tenant.Status is PlatformTenantStatus.Active or PlatformTenantStatus.Trial
                && DateOnly.FromDateTime(tenant.CreatedAtUtc.UtcDateTime) <= threshold
                && (!last.TryGetValue(tenant.TenantId, out var day) || day < threshold))
            .OrderBy(tenant => last.TryGetValue(tenant.TenantId, out var day) ? day : DateOnly.MinValue)
            .ThenBy(tenant => tenant.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxTop)
            .Select(Row)
            .ToList();
        return new PlatformTenantActivityDto(query.From, query.To, mostActive, inactive);
    }
}

internal static class PlatformQueries
{
    public static void EnsurePeriod(DateOnly from, DateOnly to, ReportingSettings settings) =>
        DomainException.ThrowIf(to < from || to.DayNumber - from.DayNumber + 1 > settings.MaxPeriodDays, ReportingApplicationErrors.PeriodInvalid);

    public static MetricQuery Query(DateOnly from, DateOnly to, IReadOnlyList<string> codes) =>
        new(from, to, ReportingMetrics.StoredInputs(codes), ValueFilter<Guid>.All, ValueFilter<string>.All, ValueFilter<string>.All);
}
