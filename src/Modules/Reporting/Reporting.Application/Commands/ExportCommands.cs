using Reporting.Application.Abstractions;
using Reporting.Application.Exports;
using Reporting.Application.Metrics;
using Reporting.Application.Scheduling;
using Reporting.Domain;

namespace Reporting.Application.Commands;

// Exports CSV : demandés par le personnel ou par un envoi planifié, produits par le job du module, téléchargeables
// jusqu'à leur expiration. Un export ne fait que LIRE les données du restaurant : il reste possible pour un restaurant
// suspendu (il garde l'accès à ses chiffres).

/// <summary>
/// Demande un export : d'un rapport enregistré (<paramref name="DefinitionId"/>), ou ponctuel (mesures et filtres
/// donnés). Période en jours locaux, bornes comprises. Renvoie l'id du job.
/// </summary>
public sealed record RequestExportCommand(
    Guid? DefinitionId,
    IReadOnlyList<string>? Metrics,
    IReadOnlyList<ReportFilterInput>? Filters,
    DateOnly From,
    DateOnly To,
    ReportGranularity Granularity = ReportGranularity.Day,
    bool ByLocation = false,
    ExportFormat Format = ExportFormat.Csv) : ICommand<Guid>;

public sealed class RequestExportHandler(ReportingWorkspace workspace, IClock clock) : ICommandHandler<RequestExportCommand, Guid>
{
    public const string AdHocTitle = "Rapport";

    public async Task<Guid> HandleAsync(RequestExportCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = workspace.RequireRequester();
        workspace.EnsurePeriod(command.From, command.To);
        ReportingWorkspace.EnsureGranularity(command.Granularity);

        string title;
        IReadOnlyCollection<string> metrics;
        IReadOnlyCollection<ReportFilter> filters;
        ReportDefinitionReference? reference = null;
        if (command.DefinitionId is { } definitionId)
        {
            var definition = await workspace.LoadDefinitionAsync(definitionId, cancellationToken);
            definition.EnsureCompatibleWith(ReportingMetrics.Catalog);
            (title, metrics, filters, reference) = (definition.Name, definition.Metrics.Codes, definition.Filters.Items, definition.ToReference());
        }
        else
        {
            title = AdHocTitle;
            metrics = MetricSet.Create(command.Metrics ?? Array.Empty<string>(), ReportingMetrics.Catalog).Codes;
            filters = ReportFilters.Create(ReportFilterInput.ToFilters(command.Filters), ReportingMetrics.Catalog).Items;
        }

        DomainException.ThrowIf(!Enum.IsDefined(command.Format), ExportErrors.TypeInvalid);
        var job = ExportJob.Queue(workspace.CurrentTenant, ExportType.Create(ExportFormats.TypeCode(command.Format)), user, reference, clock);
        workspace.Exports.Add(job);
        workspace.ExportRequests.Add(
            job.TenantId,
            job.Id,
            new ExportRequest(
                title, command.From, command.To, command.Granularity, command.ByLocation,
                [.. metrics], [.. filters.Select(ExportFilter.From)], []));
        await workspace.SaveAsync(cancellationToken);
        return job.Id.Value;
    }
}

/// <summary>
/// Demande l'export comptable d'une période (journal des ventes et de la trésorerie, par jour) avec ce plan de comptes
/// (le plan SYSCOHADA par défaut). Renvoie l'id du job.
/// </summary>
public sealed record RequestAccountingExportCommand(DateOnly From, DateOnly To, AccountingPlan? Plan = null) : ICommand<Guid>;

public sealed class RequestAccountingExportHandler(ReportingWorkspace workspace, IClock clock) : ICommandHandler<RequestAccountingExportCommand, Guid>
{
    public const string Title = "Journal comptable";

    public async Task<Guid> HandleAsync(RequestAccountingExportCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = workspace.RequireRequester();
        workspace.EnsurePeriod(command.From, command.To);
        var plan = command.Plan ?? AccountingPlan.Default;
        plan.Validate();

        var job = ExportJob.Queue(workspace.CurrentTenant, ExportType.Create(ExportFormats.AccountingCsv), user, null, clock);
        workspace.Exports.Add(job);
        workspace.ExportRequests.Add(
            job.TenantId,
            job.Id,
            new ExportRequest(Title, command.From, command.To, ReportGranularity.Day, false, [.. AccountingJournalWriter.Metrics], [], [], plan));
        await workspace.SaveAsync(cancellationToken);
        return job.Id.Value;
    }
}

public enum ExportOutcome
{
    Nothing = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>
/// Job : produit le fichier d'un export en file pour le tenant du contexte. Le passage à « en cours » est enregistré
/// d'abord : deux workers sur le même job, le second échoue sur la version (concurrence optimiste). Une règle qui ne
/// passe plus (mesure retirée du catalogue...) fait échouer l'export avec son code d'erreur.
/// </summary>
public sealed record GenerateExportCommand(Guid ExportJobId) : ICommand<ExportOutcome>;

public sealed class GenerateExportHandler(ReportingWorkspace workspace, IClock clock, IReportingLocationNames? locationNames = null)
    : ICommandHandler<GenerateExportCommand, ExportOutcome>
{
    public const string RequestMissing = "reporting.export_job.request_missing";

    public async Task<ExportOutcome> HandleAsync(GenerateExportCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var job = command.ExportJobId == Guid.Empty
            ? null
            : await workspace.Exports.FindAsync(tenant, ExportJobId.From(command.ExportJobId), cancellationToken);
        if (job is not { Status: ExportJobStatus.Queued })
        {
            return ExportOutcome.Nothing;
        }

        job.Start(clock);
        await workspace.SaveAsync(cancellationToken);

        var request = await workspace.ExportRequests.FindAsync(tenant, job.Id, cancellationToken);
        if (request is null)
        {
            job.Fail(RequestMissing, clock);
            await workspace.SaveAsync(cancellationToken);
            return ExportOutcome.Failed;
        }

        StoredFileOutcome stored;
        try
        {
            var metrics = MetricSet.Create(request.Metrics, ReportingMetrics.Catalog).Codes.ToList();
            var filters = ReportFilters.Create(request.Filters.Select(filter => filter.ToFilter()), ReportingMetrics.Catalog).Items;
            var rows = await workspace.RunAsync(metrics, filters, request.From, request.To, request.Granularity, request.ByLocation, cancellationToken);
            var type = job.Type.Value;
            var name = ExportFormats.FileName(request.Title, request.From, request.To, type);
            stored = type switch
            {
                ExportFormats.ReportXlsx => new StoredFileOutcome(name, XlsxWriter.Write(await TableAsync(request, rows, cancellationToken)), null),
                ExportFormats.ReportPdf => new StoredFileOutcome(name, PdfWriter.Write(await TableAsync(request, rows, cancellationToken)), null),
                ExportFormats.AccountingCsv => new StoredFileOutcome(name, AccountingJournalWriter.Write(rows, request.Accounting ?? AccountingPlan.Default), null),
                _ => new StoredFileOutcome(name, CsvReportWriter.Write(rows), null),
            };
        }
        catch (DomainException exception)
        {
            stored = new StoredFileOutcome(string.Empty, [], exception.Code);
        }

        if (stored.FailureCode is { } code)
        {
            job.Fail(code, clock);
            await workspace.SaveAsync(cancellationToken);
            return ExportOutcome.Failed;
        }

        var file = await workspace.Files.SaveAsync(tenant, job.Id, stored.FileName, ExportFormats.ContentType(job.Type.Value), stored.Content, cancellationToken);
        job.Complete(file, clock);
        await workspace.SaveAsync(cancellationToken);
        return ExportOutcome.Completed;
    }

    private async Task<ExportTable> TableAsync(ExportRequest request, IReadOnlyList<ReportRow> rows, CancellationToken cancellationToken)
    {
        var names = request.ByLocation && locationNames is not null
            ? await locationNames.ListAsync(workspace.CurrentTenant, cancellationToken)
            : new Dictionary<Guid, string>();
        return ExportTable.FromReport(request.Title, request.From, request.To, rows, request.ByLocation, names);
    }

    private sealed record StoredFileOutcome(string FileName, byte[] Content, string? FailureCode);
}

/// <summary>Job : un export terminé depuis plus que la durée de conservation expire ; son fichier est supprimé ensuite (ExportExpired).</summary>
public sealed record ExpireExportCommand(Guid ExportJobId) : ICommand<bool>;

public sealed class ExpireExportHandler(ReportingWorkspace workspace, IClock clock) : ICommandHandler<ExpireExportCommand, bool>
{
    public async Task<bool> HandleAsync(ExpireExportCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var job = command.ExportJobId == Guid.Empty
            ? null
            : await workspace.Exports.FindAsync(tenant, ExportJobId.From(command.ExportJobId), cancellationToken);
        if (job is not { Status: ExportJobStatus.Completed, CompletedAtUtc: { } completedAt, FileId: { } fileId }
            || completedAt + workspace.Settings.ExportRetention > clock.UtcNow)
        {
            return false;
        }

        job.Expire(clock);
        await workspace.SaveAsync(cancellationToken);
        return true;
    }
}

public sealed record DeleteExpiredExportFileCommand(Guid ExportJobId) : ICommand<bool>;

public sealed class DeleteExpiredExportFileHandler(ReportingWorkspace workspace) : ICommandHandler<DeleteExpiredExportFileCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteExpiredExportFileCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var job = command.ExportJobId == Guid.Empty
            ? null
            : await workspace.Exports.FindAsync(tenant, ExportJobId.From(command.ExportJobId), cancellationToken);
        if (job is not { Status: ExportJobStatus.Expired, FileId: { } fileId })
        {
            return false;
        }

        await workspace.Files.DeleteAsync(tenant, fileId, cancellationToken);
        return true;
    }
}

public enum ScheduledRunOutcome
{
    Nothing = 1,
    Queued = 2,

    /// <summary>Restaurant suspendu ou fermé : l'envoi est sauté, le suivant reste prévu.</summary>
    Skipped = 3,

    /// <summary>Le rapport ne passe plus le catalogue : il est déplanifié.</summary>
    Unscheduled = 4,
}

/// <summary>
/// Job : un rapport planifié arrivé à échéance pour le tenant du contexte. Un export de la période couverte est mis en
/// file avec les destinataires ; l'hôte leur envoie le lien quand il est prêt. La prochaine exécution est calculée à
/// partir de maintenant (pas de rattrapage en rafale après une interruption).
/// </summary>
public sealed record RunScheduledReportCommand(Guid DefinitionId) : ICommand<ScheduledRunOutcome>;

public sealed class RunScheduledReportHandler(ReportingWorkspace workspace, IClock clock) : ICommandHandler<RunScheduledReportCommand, ScheduledRunOutcome>
{
    public async Task<ScheduledRunOutcome> HandleAsync(RunScheduledReportCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        if (command.DefinitionId == Guid.Empty)
        {
            return ScheduledRunOutcome.Nothing;
        }

        var definitionId = ReportDefinitionId.From(command.DefinitionId);
        var delivery = await workspace.Deliveries.FindAsync(tenant, definitionId, cancellationToken);
        var now = clock.UtcNow;
        if (delivery is null || delivery.NextRunAtUtc > now)
        {
            return ScheduledRunOutcome.Nothing;
        }

        var definition = await workspace.Definitions.FindAsync(tenant, definitionId, cancellationToken);
        if (definition?.Schedule is not { } schedule)
        {
            workspace.Deliveries.Remove(delivery);
            await workspace.SaveAsync(cancellationToken);
            return ScheduledRunOutcome.Nothing;
        }

        try
        {
            definition.EnsureCompatibleWith(ReportingMetrics.Catalog);
        }
        catch (DomainException)
        {
            definition.Unschedule(clock);
            workspace.Deliveries.Remove(delivery);
            await workspace.SaveAsync(cancellationToken);
            return ScheduledRunOutcome.Unscheduled;
        }

        var next = ReportOccurrences.NextAfter(schedule, now);
        if (await workspace.TenantAsync(tenant, cancellationToken) is not { AcceptsBusinessWrites: true })
        {
            delivery.MarkRun(now, next);
            await workspace.SaveAsync(cancellationToken);
            return ScheduledRunOutcome.Skipped;
        }

        var (from, to) = ReportOccurrences.CoveredDays(schedule, delivery.NextRunAtUtc);
        var job = ExportJob.Queue(tenant, ExportType.Create(CsvReportWriter.ExportTypeCode), delivery.ScheduledBy, definition.ToReference(), clock);
        workspace.Exports.Add(job);
        workspace.ExportRequests.Add(
            tenant,
            job.Id,
            new ExportRequest(
                definition.Name, from, to, ReportGranularity.Day, false,
                [.. definition.Metrics.Codes],
                [.. definition.Filters.Items.Select(ExportFilter.From)],
                [.. schedule.Recipients.Select(recipient => recipient.Value)]));
        delivery.MarkRun(now, next);
        await workspace.SaveAsync(cancellationToken);
        return ScheduledRunOutcome.Queued;
    }
}
