using Reporting.Application.Exports;
using Reporting.Application.Metrics;
using Reporting.Application.Platform;
using Reporting.Application.Scheduling;
using Reporting.Domain;

namespace Reporting.Application.Queries;

public sealed record MetricDto(string Code, string Source, bool Monetary, bool Derived);

/// <summary>Ce qu'un rapport peut mesurer et filtrer.</summary>
public sealed record ReportCatalogDto(IReadOnlyList<MetricDto> Metrics, IReadOnlyList<string> FilterFields, IReadOnlyList<FilterOperator> Operators);

public sealed record ReportRowDto(
    DateOnly PeriodStart, DateOnly PeriodEnd, Guid? LocationId, string Metric, string Dimension, string Currency, decimal Value)
{
    public static ReportRowDto From(ReportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new(row.PeriodStart, row.PeriodEnd, row.LocationId, row.Metric, row.Dimension, row.Currency, row.Value);
    }
}

public sealed record ReportResultDto(DateOnly From, DateOnly To, ReportGranularity Granularity, IReadOnlyList<ReportRowDto> Rows);

/// <summary>Le tableau de bord du restaurant : indicateurs de la période et chiffre d'affaires jour par jour.</summary>
public sealed record DashboardDto(DateOnly From, DateOnly To, IReadOnlyList<ReportRowDto> Totals, IReadOnlyList<ReportRowDto> RevenueByDay);

public sealed record ReportFilterDto(string Field, FilterOperator Operator, string Value);

public sealed record ReportScheduleDto(
    ReportFrequency Frequency,
    DayOfWeek? DayOfWeek,
    int? DayOfMonth,
    string LocalTime,
    string TimeZone,
    IReadOnlyList<string> Recipients,
    DateTimeOffset? NextRunAtUtc,
    DateTimeOffset? LastRunAtUtc);

public sealed record ReportDefinitionDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Metrics,
    IReadOnlyList<ReportFilterDto> Filters,
    ReportScheduleDto? Schedule,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static ReportDefinitionDto From(ReportDefinition definition, ReportDelivery? delivery)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var schedule = definition.Schedule is { } planned
            ? new ReportScheduleDto(
                planned.Frequency,
                planned.DayOfWeek,
                planned.DayOfMonth,
                planned.LocalTimeOfDay.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                planned.TimeZone.Value,
                planned.Recipients.Select(recipient => recipient.Value).ToList(),
                delivery?.NextRunAtUtc,
                delivery?.LastRunAtUtc)
            : null;
        return new(
            definition.Id.Value,
            definition.Name,
            definition.Metrics.Codes.ToList(),
            definition.Filters.Items.Select(filter => new ReportFilterDto(filter.Field, filter.Operator, filter.Value)).ToList(),
            schedule,
            definition.CreatedAtUtc,
            definition.UpdatedAtUtc);
    }
}

/// <summary>Un export : son état et ce qu'il couvre. <see cref="Downloadable"/> : le fichier peut être téléchargé.</summary>
public sealed record ExportJobDto(
    Guid Id,
    ExportJobStatus Status,
    string Type,
    Guid? DefinitionId,
    string? Title,
    DateOnly? From,
    DateOnly? To,
    bool ScheduledDelivery,
    bool Downloadable,
    string? FailureReason,
    DateTimeOffset QueuedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ExpiredAtUtc)
{
    public static ExportJobDto Create(ExportJob job, ExportRequest? request)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new(
            job.Id.Value,
            job.Status,
            job.Type.Value,
            job.ReportDefinitionId?.Value,
            request?.Title,
            request?.From,
            request?.To,
            request?.IsScheduledDelivery ?? false,
            job.IsDownloadable,
            job.FailureReason,
            job.QueuedAtUtc,
            job.CompletedAtUtc,
            job.ExpiredAtUtc);
    }
}

public sealed record ExportFileDto(string FileName, string ContentType, byte[] Content);

/// <summary>Pour l'hôte : à qui annoncer un export terminé (destinataires d'un envoi planifié, sinon le demandeur).</summary>
public sealed record ExportDeliveryDto(
    Guid ExportJobId, string Title, DateOnly From, DateOnly To, Guid? RequestedBy, IReadOnlyList<string> Recipients);

public sealed record PlatformDashboardDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<StatusCount<PlatformTenantStatus>> TenantsByStatus,
    int NewTenants,
    IReadOnlyList<StatusCount<PlatformSubscriptionStatus>> SubscriptionsByStatus,
    IReadOnlyList<AmountByCurrency> MonthlyRecurringRevenue,
    IReadOnlyList<ReportRowDto> Activity);
