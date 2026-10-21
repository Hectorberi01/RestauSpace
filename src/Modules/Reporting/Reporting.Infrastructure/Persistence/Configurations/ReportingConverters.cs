using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Reporting.Application.Exports;
using Reporting.Domain;

namespace Reporting.Infrastructure.Persistence.Configurations;

/// <summary>
/// Conversions entre les types du domaine et les colonnes. Les objets-valeurs d'une définition (mesures, filtres,
/// planning) sont stockés en JSON et relus par les fabriques du domaine. À la relecture, le catalogue est celui des
/// valeurs stockées elles-mêmes : une définition dont une mesure a quitté le catalogue se recharge quand même, et
/// <c>EnsureCompatibleWith</c> décide ensuite (le domaine le prévoit).
/// </summary>
internal static class ReportingConverters
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<ReportDefinitionId, Guid> DefinitionKey = new(id => id.Value, value => ReportDefinitionId.From(value));
    public static readonly ValueConverter<ReportDefinitionId?, Guid?> OptionalDefinitionKey =
        new(id => id.HasValue ? (Guid?)id.Value.Value : null, value => value.HasValue ? (ReportDefinitionId?)ReportDefinitionId.From(value.Value) : null);
    public static readonly ValueConverter<ExportJobId, Guid> ExportKey = new(id => id.Value, value => ExportJobId.From(value));
    public static readonly ValueConverter<FileId?, Guid?> OptionalFile =
        new(id => id.HasValue ? (Guid?)id.Value.Value : null, value => value.HasValue ? (FileId?)FileId.From(value.Value) : null);
    public static readonly ValueConverter<ExportType, string> ExportTypeCode = new(type => type.Value, value => ExportType.Create(value));
    public static readonly ValueConverter<MetricSet, string> Metrics = new(set => WriteMetrics(set), json => ReadMetrics(json));
    public static readonly ValueConverter<ReportFilters, string> Filters = new(filters => WriteFilters(filters), json => ReadFilters(json));
    public static readonly ValueConverter<ReportSchedule?, string?> Schedule = new(schedule => WriteSchedule(schedule), json => ReadSchedule(json));

    public static string WriteMetrics(MetricSet set) => JsonSerializer.Serialize(set.Codes, Json);

    public static MetricSet ReadMetrics(string json)
    {
        var codes = JsonSerializer.Deserialize<List<string>>(json, Json) ?? new List<string>();
        return MetricSet.Create(codes, ReportCatalog.Create(codes, Array.Empty<string>()));
    }

    public static string WriteFilters(ReportFilters filters) =>
        JsonSerializer.Serialize(filters.Items.Select(filter => new FilterJson(filter.Field, filter.Operator, filter.Value)), Json);

    public static ReportFilters ReadFilters(string json)
    {
        var items = (JsonSerializer.Deserialize<List<FilterJson>>(json, Json) ?? new List<FilterJson>())
            .Select(item => ReportFilter.Create(item.Field, item.Operator, item.Value))
            .ToList();
        return ReportFilters.Create(items, ReportCatalog.Create(["stored"], items.Select(item => item.Field)));
    }

    public static string? WriteSchedule(ReportSchedule? schedule) =>
        schedule is null
            ? null
            : JsonSerializer.Serialize(
                new ScheduleJson(
                    schedule.Frequency,
                    schedule.DayOfWeek,
                    schedule.DayOfMonth,
                    schedule.LocalTimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture),
                    schedule.TimeZone.Value,
                    schedule.Recipients.Select(recipient => recipient.Value).ToList()),
                Json);

    public static ReportSchedule? ReadSchedule(string? json)
    {
        if (json is null)
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<ScheduleJson>(json, Json)!;
        return ReportSchedule.Create(
            stored.Frequency,
            stored.DayOfWeek,
            stored.DayOfMonth,
            TimeOnly.ParseExact(stored.LocalTime, "HH:mm", CultureInfo.InvariantCulture),
            TimeZoneId.Create(stored.TimeZone),
            stored.Recipients.Select(Email.Create));
    }

    public static string WriteRequest(ExportRequest request) => JsonSerializer.Serialize(request, Json);

    public static ExportRequest ReadRequest(string json) => JsonSerializer.Deserialize<ExportRequest>(json, Json)!;

    private sealed record FilterJson(string Field, FilterOperator Operator, string Value);

    private sealed record ScheduleJson(
        ReportFrequency Frequency, DayOfWeek? DayOfWeek, int? DayOfMonth, string LocalTime, string TimeZone, List<string> Recipients);
}

/// <summary>
/// Contraintes CHECK du schéma : garde-fous en base DERRIÈRE les règles du domaine, pour une ligne écrite hors de
/// l'application (script, correction manuelle) ou par un bug de mapping.
/// </summary>
internal static class Checks
{
    /// <summary>Liste GÉNÉRÉE depuis l'enum : ajouter une valeur change le modèle, donc la migration.</summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IN (" + string.Join(", ", Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))) + ")";

    /// <summary>Code ISO 4217 : trois lettres majuscules (ou vide quand la mesure n'a pas de devise).</summary>
    public static string CurrencyOrEmpty(string column) => column + " ~ '^([A-Z]{3})?$'";
}
