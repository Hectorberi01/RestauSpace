using Reporting.Application.Abstractions;
using Reporting.Application.Commands;
using Reporting.Application.Exports;
using Reporting.Application.Metrics;
using Reporting.Application.Platform;
using Reporting.Application.Queries;
using Reporting.Application.Scheduling;
using Reporting.Domain;

namespace Reporting.Application;

/// <summary>
/// Ports en mémoire du module Reporting. Comme les vrais, ils ne répondent que pour le tenant donné (les lectures de la
/// plateforme exceptées). Le tenant de « la requête » est <see cref="CurrentTenantId"/>, l'utilisateur
/// <see cref="CurrentUser"/>. Le restaurant A est à Paris, le B à Cotonou.
/// </summary>
public sealed class ReportingFixture
    : ITenantContext, IReportingTenants, IReportingCurrentUser, IReportDefinitionRepository, IExportJobRepository, IExportRequestStore,
      IReportDeliveryStore, IMetricsStore, IExportFileStore, IPlatformDirectory, IReportingUnitOfWork
{
    private readonly Dictionary<(TenantId, Guid, DateOnly, string, string, string), decimal> _metrics = [];
    private readonly HashSet<string> _sources = [];
    private readonly Dictionary<(TenantId, ExportJobId), ExportRequest> _requests = [];

    public ReportingFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Tenants[TenantA] = new ReportingTenant(TenantA, "Chez A", TimeZoneId.Create("Europe/Paris"), true);
        Tenants[TenantB] = new ReportingTenant(TenantB, "Chez B", TimeZoneId.Create("Africa/Porto-Novo"), true);
        CurrentTenantId = TenantA;
        CurrentUser = UserId.New();
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    public UserId? CurrentUser { get; set; }

    UserId? IReportingCurrentUser.UserId => CurrentUser;

    public Guid? CurrentApiKey { get; set; }

    Guid? IReportingCurrentUser.ApiCredentialId => CurrentApiKey;

    public Dictionary<TenantId, ReportingTenant> Tenants { get; } = [];

    public List<ReportDefinition> Definitions { get; } = [];

    public List<ExportJob> Exports { get; } = [];

    public List<ReportDelivery> Deliveries { get; } = [];

    public Dictionary<(TenantId, FileId), StoredExportFile> Files { get; } = [];

    public Dictionary<TenantId, PlatformTenantSnapshot> PlatformTenants { get; } = [];

    public Dictionary<TenantId, PlatformSubscriptionSnapshot> PlatformSubscriptions { get; } = [];

    public ReportingSettings Settings { get; } = new();

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public ReportingWorkspace Workspace => new(this, this, this, this, this, this, this, this, this, Settings, this);

    public RecordReportingFactsHandler Record => new(Workspace);

    public RecordTenantSnapshotHandler RecordTenant => new(this, Clock);

    public RecordSubscriptionSnapshotHandler RecordSubscription => new(this, Clock);

    public CreateReportDefinitionHandler Create => new(Workspace, Clock);

    public RenameReportDefinitionHandler Rename => new(Workspace, Clock);

    public ChangeReportFiltersHandler ChangeFilters => new(Workspace, Clock);

    public ScheduleReportHandler Schedule => new(Workspace, Clock);

    public UnscheduleReportHandler Unschedule => new(Workspace, Clock);

    public RunScheduledReportHandler RunScheduled => new(Workspace, Clock);

    public RequestExportHandler RequestExport => new(Workspace, Clock);

    public GenerateExportHandler Generate => new(Workspace, Clock);

    public RequestAccountingExportHandler RequestAccounting => new(Workspace, Clock);

    public ExpireExportHandler Expire => new(Workspace, Clock);

    public DeleteExpiredExportFileHandler DeleteExpiredFile => new(Workspace);

    public RunReportHandler Run => new(Workspace);

    public RunSavedReportHandler RunSaved => new(Workspace);

    public GetDashboardHandler Dashboard => new(Workspace);

    public ListReportDefinitionsHandler ListDefinitions => new(Workspace);

    public GetReportDefinitionHandler GetDefinition => new(Workspace);

    public ListExportsHandler ListExports => new(Workspace);

    public GetExportFileHandler Download => new(Workspace);

    public GetExportDeliveryHandler Delivery => new(Workspace);

    public GetPlatformDashboardHandler PlatformDashboard => new(this, this, Settings);

    public GetPlatformTenantActivityHandler PlatformTenantActivity => new(this, this, Settings, Clock);

    public GetPlatformActivityHandler PlatformActivity => new(this, Settings);

    public ListPlatformTenantsHandler PlatformTenantList => new(this);

    /// <summary>Compte des faits pour le restaurant courant, comme le ferait l'hôte pour un message de l'outbox.</summary>
    public Task<bool> FactsAsync(string source, DateTimeOffset at, Guid? location, params ReportingFactInput[] facts) =>
        Record.HandleAsync(new RecordReportingFactsCommand(source, at, location, facts));

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<ReportingTenant?> IReportingTenants.FindAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<ReportingTenant?>(Tenants.GetValueOrDefault(tenantId));

    Task<ReportDefinition?> IReportDefinitionRepository.FindAsync(TenantId tenantId, ReportDefinitionId id, CancellationToken cancellationToken) =>
        Task.FromResult<ReportDefinition?>(Definitions.SingleOrDefault(definition => definition.TenantId == tenantId && definition.Id == id));

    Task<IReadOnlyList<ReportDefinition>> IReportDefinitionRepository.ListAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReportDefinition>>(Definitions.Where(definition => definition.TenantId == tenantId).OrderBy(definition => definition.Name).ToList());

    void IReportDefinitionRepository.Add(ReportDefinition definition) => Definitions.Add(definition);

    Task<ExportJob?> IExportJobRepository.FindAsync(TenantId tenantId, ExportJobId id, CancellationToken cancellationToken) =>
        Task.FromResult<ExportJob?>(Exports.SingleOrDefault(job => job.TenantId == tenantId && job.Id == id));

    Task<IReadOnlyList<ExportJob>> IExportJobRepository.ListAsync(TenantId tenantId, int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExportJob>>(Exports.Where(job => job.TenantId == tenantId).OrderByDescending(job => job.QueuedAtUtc).Skip(skip).Take(take).ToList());

    void IExportJobRepository.Add(ExportJob job) => Exports.Add(job);

    Task<ExportRequest?> IExportRequestStore.FindAsync(TenantId tenantId, ExportJobId jobId, CancellationToken cancellationToken) =>
        Task.FromResult<ExportRequest?>(_requests.GetValueOrDefault((tenantId, jobId)));

    void IExportRequestStore.Add(TenantId tenantId, ExportJobId jobId, ExportRequest request) => _requests[(tenantId, jobId)] = request;

    Task<ReportDelivery?> IReportDeliveryStore.FindAsync(TenantId tenantId, ReportDefinitionId definitionId, CancellationToken cancellationToken) =>
        Task.FromResult<ReportDelivery?>(Deliveries.SingleOrDefault(delivery => delivery.TenantId == tenantId && delivery.DefinitionId == definitionId));

    void IReportDeliveryStore.Add(ReportDelivery delivery) => Deliveries.Add(delivery);

    void IReportDeliveryStore.Remove(ReportDelivery delivery) => Deliveries.Remove(delivery);

    Task<bool> IMetricsStore.RecordAsync(TenantId tenantId, string sourceKey, DailyFacts facts, CancellationToken cancellationToken)
    {
        if (!_sources.Add(sourceKey))
        {
            return Task.FromResult(false);
        }

        foreach (var fact in facts.Facts)
        {
            var key = (tenantId, facts.Location?.Value ?? Guid.Empty, facts.Day, fact.Metric, fact.Dimension, fact.Currency);
            _metrics[key] = _metrics.GetValueOrDefault(key) + fact.Value;
        }

        return Task.FromResult(true);
    }

    Task<IReadOnlyList<MetricRow>> IMetricsStore.QueryAsync(TenantId tenantId, MetricQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetricRow>>(Rows(query).Where(pair => pair.Tenant == tenantId).Select(pair => pair.Row).ToList());

    Task<IReadOnlyList<TenantMetricTotal>> IMetricsStore.TotalsByTenantAsync(string metric, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TenantMetricTotal>>(_metrics
            .Where(entry => entry.Key.Item4 == metric && entry.Key.Item3 >= from && entry.Key.Item3 <= to && entry.Value != 0)
            .GroupBy(entry => (Tenant: entry.Key.Item1, Currency: entry.Key.Item6))
            .Select(group => new TenantMetricTotal(group.Key.Tenant, group.Key.Currency, group.Sum(entry => entry.Value), group.Max(entry => entry.Key.Item3)))
            .ToList());

    Task<IReadOnlyList<MetricRow>> IMetricsStore.QueryAllTenantsAsync(MetricQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetricRow>>(Rows(query).Select(pair => pair.Row with { LocationId = Guid.Empty }).ToList());

    Task<ExportedFileReference> IExportFileStore.SaveAsync(TenantId tenantId, ExportJobId jobId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken)
    {
        var id = FileId.New();
        Files[(tenantId, id)] = new StoredExportFile(id, fileName, contentType, content);
        return Task.FromResult(new ExportedFileReference(tenantId, id));
    }

    Task<StoredExportFile?> IExportFileStore.OpenAsync(TenantId tenantId, FileId fileId, CancellationToken cancellationToken) =>
        Task.FromResult<StoredExportFile?>(Files.GetValueOrDefault((tenantId, fileId)));

    Task IExportFileStore.DeleteAsync(TenantId tenantId, FileId fileId, CancellationToken cancellationToken)
    {
        Files.Remove((tenantId, fileId));
        return Task.CompletedTask;
    }

    Task IPlatformDirectory.UpsertTenantAsync(PlatformTenantSnapshot snapshot, CancellationToken cancellationToken)
    {
        PlatformTenants[snapshot.TenantId] = snapshot;
        return Task.CompletedTask;
    }

    Task IPlatformDirectory.UpsertSubscriptionAsync(PlatformSubscriptionSnapshot snapshot, CancellationToken cancellationToken)
    {
        PlatformSubscriptions[snapshot.TenantId] = snapshot;
        return Task.CompletedTask;
    }

    Task<IReadOnlyList<PlatformTenantRow>> IPlatformDirectory.ListTenantsAsync(
        PlatformTenantStatus? status, int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlatformTenantRow>>(PlatformTenants.Values
            .Where(tenant => status is null || tenant.Status == status)
            .OrderBy(tenant => tenant.Name, StringComparer.Ordinal)
            .Skip(skip)
            .Take(take)
            .Select(tenant =>
            {
                var subscription = PlatformSubscriptions.GetValueOrDefault(tenant.TenantId);
                return new PlatformTenantRow(
                    tenant.TenantId.Value, tenant.Name, tenant.Status, tenant.CreatedAtUtc,
                    subscription?.Status, subscription?.PlanName, subscription?.MonthlyAmount, subscription?.CurrencyCode);
            })
            .ToList());

    Task<PlatformSummary> IPlatformDirectory.SummarizeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult(new PlatformSummary(
            PlatformTenants.Values.GroupBy(tenant => tenant.Status).OrderBy(group => group.Key)
                .Select(group => new StatusCount<PlatformTenantStatus>(group.Key, group.Count())).ToList(),
            PlatformTenants.Values.Count(tenant => tenant.CreatedAtUtc >= fromUtc && tenant.CreatedAtUtc < toUtc),
            PlatformSubscriptions.Values.GroupBy(subscription => subscription.Status).OrderBy(group => group.Key)
                .Select(group => new StatusCount<PlatformSubscriptionStatus>(group.Key, group.Count())).ToList(),
            PlatformSubscriptions.Values
                .Where(subscription => subscription.Status is PlatformSubscriptionStatus.Active or PlatformSubscriptionStatus.PastDue)
                .GroupBy(subscription => subscription.CurrencyCode)
                .Select(group => new AmountByCurrency(group.Key, group.Sum(subscription => subscription.MonthlyAmount)))
                .ToList()));

    private IEnumerable<(TenantId Tenant, MetricRow Row)> Rows(MetricQuery query) =>
        _metrics
            .Where(pair => pair.Key.Item3 >= query.From && pair.Key.Item3 <= query.To && query.Metrics.Contains(pair.Key.Item4))
            .Where(pair => query.Locations.Allows(pair.Key.Item2) && query.Currencies.Allows(pair.Key.Item6) && query.Dimensions.Allows(pair.Key.Item5))
            .Select(pair => (pair.Key.Item1, new MetricRow(pair.Key.Item3, pair.Key.Item2, pair.Key.Item4, pair.Key.Item5, pair.Key.Item6, pair.Value)));
}
