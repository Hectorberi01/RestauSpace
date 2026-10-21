using System.Globalization;
using Reporting.Domain;

namespace Reporting.Application.Metrics;

/// <summary>Un incrément d'un compteur du jour. Dimension et devise vides quand elles ne s'appliquent pas.</summary>
public sealed record MetricFact(string Metric, string Dimension, string Currency, decimal Value);

/// <summary>Les incréments d'une source (un événement) : un établissement (ou aucun) et un jour LOCAL du restaurant.</summary>
public sealed record DailyFacts(LocationId? Location, DateOnly Day, IReadOnlyList<MetricFact> Facts);

/// <summary>Un compteur stocké. <see cref="LocationId"/> vide : fait du restaurant, sans établissement.</summary>
public sealed record MetricRow(DateOnly Day, Guid LocationId, string Metric, string Dimension, string Currency, decimal Value);

/// <summary>Valeurs retenues (null = toutes) et valeurs écartées d'un champ.</summary>
public sealed record ValueFilter<T>(IReadOnlySet<T>? Include, IReadOnlySet<T> Exclude)
{
    public static ValueFilter<T> All { get; } = new(null, new HashSet<T>());

    public bool Allows(T value) => (Include is null || Include.Contains(value)) && !Exclude.Contains(value);
}

/// <summary>Ce qu'on lit des compteurs : période en jours locaux (bornes comprises), mesures stockées, filtres.</summary>
public sealed record MetricQuery(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string> Metrics,
    ValueFilter<Guid> Locations,
    ValueFilter<string> Currencies,
    ValueFilter<string> Dimensions);

public enum ReportGranularity
{
    Day = 1,

    /// <summary>Semaine ISO, du lundi au dimanche.</summary>
    Week = 2,

    Month = 3,

    /// <summary>Une seule ligne par mesure pour toute la période.</summary>
    Total = 4,
}

/// <summary>
/// Les filtres d'un rapport traduits en critères de lecture. Champs : <c>location.id</c> (uuid), <c>currency</c> (code
/// ISO), <c>dimension</c> (code) ; opérateurs : égal, différent, dans une liste (valeurs séparées par des virgules).
/// Plusieurs critères sur un même champ se cumulent (intersection des inclusions, union des exclusions).
/// </summary>
public static class ReportFilterRules
{
    public const int MaxListValues = 50;

    /// <summary>Lève une erreur stable pour un opérateur non pris en charge ou une valeur mal formée.</summary>
    public static void Validate(IEnumerable<ReportFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        _ = Translate(filters, DateOnly.MinValue, DateOnly.MinValue, []);
    }

    public static MetricQuery Translate(IEnumerable<ReportFilter> filters, DateOnly from, DateOnly to, IReadOnlyList<string> metrics)
    {
        ArgumentNullException.ThrowIfNull(filters);
        HashSet<Guid>? locationsIn = null;
        HashSet<string>? currenciesIn = null;
        HashSet<string>? dimensionsIn = null;
        var locationsOut = new HashSet<Guid>();
        var currenciesOut = new HashSet<string>(StringComparer.Ordinal);
        var dimensionsOut = new HashSet<string>(StringComparer.Ordinal);

        foreach (var filter in filters)
        {
            switch (filter.Field)
            {
                case ReportingMetrics.LocationField:
                    Apply(filter, ParseLocation, ref locationsIn, locationsOut);
                    break;
                case ReportingMetrics.CurrencyField:
                    Apply(filter, ParseCurrency, ref currenciesIn, currenciesOut);
                    break;
                case ReportingMetrics.DimensionField:
                    Apply(filter, ParseDimension, ref dimensionsIn, dimensionsOut);
                    break;
                default:
                    throw new DomainException(ReportErrors.FilterFieldNotAllowed);
            }
        }

        return new MetricQuery(
            from,
            to,
            metrics,
            new ValueFilter<Guid>(locationsIn, locationsOut),
            new ValueFilter<string>(currenciesIn, currenciesOut),
            new ValueFilter<string>(dimensionsIn, dimensionsOut));
    }

    /// <summary>Le code de dimension tel qu'il est stocké : minuscules, 64 caractères au plus (vide = sans dimension).</summary>
    public static string NormalizeDimension(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().ToLowerInvariant();
        DomainException.ThrowIf(trimmed.Length > 64 || trimmed.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')), ReportingApplicationErrors.DimensionInvalid);
        return trimmed;
    }

    /// <summary>Code ISO 4217 en majuscules (vide = sans devise).</summary>
    public static string NormalizeCurrency(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().ToUpperInvariant();
        DomainException.ThrowIf(trimmed.Length != 0 && (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper)), ReportingApplicationErrors.CurrencyInvalid);
        return trimmed;
    }

    private static void Apply<T>(ReportFilter filter, Func<string, T> parse, ref HashSet<T>? include, HashSet<T> exclude)
    {
        switch (filter.Operator)
        {
            case FilterOperator.EqualTo:
            case FilterOperator.In:
                var values = Values(filter).Select(parse).ToHashSet();
                if (include is null)
                {
                    include = values;
                }
                else
                {
                    include.IntersectWith(values);
                }

                break;
            case FilterOperator.NotEqualTo:
                exclude.UnionWith(Values(filter).Select(parse));
                break;
            default:
                throw new DomainException(ReportingApplicationErrors.FilterOperatorNotSupported);
        }
    }

    private static List<string> Values(ReportFilter filter)
    {
        var values = new List<string>();
        if (filter.Operator == FilterOperator.In)
        {
            values.AddRange(filter.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else
        {
            values.Add(filter.Value);
        }

        DomainException.ThrowIf(values.Count is 0 or > MaxListValues, ReportingApplicationErrors.FilterValueInvalid);
        return values;
    }

    private static Guid ParseLocation(string value) =>
        Guid.TryParse(value, CultureInfo.InvariantCulture, out var id) && id != Guid.Empty
            ? id
            : throw new DomainException(ReportingApplicationErrors.FilterValueInvalid);

    private static string ParseCurrency(string value)
    {
        try
        {
            var currency = NormalizeCurrency(value);
            DomainException.ThrowIf(currency.Length == 0, ReportingApplicationErrors.FilterValueInvalid);
            return currency;
        }
        catch (DomainException)
        {
            throw new DomainException(ReportingApplicationErrors.FilterValueInvalid);
        }
    }

    private static string ParseDimension(string value)
    {
        try
        {
            return NormalizeDimension(value);
        }
        catch (DomainException)
        {
            throw new DomainException(ReportingApplicationErrors.FilterValueInvalid);
        }
    }
}
