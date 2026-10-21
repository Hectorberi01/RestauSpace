using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Scheduling;

public sealed record MyShiftDto(
    Guid WeekId,
    Guid LocationId,
    string LocationName,
    ShiftDto Shift,
    DateTimeOffset StartsAtUtc,
    IReadOnlyList<string> Colleagues);

public sealed record ColleagueDto(Guid StaffMemberId, string Name);

public sealed record MyLocationDto(Guid LocationId, string Name, IReadOnlyList<ColleagueDto> Colleagues);

public sealed record MyScheduleDto(
    Guid StaffMemberId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<MyShiftDto> Shifts,
    IReadOnlyList<TimeOffDto> TimeOff,
    IReadOnlyList<ShiftSwapDto> Swaps,
    IReadOnlyList<MyLocationDto> Locations);

/// <summary>
/// Le planning PUBLIÉ du membre connecté, de la semaine de <paramref name="From"/> sur <paramref name="Weeks"/> semaines
/// (1 à 8), avec ses absences et ses remplacements. Les collègues listés sont ceux qui travaillent le même jour au même
/// endroit (prénom court seulement).
/// </summary>
public sealed record GetMyScheduleQuery(Guid UserId, DateOnly From, int Weeks) : IQuery<MyScheduleDto>
{
    public const int MaxWeeks = 8;
}

public sealed class GetMyScheduleHandler(SchedulingDesk desk, ITimeOffRepository absences, IShiftSwapRepository swaps)
    : IQueryHandler<GetMyScheduleQuery, MyScheduleDto>
{
    public async Task<MyScheduleDto> HandleAsync(GetMyScheduleQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = desk.Workspace.CurrentTenant;
        var me = await desk.Workspace.LoadSelfAsync(tenantId, query.UserId, cancellationToken);
        var weeks = Math.Clamp(query.Weeks, 1, GetMyScheduleQuery.MaxWeeks);
        var from = ScheduleWeek.MondayOf(query.From);
        var to = from.AddDays((7 * weeks) - 1);

        var found = await desk.Schedules.ListAsync(tenantId, null, from, to, cancellationToken);
        var everyone = found.SelectMany(week => week.PublishedShifts.Select(shift => shift.StaffMemberId)).Append(me.Id);
        var active = await desk.Workspace.Members.ListActiveAsync(tenantId, cancellationToken);
        var names = await desk.NamesAsync(tenantId, everyone.Concat(active.Select(member => member.Id)), active, cancellationToken);

        var locations = new Dictionary<LocationId, ScheduleLocation?>();
        var shifts = new List<MyShiftDto>();
        foreach (var week in found)
        {
            var mine = week.PublishedShifts.Where(shift => shift.StaffMemberId == me.Id).ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            if (!locations.TryGetValue(week.LocationId, out var location))
            {
                location = await desk.FindLocationAsync(tenantId, week.LocationId, cancellationToken);
                locations[week.LocationId] = location;
            }

            var zone = SchedulingDesk.Zone(location?.TimeZoneId ?? "UTC");
            foreach (var shift in mine)
            {
                var colleagues = week.PublishedShifts
                    .Where(other => other.StaffMemberId != me.Id && other.Date == shift.Date)
                    .Select(other => names.GetValueOrDefault(other.StaffMemberId, "Membre"))
                    .Distinct()
                    .Order(StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                shifts.Add(new MyShiftDto(
                    week.Id.Value, week.LocationId.Value, location?.Name ?? string.Empty, ShiftDto.From(shift, names.GetValueOrDefault(me.Id, "Moi")),
                    SchedulingDesk.ToUtc(shift.StartsAt, zone), colleagues));
            }
        }

        var places = new List<MyLocationDto>();
        foreach (var location in locations.Values.OfType<ScheduleLocation>())
        {
            places.Add(new MyLocationDto(
                location.Id.Value,
                location.Name,
                active.Where(member => member.Id != me.Id && member.EffectiveRoleAt(location.Id) is not null)
                    .Select(member => new ColleagueDto(member.Id.Value, names.GetValueOrDefault(member.Id, "Membre")))
                    .OrderBy(colleague => colleague.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()));
        }

        var myAbsences = await absences.ListAsync(tenantId, from.AddDays(-31), to.AddDays(92), me.Id, null, 200, cancellationToken);
        var mySwaps = await swaps.ListAsync(tenantId, desk.Clock.UtcNow.AddDays(-14), me.Id, openOnly: false, 200, cancellationToken);
        var myName = names.GetValueOrDefault(me.Id, "Moi");
        return new MyScheduleDto(
            me.Id.Value,
            from,
            to,
            shifts.OrderBy(shift => shift.StartsAtUtc).ToList(),
            myAbsences.OrderByDescending(absence => absence.FirstDay).Select(absence => TimeOffDto.From(absence, myName)).ToList(),
            await ShiftSwapViews.ToDtosAsync(desk, tenantId, mySwaps.OrderBy(swap => swap.ShiftStartsAtUtc).ToList(), cancellationToken),
            places);
    }
}
