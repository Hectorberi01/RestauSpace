using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reporting.Application.Platform;
using Reporting.Application.Scheduling;
using Reporting.Domain;

namespace Reporting.Infrastructure.Persistence.Configurations;

/// <summary>Agrégat <see cref="ReportDefinition"/> : table <c>report_definitions</c> ; mesures, filtres et planning en JSON.</summary>
internal sealed class ReportDefinitionConfiguration : IEntityTypeConfiguration<ReportDefinition>
{
    public void Configure(EntityTypeBuilder<ReportDefinition> builder)
    {
        builder.ToTable("report_definitions");
        builder.HasKey(definition => definition.Id);
        builder.Property(definition => definition.Id).HasColumnName("id").HasConversion(ReportingConverters.DefinitionKey).ValueGeneratedNever();
        builder.Property(definition => definition.TenantId).HasColumnName("tenant_id").HasConversion(ReportingConverters.TenantKey);
        builder.Property(definition => definition.Name).HasColumnName("name").HasMaxLength(ReportDefinition.MaxNameLength);
        builder.Property(definition => definition.Metrics).HasColumnName("metrics").HasColumnType("jsonb").HasConversion(ReportingConverters.Metrics);
        builder.Property(definition => definition.Filters).HasColumnName("filters").HasColumnType("jsonb").HasConversion(ReportingConverters.Filters);
        builder.Property(definition => definition.Schedule).HasColumnName("schedule").HasColumnType("jsonb").HasConversion(ReportingConverters.Schedule);
        builder.Property(definition => definition.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(definition => definition.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Ignore(definition => definition.IsScheduled);
        builder.HasIndex(definition => new { definition.TenantId, definition.Name }).HasDatabaseName("ix_report_definitions_tenant_id_name");
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(definition => definition.DomainEvents);
    }
}

/// <summary>Agrégat <see cref="ExportJob"/> : table <c>export_jobs</c>.</summary>
internal sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        var completed = (int)ExportJobStatus.Completed;
        var expired = (int)ExportJobStatus.Expired;
        var failed = (int)ExportJobStatus.Failed;
        builder.ToTable("export_jobs", table =>
        {
            table.HasCheckConstraint("ck_export_jobs_status", Checks.In<ExportJobStatus>("status"));
            table.HasCheckConstraint("ck_export_jobs_requested_by_kind", Checks.In<ExportRequesterKind>("requested_by_kind"));
            // Un export terminé (puis expiré) a toujours son fichier ; un export en échec, sa raison.
            table.HasCheckConstraint("ck_export_jobs_file", $"status NOT IN ({completed}, {expired}) OR file_id IS NOT NULL");
            table.HasCheckConstraint("ck_export_jobs_failure_reason", $"status <> {failed} OR failure_reason IS NOT NULL");
        });

        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).HasColumnName("id").HasConversion(ReportingConverters.ExportKey).ValueGeneratedNever();
        builder.Property(job => job.TenantId).HasColumnName("tenant_id").HasConversion(ReportingConverters.TenantKey);
        builder.Property(job => job.Type).HasColumnName("type").HasConversion(ReportingConverters.ExportTypeCode).HasMaxLength(64);
        builder.Ignore(job => job.RequestedBy);
        builder.Property<Guid>("RequesterId").HasColumnName("requested_by");
        builder.Property<ExportRequesterKind>("RequesterKind").HasColumnName("requested_by_kind").HasDefaultValue(ExportRequesterKind.User).HasSentinel((ExportRequesterKind)0);
        builder.Property(job => job.ReportDefinitionId).HasColumnName("report_definition_id").HasConversion(ReportingConverters.OptionalDefinitionKey);
        builder.Property(job => job.Status).HasColumnName("status");
        builder.Property(job => job.FileId).HasColumnName("file_id").HasConversion(ReportingConverters.OptionalFile);
        builder.Property(job => job.FailureReason).HasColumnName("failure_reason").HasMaxLength(ExportJob.MaxFailureReasonLength);
        builder.Property(job => job.QueuedAtUtc).HasColumnName("queued_at_utc");
        builder.Property(job => job.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(job => job.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(job => job.FailedAtUtc).HasColumnName("failed_at_utc");
        builder.Property(job => job.ExpiredAtUtc).HasColumnName("expired_at_utc");
        builder.Ignore(job => job.IsDownloadable);

        // Historique d'un restaurant ; file d'attente et expiration du job (par statut).
        builder.HasIndex(job => new { job.TenantId, job.QueuedAtUtc }).HasDatabaseName("ix_export_jobs_tenant_id_queued_at_utc");
        builder.HasIndex(job => new { job.Status, job.QueuedAtUtc }).HasDatabaseName("ix_export_jobs_status_queued_at_utc");

        // Concurrence optimiste : deux workers sur le même job, le second échoue.
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(job => job.DomainEvents);
    }
}

internal sealed class ExportRequestConfiguration : IEntityTypeConfiguration<ExportRequestRecord>
{
    public void Configure(EntityTypeBuilder<ExportRequestRecord> builder)
    {
        builder.ToTable("export_requests");
        builder.HasKey(request => request.ExportJobId);
        builder.Property(request => request.ExportJobId).HasColumnName("export_job_id").HasConversion(ReportingConverters.ExportKey).ValueGeneratedNever();
        builder.Property(request => request.TenantId).HasColumnName("tenant_id").HasConversion(ReportingConverters.TenantKey);
        builder.Property(request => request.Parameters).HasColumnName("parameters").HasColumnType("jsonb");
        builder.HasOne<ExportJob>().WithOne().HasForeignKey<ExportRequestRecord>(request => request.ExportJobId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>État d'envoi d'un rapport planifié : un par définition, version optimiste (job et personnel en même temps).</summary>
internal sealed class ReportDeliveryConfiguration : IEntityTypeConfiguration<ReportDelivery>
{
    public void Configure(EntityTypeBuilder<ReportDelivery> builder)
    {
        builder.ToTable("report_deliveries", table =>
            table.HasCheckConstraint("ck_report_deliveries_scheduled_by_kind", Checks.In<ExportRequesterKind>("scheduled_by_kind")));
        builder.HasKey(delivery => delivery.DefinitionId);
        builder.Property(delivery => delivery.DefinitionId).HasColumnName("report_definition_id").HasConversion(ReportingConverters.DefinitionKey).ValueGeneratedNever();
        builder.Property(delivery => delivery.TenantId).HasColumnName("tenant_id").HasConversion(ReportingConverters.TenantKey);
        builder.Ignore(delivery => delivery.ScheduledBy);
        builder.Property<Guid>("SchedulerId").HasColumnName("scheduled_by");
        builder.Property<ExportRequesterKind>("SchedulerKind").HasColumnName("scheduled_by_kind").HasDefaultValue(ExportRequesterKind.User).HasSentinel((ExportRequesterKind)0);
        builder.Property(delivery => delivery.NextRunAtUtc).HasColumnName("next_run_at_utc");
        builder.Property(delivery => delivery.LastRunAtUtc).HasColumnName("last_run_at_utc");
        builder.HasIndex(delivery => delivery.NextRunAtUtc).HasDatabaseName("ix_report_deliveries_next_run_at_utc");
        builder.HasOne<ReportDefinition>().WithOne().HasForeignKey<ReportDelivery>(delivery => delivery.DefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.Property<uint>("Version").IsRowVersion();
    }
}

/// <summary>
/// Les compteurs du jour : une ligne par (tenant, établissement, jour, mesure, dimension, devise), incrémentée en place
/// par un upsert (pas de version : l'incrément est atomique en base).
/// </summary>
internal sealed class DailyMetricConfiguration : IEntityTypeConfiguration<DailyMetricRecord>
{
    public void Configure(EntityTypeBuilder<DailyMetricRecord> builder)
    {
        builder.ToTable("daily_metrics", table =>
            table.HasCheckConstraint("ck_daily_metrics_currency", Checks.CurrencyOrEmpty("currency")));
        builder.HasKey(row => new { row.TenantId, row.LocationId, row.Day, row.Metric, row.Dimension, row.Currency });
        builder.Property(row => row.TenantId).HasColumnName("tenant_id").HasConversion(ReportingConverters.TenantKey);
        builder.Property(row => row.LocationId).HasColumnName("location_id");
        builder.Property(row => row.Day).HasColumnName("day");
        builder.Property(row => row.Metric).HasColumnName("metric").HasMaxLength(64);
        builder.Property(row => row.Dimension).HasColumnName("dimension").HasMaxLength(64);
        builder.Property(row => row.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(row => row.Value).HasColumnName("value").HasPrecision(MetricsStore.ValuePrecision, MetricsStore.ValueScale);
        // Activité de la plateforme : une période, toutes les lignes de quelques mesures.
        builder.HasIndex(row => new { row.Day, row.Metric }).HasDatabaseName("ix_daily_metrics_day_metric");
    }
}

internal sealed class ProcessedSourceConfiguration : IEntityTypeConfiguration<ProcessedSourceRecord>
{
    public void Configure(EntityTypeBuilder<ProcessedSourceRecord> builder)
    {
        builder.ToTable("processed_sources");
        builder.HasKey(source => source.SourceKey);
        builder.Property(source => source.SourceKey).HasColumnName("source_key").HasMaxLength(120);
        builder.Property(source => source.TenantId).HasColumnName("tenant_id").HasConversion(ReportingConverters.TenantKey);
        builder.Property(source => source.ProcessedAtUtc).HasColumnName("processed_at_utc");
    }
}

internal sealed class StoredExportFileConfiguration : IEntityTypeConfiguration<StoredExportFileRecord>
{
    public void Configure(EntityTypeBuilder<StoredExportFileRecord> builder)
    {
        builder.ToTable("export_files");
        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(file => file.TenantId).HasColumnName("tenant_id").HasConversion(ReportingConverters.TenantKey);
        builder.Property(file => file.FileName).HasColumnName("file_name").HasMaxLength(200);
        builder.Property(file => file.ContentType).HasColumnName("content_type").HasMaxLength(100);
        builder.Property(file => file.Content).HasColumnName("content");
        builder.Property(file => file.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(file => file.TenantId).HasDatabaseName("ix_export_files_tenant_id");
    }
}

internal sealed class PlatformTenantConfiguration : IEntityTypeConfiguration<PlatformTenantRecord>
{
    public void Configure(EntityTypeBuilder<PlatformTenantRecord> builder)
    {
        builder.ToTable("platform_tenants", table =>
            table.HasCheckConstraint("ck_platform_tenants_status", Checks.In<PlatformTenantStatus>("status")));
        builder.HasKey(tenant => tenant.TenantId);
        builder.Property(tenant => tenant.TenantId).HasColumnName("tenant_id").ValueGeneratedNever();
        builder.Property(tenant => tenant.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(tenant => tenant.Status).HasColumnName("status");
        builder.Property(tenant => tenant.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(tenant => tenant.ObservedAtUtc).HasColumnName("observed_at_utc");
        builder.HasIndex(tenant => tenant.Status).HasDatabaseName("ix_platform_tenants_status");
    }
}

internal sealed class PlatformSubscriptionConfiguration : IEntityTypeConfiguration<PlatformSubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<PlatformSubscriptionRecord> builder)
    {
        builder.ToTable("platform_subscriptions", table =>
        {
            table.HasCheckConstraint("ck_platform_subscriptions_status", Checks.In<PlatformSubscriptionStatus>("status"));
            table.HasCheckConstraint("ck_platform_subscriptions_currency_code", "currency_code ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("ck_platform_subscriptions_monthly_amount", "monthly_amount >= 0");
        });
        builder.HasKey(subscription => subscription.TenantId);
        builder.Property(subscription => subscription.TenantId).HasColumnName("tenant_id").ValueGeneratedNever();
        builder.Property(subscription => subscription.SubscriptionId).HasColumnName("subscription_id");
        builder.Property(subscription => subscription.PlanId).HasColumnName("plan_id");
        builder.Property(subscription => subscription.PlanName).HasColumnName("plan_name").HasMaxLength(200);
        builder.Property(subscription => subscription.Status).HasColumnName("status");
        builder.Property(subscription => subscription.MonthlyAmount).HasColumnName("monthly_amount").HasPrecision(19, 3);
        builder.Property(subscription => subscription.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsFixedLength();
        builder.Property(subscription => subscription.ObservedAtUtc).HasColumnName("observed_at_utc");
        builder.HasIndex(subscription => subscription.Status).HasDatabaseName("ix_platform_subscriptions_status");
    }
}
