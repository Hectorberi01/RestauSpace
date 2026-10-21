namespace Reporting.Domain;

public sealed record ExportRequested(
    TenantId TenantId,
    ExportJobId ExportJobId,
    string ExportType,
    ExportRequester Actor,
    ReportDefinitionId? ReportDefinitionId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ExportCompleted(
    TenantId TenantId,
    ExportJobId ExportJobId,
    FileId FileId,
    UserId? RequestedBy,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ExportFailed(
    TenantId TenantId,
    ExportJobId ExportJobId,
    string Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Le fichier produit ne doit plus être téléchargeable ; le module Files réagit en le supprimant.</summary>
public sealed record ExportExpired(
    TenantId TenantId,
    ExportJobId ExportJobId,
    FileId FileId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
