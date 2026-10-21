using Reporting.Application.Scheduling;
using Reporting.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Reporting.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Reporting : son propre schéma, jamais joint aux tables d'un autre module. Définitions,
/// exports, compteurs du jour et fichiers appartiennent à un tenant et sont filtrés sur le tenant courant. Les tables
/// de la plateforme (<c>platform_*</c>) n'ont pas de filtre : seules les routes d'administration et l'hôte les lisent.
/// Les jobs qui voient tous les tenants le déclarent (<c>IgnoreQueryFilters</c>).
/// </summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<ReportDefinition> Definitions => Set<ReportDefinition>();

    public DbSet<ExportJob> Exports => Set<ExportJob>();

    public DbSet<ReportDelivery> Deliveries => Set<ReportDelivery>();

    internal DbSet<ExportRequestRecord> ExportRequests => Set<ExportRequestRecord>();

    internal DbSet<DailyMetricRecord> DailyMetrics => Set<DailyMetricRecord>();

    internal DbSet<ProcessedSourceRecord> ProcessedSources => Set<ProcessedSourceRecord>();

    internal DbSet<StoredExportFileRecord> ExportFiles => Set<StoredExportFileRecord>();

    internal DbSet<PlatformTenantRecord> PlatformTenants => Set<PlatformTenantRecord>();

    internal DbSet<PlatformSubscriptionRecord> PlatformSubscriptions => Set<PlatformSubscriptionRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReportingDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}
