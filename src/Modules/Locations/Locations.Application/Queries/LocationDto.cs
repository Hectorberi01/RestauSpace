using Locations.Domain.Locations;

namespace Locations.Application.Queries;

public sealed record LocationDto(
    Guid Id,
    string Name,
    LocationStatus Status,
    bool IsOperational,
    AddressDto Address,
    ContactDto Contact,
    int Seats,
    string TimeZoneId,
    IReadOnlyList<WeeklySlotDto> WeeklyHours,
    IReadOnlyList<ExceptionalScheduleDto> ExceptionalSchedules,
    string? TemporaryClosureReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ArchivedAtUtc)
{
    public static LocationDto From(RestaurantLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return new LocationDto(
            location.Id.Value,
            location.Name.Value,
            location.Status,
            location.IsOperational,
            AddressDto.From(location.Address),
            ContactDto.From(location.Contact),
            location.Capacity.Seats,
            location.TimeZone.Value,
            location.Hours.WeeklySchedule
                .OrderBy(day => day.Key)
                .SelectMany(day => day.Value.Select(range => WeeklySlotDto.From(day.Key, range)))
                .ToList(),
            location.ExceptionalSchedules
                .OrderBy(schedule => schedule.Date)
                .Select(ExceptionalScheduleDto.From)
                .ToList(),
            location.TemporaryClosureReason,
            location.CreatedAtUtc,
            location.ArchivedAtUtc);
    }
}

public sealed record AddressDto(
    string Street, string City, string? PostalCode, string CountryCode, decimal? Latitude, decimal? Longitude)
{
    public static AddressDto From(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return new AddressDto(
            address.Street, address.City, address.PostalCode, address.CountryCode.Value, address.Latitude, address.Longitude);
    }
}

public sealed record ContactDto(string? Phone, string? WhatsApp, string? Email)
{
    public static ContactDto From(LocationContact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return new ContactDto(contact.Phone?.Value, contact.WhatsApp?.Value, contact.Email?.Value);
    }

    public override string ToString() => "ContactDto(***)";
}

/// <summary>Les heures sont des heures locales de l'établissement, au format "HH:mm".</summary>
public sealed record TimeRangeDto(string Start, string End, bool EndsNextDay)
{
    public static TimeRangeDto From(TimeRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return new TimeRangeDto(LocationInputs.Format(range.Start), LocationInputs.Format(range.End), range.EndsNextDay);
    }
}

public sealed record WeeklySlotDto(DayOfWeek Day, string Start, string End, bool EndsNextDay)
{
    public static WeeklySlotDto From(DayOfWeek day, TimeRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return new WeeklySlotDto(day, LocationInputs.Format(range.Start), LocationInputs.Format(range.End), range.EndsNextDay);
    }
}

public sealed record ExceptionalScheduleDto(DateOnly Date, bool IsClosed, IReadOnlyList<TimeRangeDto> Ranges, string? Reason)
{
    public static ExceptionalScheduleDto From(ExceptionalSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return new ExceptionalScheduleDto(
            schedule.Date, schedule.IsClosed, schedule.Ranges.Select(TimeRangeDto.From).ToList(), schedule.Reason);
    }
}

public sealed record LocationSummaryDto(Guid Id, string Name, LocationStatus Status, string City, string CountryCode, int Seats)
{
    public static LocationSummaryDto From(RestaurantLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return new LocationSummaryDto(
            location.Id.Value,
            location.Name.Value,
            location.Status,
            location.Address.City,
            location.Address.CountryCode.Value,
            location.Capacity.Seats);
    }
}

