using Reporting.Application.Exports;
using Reporting.Application.Metrics;
using Reporting.Application.Platform;
using Reporting.Application.Scheduling;
using Reporting.Domain;

namespace Reporting.Application.Abstractions;

// Ports du module Reporting. Les projections (compteurs du jour, état de la plateforme) sont écrites hors des agrégats ;
// les définitions et les exports passent par l'unit of work. Le tenant est TOUJOURS donné explicitement, celui du
// contexte côté restaurant ; seules les lectures de la plateforme et des jobs (ports « Platform ») voient tous les tenants.

public interface IReportDefinitionRepository
{
    Task<ReportDefinition?> FindAsync(TenantId tenantId, ReportDefinitionId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par nom.</summary>
    Task<IReadOnlyList<ReportDefinition>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(ReportDefinition definition);
}

public interface IExportJobRepository
{
    Task<ExportJob?> FindAsync(TenantId tenantId, ExportJobId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<ExportJob>> ListAsync(TenantId tenantId, int skip, int take, CancellationToken cancellationToken = default);

    void Add(ExportJob job);
}

/// <summary>Les paramètres d'un export (période, mesures, destinataires), écrits avec le job.</summary>
public interface IExportRequestStore
{
    Task<ExportRequest?> FindAsync(TenantId tenantId, ExportJobId jobId, CancellationToken cancellationToken = default);

    void Add(TenantId tenantId, ExportJobId jobId, ExportRequest request);
}

/// <summary>L'état d'envoi d'un rapport planifié (prochaine exécution, qui l'a planifié).</summary>
public interface IReportDeliveryStore
{
    Task<ReportDelivery?> FindAsync(TenantId tenantId, ReportDefinitionId definitionId, CancellationToken cancellationToken = default);

    void Add(ReportDelivery delivery);

    void Remove(ReportDelivery delivery);
}

/// <summary>Un travail d'arrière-plan à mener pour un tenant (job de la plateforme).</summary>
public sealed record TenantWork(TenantId TenantId, Guid Id);

/// <summary>Pour les jobs SEULEMENT : tous les tenants. Jamais utilisé par une route d'un restaurant.</summary>
public interface IReportingWorkQueue
{
    Task<IReadOnlyList<TenantWork>> ListDueDeliveriesAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantWork>> ListQueuedExportsAsync(int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantWork>> ListExpiredExportsAsync(DateTimeOffset completedBeforeUtc, int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// Les compteurs quotidiens d'un restaurant (projection). <see cref="RecordAsync"/> est ATOMIQUE et idempotent : une
/// même source (message de l'outbox) n'est comptée qu'une fois, même livrée deux fois.
/// </summary>
public interface IMetricsStore
{
    /// <returns>Faux si la source avait déjà été comptée (rien n'est écrit).</returns>
    Task<bool> RecordAsync(TenantId tenantId, string sourceKey, DailyFacts facts, CancellationToken cancellationToken = default);

    /// <summary>Les compteurs du tenant sur la période (jours locaux, bornes comprises), filtres appliqués en base.</summary>
    Task<IReadOnlyList<MetricRow>> QueryAsync(TenantId tenantId, MetricQuery query, CancellationToken cancellationToken = default);

    /// <summary>PLATEFORME : les mêmes compteurs, tous restaurants confondus.</summary>
    Task<IReadOnlyList<MetricRow>> QueryAllTenantsAsync(MetricQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// PLATEFORME : par restaurant et par devise, la somme d'une mesure stockée sur la période (jours bornes comprises) et
    /// le dernier jour où elle a été comptée. Les restaurants sans valeur n'apparaissent pas.
    /// </summary>
    Task<IReadOnlyList<TenantMetricTotal>> TotalsByTenantAsync(string metric, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public sealed record TenantMetricTotal(TenantId TenantId, string Currency, decimal Value, DateOnly LastDay);

/// <summary>PLATEFORME : l'état courant de chaque restaurant et de son abonnement (projection).</summary>
public interface IPlatformDirectory
{
    Task UpsertTenantAsync(PlatformTenantSnapshot snapshot, CancellationToken cancellationToken = default);

    Task UpsertSubscriptionAsync(PlatformSubscriptionSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformTenantRow>> ListTenantsAsync(PlatformTenantStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    Task<PlatformSummary> SummarizeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stockage des fichiers d'export. Implémentation par défaut : en base dans le schéma du module (dans la transaction de
/// l'unit of work) ; l'hôte la remplace par le module Files (Cloudflare R2). Le contenu n'est jamais journalisé.
/// </summary>
public interface IExportFileStore
{
    /// <param name="jobId">L'export propriétaire du fichier (le stockage le rattache à lui).</param>
    Task<ExportedFileReference> SaveAsync(
        TenantId tenantId, ExportJobId jobId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default);

    Task<StoredExportFile?> OpenAsync(TenantId tenantId, FileId fileId, CancellationToken cancellationToken = default);

    Task DeleteAsync(TenantId tenantId, FileId fileId, CancellationToken cancellationToken = default);
}

/// <summary>Les noms des établissements du tenant du contexte (fichiers lus par une personne), fournis par l'hôte depuis Locations.</summary>
public interface IReportingLocationNames
{
    Task<IReadOnlyDictionary<Guid, string>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Le tenant du contexte a-t-il plus d'un établissement non archivé ? Fourni par l'hôte depuis Locations.</summary>
public interface IReportingLocations
{
    Task<bool> HasSeveralAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Sans hôte : un seul établissement.</summary>
public sealed class SingleReportingLocation : IReportingLocations
{
    public Task<bool> HasSeveralAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

/// <summary>Ce que Reporting doit savoir d'un restaurant, fourni par l'hôte depuis Tenants.</summary>
public sealed record ReportingTenant(TenantId TenantId, string Name, TimeZoneId TimeZone, bool AcceptsBusinessWrites);

public interface IReportingTenants
{
    /// <summary>Null pour un tenant inconnu.</summary>
    Task<ReportingTenant?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>L'auteur de la requête : l'utilisateur (claim <c>sub</c>) ou la clé d'API ; null hors requête.</summary>
public interface IReportingCurrentUser
{
    UserId? UserId { get; }

    /// <summary>La clé d'API de la requête (claim <c>cid</c>) ; null pour un utilisateur ou hors requête.</summary>
    Guid? ApiCredentialId => null;
}
