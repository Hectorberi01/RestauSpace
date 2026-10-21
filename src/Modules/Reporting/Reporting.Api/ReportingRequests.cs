using Reporting.Application.Commands;
using Reporting.Application.Exports;
using Reporting.Application.Metrics;
using Reporting.Domain;

namespace Reporting.Api;

public sealed record CreatedResponse(Guid Id);

public sealed record RunReportRequest(
    IReadOnlyList<string>? Metrics,
    IReadOnlyList<ReportFilterInput>? Filters,
    DateOnly From,
    DateOnly To,
    ReportGranularity Granularity = ReportGranularity.Day,
    bool ByLocation = false);

public sealed record CreateReportDefinitionRequest(string? Name, IReadOnlyList<string>? Metrics, IReadOnlyList<ReportFilterInput>? Filters = null);

public sealed record RenameReportRequest(string? Name);

public sealed record ReportFiltersRequest(IReadOnlyList<ReportFilterInput>? Filters);

public sealed record ReportPeriodRequest(DateOnly From, DateOnly To, ReportGranularity Granularity = ReportGranularity.Day, bool ByLocation = false);

/// <param name="LocalTime">Heure locale du restaurant, « HH:mm ».</param>
public sealed record ScheduleReportRequest(
    ReportFrequency Frequency, DayOfWeek? DayOfWeek, int? DayOfMonth, string? LocalTime, IReadOnlyList<string>? Recipients);

/// <summary>Export d'un rapport enregistré (<see cref="DefinitionId"/>) ou ponctuel (mesures et filtres).</summary>
public sealed record RequestExportRequest(
    Guid? DefinitionId,
    IReadOnlyList<string>? Metrics,
    IReadOnlyList<ReportFilterInput>? Filters,
    DateOnly From,
    DateOnly To,
    ReportGranularity Granularity = ReportGranularity.Day,
    bool ByLocation = false,
    ExportFormat? Format = null);

/// <param name="Plan">Facultatif : le plan de comptes (SYSCOHADA par défaut).</param>
public sealed record AccountingExportRequest(DateOnly From, DateOnly To, AccountingPlan? Plan = null);
