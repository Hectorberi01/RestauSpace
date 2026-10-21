using Locations.Application;

namespace Locations.Api;

public sealed record CreateLocationRequest(
    string? Name,
    AddressInput? Address,
    int Seats,
    string? TimeZoneId = null,
    IReadOnlyList<WeeklySlotInput>? WeeklyHours = null);

public sealed record LocationCreatedResponse(Guid Id);

public sealed record RenameLocationRequest(string? Name);

public sealed record SetCapacityRequest(int Seats);

public sealed record ChangeTimeZoneRequest(string? TimeZoneId);

/// <summary>Remplace toute la semaine : un jour omis est fermé.</summary>
public sealed record SetBusinessHoursRequest(IReadOnlyList<WeeklySlotInput>? WeeklyHours);

public sealed record TemporarilyCloseRequest(string? Reason = null);

public sealed record ExceptionalClosureRequest(DateOnly Date, string? Reason = null);

public sealed record ExceptionalOpeningRequest(DateOnly Date, IReadOnlyList<TimeRangeInput>? Ranges, string? Reason = null);
