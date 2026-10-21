using System.Text.RegularExpressions;

namespace SaasRestaurant.BuildingBlocks.Domain;

/// <summary>
/// Identifiant de fuseau horaire IANA (ex. "Africa/Porto-Novo", "Europe/Paris", "UTC"), vérifié contre la base
/// des fuseaux du runtime (BCL uniquement). Les ids Windows comme "Romance Standard Time" sont refusés.
/// </summary>
public sealed partial class TimeZoneId : ValueObject
{
    public const int MaxLength = 64;

    private TimeZoneId(string value) => Value = value;

    public string Value { get; }

    private static readonly Lazy<HashSet<string>> KnownIds = new(() =>
    {
        var ids = TimeZoneInfo.GetSystemTimeZones()
            .Where(zone => zone.HasIanaId)
            .Select(zone => zone.Id)
            .ToHashSet(StringComparer.Ordinal);
        ids.Add("UTC");
        return ids;
    });

    public static TimeZoneId Utc { get; } = new("UTC");

    public static TimeZoneId Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), CommonErrors.TimeZoneEmpty);

        var trimmed = value!.Trim();
        // Correspondance exacte, sensible à la casse, avec les ids canoniques : l'API de recherche ignore la casse sur
        // certains hôtes et laisserait passer "europe/paris", qui échoue ensuite sur un hôte sensible à la casse.
        DomainException.ThrowIf(
            trimmed.Length > MaxLength
            || !IanaShape().IsMatch(trimmed)
            || !KnownIds.Value.Contains(trimmed),
            CommonErrors.TimeZoneUnknown);

        return new TimeZoneId(trimmed);
    }

    /// <summary>Date et heure locales dans ce fuseau pour un instant UTC (heure d'été comprise).</summary>
    public DateTimeOffset ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(Value));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    // "Area/Location[/Sub]" ou un jeton unique comme "UTC" : pas d'espaces, ce qui écarte aussi les ids d'affichage Windows.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_+\-]*(/[A-Za-z0-9_+\-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IanaShape();
}
