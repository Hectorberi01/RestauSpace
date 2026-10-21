using System.Globalization;
using Locations.Domain.Locations;

namespace Locations.Application;

public sealed record AddressInput(
    string? Street, string? City, string? PostalCode, string? CountryCode, decimal? Latitude = null, decimal? Longitude = null);

/// <summary>Coordonnées publiques ; une valeur vide efface le champ. Téléphones au format international (+229…).</summary>
public sealed record ContactInput(string? Phone, string? WhatsApp, string? Email)
{
    public override string ToString() => "ContactInput(***)";
}

/// <summary>Plage en heure locale, "HH:mm". <paramref name="EndsNextDay"/> : de nuit, finit le lendemain à End.</summary>
public sealed record TimeRangeInput(string? Start, string? End, bool EndsNextDay = false);

public sealed record WeeklySlotInput(DayOfWeek Day, string? Start, string? End, bool EndsNextDay = false);

/// <summary>Convertit les valeurs de requête en value objects. Les règles métier restent dans les fabriques du domaine.</summary>
public static class LocationInputs
{
    public const string TimeFormat = "HH:mm";

    public static Address ToAddress(AddressInput? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Address.Create(input.Street, input.City, input.PostalCode, input.CountryCode, input.Latitude, input.Longitude);
    }

    public static LocationContact ToContact(ContactInput? input) =>
        input is null ? LocationContact.None : LocationContact.Create(input.Phone, input.WhatsApp, input.Email);

    public static TimeRange ToRange(TimeRangeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ToRange(input.Start, input.End, input.EndsNextDay);
    }

    public static IReadOnlyList<TimeRange> ToRanges(IEnumerable<TimeRangeInput>? inputs) =>
        (inputs ?? Enumerable.Empty<TimeRangeInput>()).Select(input => ToRange(input)).ToList();

    public static BusinessHours ToHours(IEnumerable<WeeklySlotInput>? slots) =>
        BusinessHours.Create((slots ?? Enumerable.Empty<WeeklySlotInput>()).Select(slot => new WeeklySlot(slot.Day, ToRange(slot.Start, slot.End, slot.EndsNextDay))));

    public static string Format(TimeOnly time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);

    private static TimeRange ToRange(string? start, string? end, bool endsNextDay)
    {
        var from = ParseTime(start);
        var to = ParseTime(end);
        return endsNextDay ? TimeRange.Overnight(from, to) : TimeRange.SameDay(from, to);
    }

    private static TimeOnly ParseTime(string? value)
    {
        if (!TimeOnly.TryParseExact(value?.Trim(), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            throw new DomainException(LocationsApplicationErrors.TimeInvalid);
        }

        return time;
    }
}
