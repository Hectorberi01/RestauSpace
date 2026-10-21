using System.Globalization;
using System.Text.Json;
using Locations.Domain.Locations;

namespace Locations.Infrastructure.Persistence;

/// <summary>
/// Formes jsonb des value objects sans colonnes naturelles (adresse, horaires hebdomadaires, plages d'exception).
/// Relues via les fabriques du domaine : une valeur stockée qui viole une règle échoue bruyamment au lieu de se charger.
/// </summary>
internal static class LocationJson
{
    private const string TimeFormat = "HH:mm:ss";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write(Address address) => JsonSerializer.Serialize(
        new AddressRow(address.Street, address.City, address.PostalCode, address.CountryCode.Value, address.Latitude, address.Longitude),
        Options);

    public static Address ReadAddress(string json)
    {
        var row = JsonSerializer.Deserialize<AddressRow>(json, Options) ?? throw new InvalidOperationException("Stored address is empty.");
        return Address.Create(row.Street, row.City, row.PostalCode, row.CountryCode, row.Latitude, row.Longitude);
    }

    public static string Write(LocationContact contact) => JsonSerializer.Serialize(
        new ContactRow(contact.Phone?.Value, contact.WhatsApp?.Value, contact.Email?.Value), Options);

    public static LocationContact ReadContact(string json)
    {
        var row = JsonSerializer.Deserialize<ContactRow>(json, Options);
        return row is null ? LocationContact.None : LocationContact.Create(row.Phone, row.WhatsApp, row.Email);
    }

    public static string Write(BusinessHours hours) => JsonSerializer.Serialize(
        hours.WeeklySchedule
            .OrderBy(day => day.Key)
            .SelectMany(day => day.Value.Select(range => new SlotRow(day.Key, Format(range.Start), Format(range.End), range.EndsNextDay)))
            .ToList(),
        Options);

    public static BusinessHours ReadHours(string json)
    {
        var rows = JsonSerializer.Deserialize<List<SlotRow>>(json, Options) ?? new();
        return BusinessHours.Create(rows.Select(row => new WeeklySlot(row.Day, ToRange(row.Start, row.End, row.EndsNextDay))));
    }

    public static string Write(List<TimeRange> ranges) => JsonSerializer.Serialize(
        ranges.Select(range => new RangeRow(Format(range.Start), Format(range.End), range.EndsNextDay)).ToList(),
        Options);

    public static List<TimeRange> ReadRanges(string json)
    {
        var rows = JsonSerializer.Deserialize<List<RangeRow>>(json, Options) ?? new();
        return rows.Select(row => ToRange(row.Start, row.End, row.EndsNextDay)).ToList();
    }

    private static string Format(TimeOnly time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);

    private static TimeRange ToRange(string start, string end, bool endsNextDay)
    {
        var from = TimeOnly.ParseExact(start, TimeFormat, CultureInfo.InvariantCulture);
        var to = TimeOnly.ParseExact(end, TimeFormat, CultureInfo.InvariantCulture);
        return endsNextDay ? TimeRange.Overnight(from, to) : TimeRange.SameDay(from, to);
    }

    internal sealed record AddressRow(
        string Street, string City, string? PostalCode, string CountryCode, decimal? Latitude, decimal? Longitude);

    internal sealed record ContactRow(string? Phone, string? WhatsApp, string? Email);

    internal sealed record SlotRow(DayOfWeek Day, string Start, string End, bool EndsNextDay);

    internal sealed record RangeRow(string Start, string End, bool EndsNextDay);
}
