using Reporting.Domain;

namespace Reporting.Application.Scheduling;

/// <summary>
/// L'état d'envoi d'un rapport planifié : qui l'a planifié, personne ou clé d'API (l'export est fait en son nom) et quand il part la prochaine
/// fois. Le planning lui-même (fréquence, heure locale, destinataires) reste dans <see cref="ReportDefinition"/>.
/// </summary>
public sealed class ReportDelivery : ITenantOwned
{
    private ReportDelivery(TenantId tenantId, ReportDefinitionId definitionId, ExportRequester scheduledBy, DateTimeOffset nextRunAtUtc)
    {
        TenantId = tenantId;
        DefinitionId = definitionId;
        SchedulerKind = scheduledBy.Kind;
        SchedulerId = scheduledBy.Id;
        NextRunAtUtc = nextRunAtUtc;
    }

    // Réservé à l'ORM.
    private ReportDelivery()
    {
    }

    public TenantId TenantId { get; private set; }

    public ReportDefinitionId DefinitionId { get; private set; }

    public ExportRequester ScheduledBy => ExportRequester.From(SchedulerKind, SchedulerId);

    private ExportRequesterKind SchedulerKind { get; set; }

    private Guid SchedulerId { get; set; }

    public DateTimeOffset NextRunAtUtc { get; private set; }

    public DateTimeOffset? LastRunAtUtc { get; private set; }

    public static ReportDelivery Create(ReportDefinition definition, ExportRequester scheduledBy, DateTimeOffset nextRunAtUtc)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(scheduledBy);
        return new ReportDelivery(definition.TenantId, definition.Id, scheduledBy, nextRunAtUtc);
    }

    /// <summary>Nouveau planning : nouvelle prochaine exécution, au nom de la personne (ou de la clé d'API) qui vient de le changer.</summary>
    public void Reschedule(ExportRequester scheduledBy, DateTimeOffset nextRunAtUtc)
    {
        ArgumentNullException.ThrowIfNull(scheduledBy);
        SchedulerKind = scheduledBy.Kind;
        SchedulerId = scheduledBy.Id;
        NextRunAtUtc = nextRunAtUtc;
    }

    /// <summary>Une exécution a eu lieu (ou a été sautée) : la suivante est calculée à partir de maintenant.</summary>
    public void MarkRun(DateTimeOffset ranAtUtc, DateTimeOffset nextRunAtUtc)
    {
        LastRunAtUtc = ranAtUtc;
        NextRunAtUtc = nextRunAtUtc;
    }
}
