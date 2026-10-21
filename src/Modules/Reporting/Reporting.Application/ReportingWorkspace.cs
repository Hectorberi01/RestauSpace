using Reporting.Application.Abstractions;
using Reporting.Application.Metrics;
using Reporting.Domain;

namespace Reporting.Application;

/// <summary>
/// Étapes communes aux cas d'usage. Le tenant vient du contexte (requête ou job), jamais d'une valeur de requête ; une
/// écriture du restaurant exige un tenant qui les accepte. Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class ReportingWorkspace(
    ITenantContext tenantContext,
    IReportingTenants tenants,
    IReportingCurrentUser currentUser,
    IReportDefinitionRepository definitions,
    IExportJobRepository exports,
    IExportRequestStore exportRequests,
    IReportDeliveryStore deliveries,
    IMetricsStore metrics,
    IExportFileStore files,
    ReportingSettings settings,
    IReportingUnitOfWork unitOfWork)
{
    public const int MaxTake = 100;

    public IReportDefinitionRepository Definitions => definitions;

    public IExportJobRepository Exports => exports;

    public IExportRequestStore ExportRequests => exportRequests;

    public IReportDeliveryStore Deliveries => deliveries;

    public IMetricsStore Metrics => metrics;

    public IExportFileStore Files => files;

    public ReportingSettings Settings => settings;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    /// <summary>L'auteur de la requête : l'utilisateur connecté, sinon la clé d'API ; refusé sans l'un ni l'autre.</summary>
    public ExportRequester RequireRequester() =>
        currentUser.UserId is { } user ? ExportRequester.ForUser(user)
        : currentUser.ApiCredentialId is { } credential ? ExportRequester.ForApiCredential(credential)
        : throw new ForbiddenException(ReportingApplicationErrors.UserRequired);

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (await tenants.FindAsync(CurrentTenant, cancellationToken) is not { AcceptsBusinessWrites: true })
        {
            throw new DomainException(ReportingApplicationErrors.TenantNotWritable);
        }
    }

    /// <summary>Le fuseau du restaurant (celui de ses journées) ; UTC s'il est inconnu.</summary>
    public async Task<TimeZoneId> TimeZoneAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        (await tenants.FindAsync(tenantId, cancellationToken))?.TimeZone ?? TimeZoneId.Utc;

    public Task<ReportingTenant?> TenantAsync(TenantId tenantId, CancellationToken cancellationToken) => tenants.FindAsync(tenantId, cancellationToken);

    public async Task<ReportDefinition> LoadDefinitionAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await definitions.FindAsync(CurrentTenant, ReportDefinitionId.From(id), cancellationToken))
        ?? throw new NotFoundException(ReportingApplicationErrors.DefinitionNotFound);

    public async Task<ExportJob> LoadExportAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await exports.FindAsync(CurrentTenant, ExportJobId.From(id), cancellationToken))
        ?? throw new NotFoundException(ReportingApplicationErrors.ExportNotFound);

    /// <summary>Période en jours locaux, bornes comprises, au plus <see cref="ReportingSettings.MaxPeriodDays"/> jours.</summary>
    public void EnsurePeriod(DateOnly from, DateOnly to) =>
        DomainException.ThrowIf(
            to < from || to.DayNumber - from.DayNumber + 1 > settings.MaxPeriodDays, ReportingApplicationErrors.PeriodInvalid);

    public static void EnsureGranularity(ReportGranularity granularity) =>
        DomainException.ThrowIf(!Enum.IsDefined(granularity), ReportingApplicationErrors.GranularityInvalid);

    public static void EnsurePage(int skip, int take) =>
        DomainException.ThrowIf(skip < 0 || take is < 1 or > MaxTake, ReportingApplicationErrors.PageInvalid);

    /// <summary>Exécute un rapport du restaurant du contexte : mesures et filtres DÉJÀ validés par le catalogue.</summary>
    public async Task<IReadOnlyList<ReportRow>> RunAsync(
        IReadOnlyList<string> metricCodes,
        IEnumerable<ReportFilter> filters,
        DateOnly from,
        DateOnly to,
        ReportGranularity granularity,
        bool byLocation,
        CancellationToken cancellationToken)
    {
        EnsurePeriod(from, to);
        EnsureGranularity(granularity);
        var query = ReportFilterRules.Translate(filters, from, to, ReportingMetrics.StoredInputs(metricCodes));
        var rows = await metrics.QueryAsync(CurrentTenant, query, cancellationToken);
        return ReportEngine.Compute(metricCodes, rows, from, to, granularity, byLocation);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}
