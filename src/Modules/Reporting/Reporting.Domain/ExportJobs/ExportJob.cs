namespace Reporting.Domain;

/// <summary>Demande d'export asynchrone. Un worker produit le fichier ; le job ne fait que suivre son cycle de vie.</summary>
public sealed class ExportJob : AggregateRoot<ExportJobId>, ITenantOwned
{
    public const int MaxFailureReasonLength = 500;

    private ExportJob(
        ExportJobId id,
        TenantId tenantId,
        ExportType type,
        ExportRequester requestedBy,
        ReportDefinitionId? reportDefinitionId,
        DateTimeOffset queuedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Type = type;
        RequesterKind = requestedBy.Kind;
        RequesterId = requestedBy.Id;
        ReportDefinitionId = reportDefinitionId;
        Status = ExportJobStatus.Queued;
        QueuedAtUtc = queuedAtUtc;
    }

    // Réservé à l'ORM.
    private ExportJob() => Type = null!;

    public TenantId TenantId { get; private set; }

    public ExportType Type { get; private set; }

    public ExportRequester RequestedBy => ExportRequester.From(RequesterKind, RequesterId);

    private ExportRequesterKind RequesterKind { get; set; }

    private Guid RequesterId { get; set; }

    /// <summary>La définition enregistrée qui est exportée, quand l'export n'est pas ponctuel.</summary>
    public ReportDefinitionId? ReportDefinitionId { get; private set; }

    public ExportJobStatus Status { get; private set; }

    /// <summary>Renseigné uniquement par <see cref="Complete"/> ; conservé après expiration pour la traçabilité.</summary>
    public FileId? FileId { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset QueuedAtUtc { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? FailedAtUtc { get; private set; }

    public DateTimeOffset? ExpiredAtUtc { get; private set; }

    public bool IsDownloadable => Status == ExportJobStatus.Completed;

    public static ExportJob Queue(
        TenantId tenantId,
        ExportType type,
        ExportRequester requestedBy,
        ReportDefinitionReference? reportDefinition,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(requestedBy);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        // La définition enregistrée doit appartenir au tenant qui demande l'export.
        DomainException.ThrowIf(
            reportDefinition is not null && reportDefinition.TenantId != tenantId,
            CommonErrors.TenantMismatch);

        var now = clock.UtcNow;
        var reportDefinitionId = reportDefinition?.ReportDefinitionId;
        var job = new ExportJob(ExportJobId.New(), tenantId, type, requestedBy, reportDefinitionId, now);
        job.Raise(new ExportRequested(tenantId, job.Id, type.Value, requestedBy, reportDefinitionId, now));
        return job;
    }

    public void Start(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(Status != ExportJobStatus.Queued, ExportErrors.NotQueued);

        Status = ExportJobStatus.Running;
        StartedAtUtc = clock.UtcNow;
    }

    public void Complete(ExportedFileReference file, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(Status != ExportJobStatus.Running, ExportErrors.NotRunning);
        DomainException.ThrowIf(file.FileId == default, ExportErrors.FileRequired);
        this.EnsureSameTenant(file.TenantId);

        var now = clock.UtcNow;
        Status = ExportJobStatus.Completed;
        FileId = file.FileId;
        CompletedAtUtc = now;

        Raise(new ExportCompleted(TenantId, Id, file.FileId, RequestedBy.User, now));
    }

    public void Fail(string? reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(Status != ExportJobStatus.Running, ExportErrors.NotRunning);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(reason), ExportErrors.FailureReasonInvalid);

        var trimmedReason = reason!.Trim();
        DomainException.ThrowIf(trimmedReason.Length > MaxFailureReasonLength, ExportErrors.FailureReasonInvalid);

        var now = clock.UtcNow;
        Status = ExportJobStatus.Failed;
        FailureReason = trimmedReason;
        FailedAtUtc = now;

        Raise(new ExportFailed(TenantId, Id, trimmedReason, now));
    }

    public void Expire(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(Status != ExportJobStatus.Completed, ExportErrors.NotCompleted);

        var now = clock.UtcNow;
        Status = ExportJobStatus.Expired;
        ExpiredAtUtc = now;

        Raise(new ExportExpired(TenantId, Id, FileId!.Value, now));
    }

    private void EnsureNotTerminal()
    {
        DomainException.ThrowIf(Status == ExportJobStatus.Failed, ExportErrors.AlreadyFailed);
        DomainException.ThrowIf(Status == ExportJobStatus.Expired, ExportErrors.AlreadyExpired);
    }
}
