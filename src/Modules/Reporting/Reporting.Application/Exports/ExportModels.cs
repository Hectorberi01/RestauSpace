using System.Globalization;
using System.Text;
using Reporting.Application.Metrics;
using Reporting.Domain;

namespace Reporting.Application.Exports;

/// <summary>Un critère tel qu'il est enregistré avec un export (le domaine le revalide à la relecture).</summary>
public sealed record ExportFilter(string Field, FilterOperator Operator, string Value)
{
    public static ExportFilter From(ReportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new(filter.Field, filter.Operator, filter.Value);
    }

    public ReportFilter ToFilter() => ReportFilter.Create(Field, Operator, Value);
}

/// <summary>
/// Les paramètres d'un export, figés à la demande : le worker produit exactement ce qui a été demandé, même si la
/// définition change entre-temps. <see cref="Recipients"/> : destinataires d'un envoi planifié (vide sinon).
/// </summary>
public sealed record ExportRequest(
    string Title,
    DateOnly From,
    DateOnly To,
    ReportGranularity Granularity,
    bool ByLocation,
    IReadOnlyList<string> Metrics,
    IReadOnlyList<ExportFilter> Filters,
    IReadOnlyList<string> Recipients,
    AccountingPlan? Accounting = null)
{
    public bool IsScheduledDelivery => Recipients.Count > 0;
}

/// <summary>Un fichier d'export relu depuis le stockage.</summary>
public sealed record StoredExportFile(FileId FileId, string FileName, string ContentType, byte[] Content);

/// <summary>
/// Le fichier CSV d'un rapport (UTF-8 avec BOM pour les tableurs, séparateur virgule, culture invariante). Toute cellule
/// texte commençant par un caractère de formule est préfixée d'une apostrophe (injection de formule dans un tableur).
/// </summary>
public static class CsvReportWriter
{
    public const string ContentType = "text/csv";
    public const string ExportTypeCode = "report.csv";

    private static readonly string[] Header = ["period_start", "period_end", "location_id", "metric", "dimension", "currency", "value"];

    public static byte[] Write(IEnumerable<ReportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var builder = new StringBuilder();
        builder.Append(string.Join(',', Header)).Append("\r\n");
        foreach (var row in rows)
        {
            builder
                .Append(row.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.LocationId?.ToString() ?? string.Empty).Append(',')
                .Append(Cell(row.Metric)).Append(',')
                .Append(Cell(row.Dimension)).Append(',')
                .Append(Cell(row.Currency)).Append(',')
                .Append(row.Value.ToString("0.####", CultureInfo.InvariantCulture)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    /// <summary>Nom du fichier : titre réduit à [a-z0-9-], puis la période.</summary>
    public static string FileName(string title, DateOnly from, DateOnly to)
    {
        var slug = new string((title ?? string.Empty).ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        if (slug.Length == 0)
        {
            slug = "report";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{slug[..Math.Min(slug.Length, 60)]}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

    private static string Cell(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
        return safe.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }
}
