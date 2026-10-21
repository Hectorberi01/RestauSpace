namespace Reporting.Domain;

public static class ReportErrors
{
    public static readonly DomainError CodeInvalid = new("reporting.code.invalid", "Metric and filter field codes must be lowercase dotted codes such as 'sales.net_revenue' (64 characters max).");
    public static readonly DomainError CatalogMetricsEmpty = new("reporting.catalog.metrics_empty", "A report catalog must expose at least one metric.");

    public static readonly DomainError NameEmpty = new("reporting.report_definition.name_empty", "Report name is required.");
    public static readonly DomainError NameTooLong = new("reporting.report_definition.name_too_long", "Report name is limited to 100 characters.");
    public static readonly DomainError MetricsEmpty = new("reporting.report_definition.metrics_empty", "A report needs at least one metric.");
    public static readonly DomainError MetricUnknown = new("reporting.report_definition.metric_unknown", "This metric is not part of the report catalog.");
    public static readonly DomainError TooManyMetrics = new("reporting.report_definition.too_many_metrics", "A report is limited to 30 metrics.");
    public static readonly DomainError FilterFieldNotAllowed = new("reporting.report_definition.filter_field_not_allowed", "This field cannot be used as a report filter.");
    public static readonly DomainError FilterOperatorInvalid = new("reporting.report_definition.filter_operator_invalid", "Filter operator is not supported.");
    public static readonly DomainError FilterValueInvalid = new("reporting.report_definition.filter_value_invalid", "Filter value is required and limited to 200 characters.");
    public static readonly DomainError FilterDuplicated = new("reporting.report_definition.filter_duplicated", "The same filter cannot be declared twice.");
    public static readonly DomainError TooManyFilters = new("reporting.report_definition.too_many_filters", "A report is limited to 20 filters.");
    public static readonly DomainError ScheduleFrequencyInvalid = new("reporting.report_definition.schedule_frequency_invalid", "Schedule frequency is not supported.");
    public static readonly DomainError ScheduleDayOfWeekRequired = new("reporting.report_definition.schedule_day_of_week_required", "A weekly schedule needs the day of the week it runs on.");
    public static readonly DomainError ScheduleDayOfWeekInvalid = new("reporting.report_definition.schedule_day_of_week_invalid", "Schedule day of the week is not a known day.");
    public static readonly DomainError ScheduleDayOfMonthRequired = new("reporting.report_definition.schedule_day_of_month_required", "A monthly schedule needs the day of the month it runs on.");
    public static readonly DomainError ScheduleDayOfMonthInvalid = new("reporting.report_definition.schedule_day_of_month_invalid", "Schedule day of the month must be between 1 and 28.");
    public static readonly DomainError ScheduleAnchorNotAllowed = new("reporting.report_definition.schedule_anchor_not_allowed", "A day of the week is only for a weekly schedule and a day of the month only for a monthly one.");
    public static readonly DomainError ScheduleRecipientsEmpty = new("reporting.report_definition.schedule_recipients_empty", "A schedule needs at least one recipient.");
    public static readonly DomainError ScheduleTooManyRecipients = new("reporting.report_definition.schedule_too_many_recipients", "A schedule is limited to 20 recipients.");
    public static readonly DomainError CatalogIncompatible = new("reporting.report_definition.catalog_incompatible", "The report uses a metric or a filter field that is no longer part of the report catalog.");
    public static readonly DomainError NotScheduled = new("reporting.report_definition.not_scheduled", "The report is not scheduled.");
}
