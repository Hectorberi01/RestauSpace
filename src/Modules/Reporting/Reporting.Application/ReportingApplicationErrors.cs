namespace Reporting.Application;

/// <summary>Erreurs des cas d'usage de Reporting (les erreurs des règles métier sont dans Reporting.Domain).</summary>
public static class ReportingApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("reporting.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    public static readonly DomainError TenantNotWritable =
        new("reporting.tenant.not_writable", "The restaurant account is suspended or closed: ask the platform to restore it.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError DefinitionNotFound = new("reporting.report_definition.not_found", "The report does not exist.");
    public static readonly DomainError AccountingPlanInvalid =
        new("reporting.accounting_plan.invalid", "Account and journal codes must be 1 to 20 letters or digits.");

    public static readonly DomainError ExportNotFound = new("reporting.export_job.not_found", "The export does not exist.");
    public static readonly DomainError ExportNotDownloadable =
        new("reporting.export_job.not_downloadable", "The export is not ready, has failed or has expired.");

    public static readonly DomainError UserRequired = new("reporting.user_required", "This action needs a signed-in user or an API key.");
    public static readonly DomainError MetricUnknown = new("reporting.metric.unknown", "This metric is not part of the report catalog.");
    public static readonly DomainError MetricNotStored = new("reporting.metric.not_stored", "A derived metric cannot be recorded; record its inputs.");
    public static readonly DomainError FactsEmpty = new("reporting.facts.empty", "At least one fact is required.");
    public static readonly DomainError SourceKeyInvalid = new("reporting.facts.source_key_invalid", "The source key is required and limited to 120 characters.");
    public static readonly DomainError DimensionInvalid = new("reporting.facts.dimension_invalid", "A dimension is a code of at most 64 characters (a-z, 0-9, '_', '-', '.').");
    public static readonly DomainError CurrencyInvalid = new("reporting.facts.currency_invalid", "A currency is a 3-letter ISO 4217 code.");
    public static readonly DomainError FilterOperatorNotSupported =
        new("reporting.filter.operator_not_supported", "Report filters support equal to, not equal to and in (comma separated) only.");
    public static readonly DomainError FilterValueInvalid =
        new("reporting.filter.value_invalid", "The filter value does not fit its field (uuid for location.id, ISO code for currency, 1 to 50 values).");
    public static readonly DomainError PeriodInvalid =
        new("reporting.period.invalid", "The period must start before it ends and cover at most 366 days.");
    public static readonly DomainError GranularityInvalid = new("reporting.granularity.invalid", "Unknown granularity.");
    public static readonly DomainError TimeOfDayInvalid = new("reporting.schedule.time_of_day_invalid", "The time of day must be written HH:mm.");
    public static readonly DomainError PageInvalid = new("reporting.page_invalid", "Skip must be zero or more and take between 1 and 100.");
}

/// <summary>
/// Codes de permission déclarés par le module Reporting pour les restaurants (accordés via les rôles Staff). Les chiffres
/// de la plateforme relèvent de la policy d'administration.
/// </summary>
public static class ReportingPermissions
{
    /// <summary>Tableau de bord, exécution des rapports, lecture des définitions et téléchargement des exports.</summary>
    public const string View = "reporting.view";

    /// <summary>Créer, modifier et planifier des rapports.</summary>
    public const string Manage = "reporting.manage";

    /// <summary>Demander un export.</summary>
    public const string Export = "reporting.export";

    public static IReadOnlyList<string> All { get; } = [View, Manage, Export];
}

/// <summary>Réglages des cas d'usage (section <c>Reporting</c> de l'hôte).</summary>
public sealed class ReportingSettings
{
    /// <summary>Durée pendant laquelle un export terminé reste téléchargeable.</summary>
    public TimeSpan ExportRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Période maximale d'un rapport ou d'un export.</summary>
    public int MaxPeriodDays { get; set; } = 366;
}
