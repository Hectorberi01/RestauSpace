namespace Reporting.Domain;

public sealed record ReportScheduled(
    TenantId TenantId,
    ReportDefinitionId ReportDefinitionId,
    ReportFrequency Frequency,
    DayOfWeek? DayOfWeek,
    int? DayOfMonth,
    TimeOnly LocalTimeOfDay,
    TimeZoneId TimeZone,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReportUnscheduled(
    TenantId TenantId,
    ReportDefinitionId ReportDefinitionId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
