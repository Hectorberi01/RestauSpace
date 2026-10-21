using Staff.Application.Abstractions;
using Staff.Application.Commands;
using Staff.Domain;

namespace Staff.Application.Scheduling;

public static class VarianceStatus
{
    /// <summary>Le créneau n'a pas encore commencé.</summary>
    public const string Upcoming = "upcoming";

    /// <summary>Le membre est pointé et le créneau n'est pas fini.</summary>
    public const string InProgress = "in_progress";

    public const string OnTime = "on_time";

    public const string Late = "late";

    /// <summary>Créneau passé (ou commencé depuis plus de la tolérance) sans pointage.</summary>
    public const string Missing = "missing";
}

public sealed record VarianceShiftDto(
    Guid ShiftId,
    Guid StaffMemberId,
    string Name,
    DateOnly Date,
    TimeOnly Start,
    TimeOnly End,
    string? Position,
    int PlannedMinutes,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateTimeOffset? ClockedInAtUtc,
    DateTimeOffset? ClockedOutAtUtc,
    int WorkedMinutes,
    int LateMinutes,
    int LeftEarlyMinutes,
    string Status);

/// <param name="DueMinutes">Heures prévues des créneaux déjà commencés : la base de l'écart en cours de semaine.</param>
public sealed record VarianceMemberDto(Guid StaffMemberId, string Name, int PlannedMinutes, int DueMinutes, int WorkedMinutes);

public sealed record UnplannedEntryDto(Guid StaffMemberId, string Name, DateTimeOffset ClockedInAtUtc, DateTimeOffset? ClockedOutAtUtc, int WorkedMinutes);

public sealed record ScheduleVarianceDto(
    DateOnly WeekStart,
    bool IsPublished,
    IReadOnlyList<VarianceShiftDto> Shifts,
    IReadOnlyList<VarianceMemberDto> Members,
    IReadOnlyList<UnplannedEntryDto> Unplanned);

/// <summary>
/// Prévu (dernière version publiée) contre pointé, pour une semaine d'un établissement. Un pointage est rattaché au
/// créneau du même membre qu'il chevauche (à 2 h près) ; les autres sont « hors planning ». Retard et départ anticipé
/// comptent au-delà de <see cref="ToleranceMinutes"/>.
/// </summary>
public sealed record GetScheduleVarianceQuery(Guid LocationId, DateOnly WeekStart) : IQuery<ScheduleVarianceDto>
{
    public const int ToleranceMinutes = 5;
    public static readonly TimeSpan Margin = TimeSpan.FromHours(2);
}

public sealed class GetScheduleVarianceHandler(SchedulingDesk desk, ITimeEntryRepository entries) : IQueryHandler<GetScheduleVarianceQuery, ScheduleVarianceDto>
{
    public async Task<ScheduleVarianceDto> HandleAsync(GetScheduleVarianceQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = desk.Workspace.CurrentTenant;
        var location = await desk.RequireLocationAsync(tenantId, query.LocationId, cancellationToken);
        var weekStart = ScheduleWeek.MondayOf(query.WeekStart);
        var week = await desk.Schedules.FindAsync(tenantId, location.Id, weekStart, cancellationToken);
        var zone = SchedulingDesk.Zone(location.TimeZoneId);
        var weekFrom = SchedulingDesk.ToUtc(weekStart.ToDateTime(TimeOnly.MinValue), zone);
        var weekTo = SchedulingDesk.ToUtc(weekStart.AddDays(7).ToDateTime(TimeOnly.MinValue), zone);
        var now = desk.Clock.UtcNow;

        var shifts = (week?.PublishedShifts ?? []).OrderBy(shift => shift.StartsAt).ToList();
        var found = await entries.ListAsync(tenantId, weekFrom - TimeSpan.FromDays(1), weekTo + TimeSpan.FromDays(1), location.Id, null, ListTimeEntriesQuery.Max, cancellationToken);
        var names = await desk.NamesAsync(tenantId, shifts.Select(shift => shift.StaffMemberId).Concat(found.Select(entry => entry.StaffMemberId)), null, cancellationToken);
        var used = new HashSet<TimeEntryId>();
        var rows = new List<VarianceShiftDto>();

        foreach (var shift in shifts)
        {
            var (start, end) = SchedulingDesk.UtcWindow(shift, zone);
            var matched = found
                .Where(entry => entry.StaffMemberId == shift.StaffMemberId && !used.Contains(entry.Id))
                .Where(entry => entry.ClockedInAtUtc < end + GetScheduleVarianceQuery.Margin && Out(entry, now) > start - GetScheduleVarianceQuery.Margin)
                .OrderBy(entry => entry.ClockedInAtUtc)
                .ToList();
            used.UnionWith(matched.Select(entry => entry.Id));

            var firstIn = matched.Count > 0 ? matched[0].ClockedInAtUtc : (DateTimeOffset?)null;
            var stillOpen = matched.Any(entry => entry.IsOpen && !entry.IsStale(now));
            var lastOut = matched.Count > 0 && !stillOpen ? matched.Max(entry => Out(entry, now)) : (DateTimeOffset?)null;
            var worked = matched.Sum(entry => Minutes(entry, now));
            var late = firstIn is { } arrived ? Over(arrived - start) : 0;
            var leftEarly = lastOut is { } left ? Over(end - left) : 0;
            var status = firstIn is null
                ? now < start + TimeSpan.FromMinutes(GetScheduleVarianceQuery.ToleranceMinutes) ? VarianceStatus.Upcoming : VarianceStatus.Missing
                : stillOpen && now < end ? VarianceStatus.InProgress
                : late > 0 ? VarianceStatus.Late
                : VarianceStatus.OnTime;

            rows.Add(new VarianceShiftDto(
                shift.Id.Value, shift.StaffMemberId.Value, names.GetValueOrDefault(shift.StaffMemberId, "Membre"), shift.Date, shift.Start, shift.End, shift.Position,
                shift.PlannedMinutes, start, end, firstIn, lastOut, worked, late, leftEarly, status));
        }

        var inWeek = found.Where(entry => entry.ClockedInAtUtc >= weekFrom && entry.ClockedInAtUtc < weekTo).ToList();
        var unplanned = inWeek.Where(entry => !used.Contains(entry.Id))
            .OrderBy(entry => entry.ClockedInAtUtc)
            .Select(entry => new UnplannedEntryDto(
                entry.StaffMemberId.Value, names.GetValueOrDefault(entry.StaffMemberId, "Membre"), entry.ClockedInAtUtc, entry.ClockedOutAtUtc, Minutes(entry, now)))
            .ToList();

        var members = shifts.Select(shift => shift.StaffMemberId)
            .Union(inWeek.Select(entry => entry.StaffMemberId))
            .Select(id => new VarianceMemberDto(
                id.Value,
                names.GetValueOrDefault(id, "Membre"),
                shifts.Where(shift => shift.StaffMemberId == id).Sum(shift => shift.PlannedMinutes),
                rows.Where(row => row.StaffMemberId == id.Value && row.StartsAtUtc <= now).Sum(row => row.PlannedMinutes),
                inWeek.Where(entry => entry.StaffMemberId == id).Sum(entry => Minutes(entry, now))))
            .OrderBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new ScheduleVarianceDto(weekStart, week?.IsPublished ?? false, rows, members, unplanned);
    }

    private static DateTimeOffset Out(TimeEntry entry, DateTimeOffset now) =>
        entry.ClockedOutAtUtc ?? (entry.IsStale(now) ? entry.ClockedInAtUtc + TimeEntry.MaxShift : now);

    private static int Minutes(TimeEntry entry, DateTimeOffset now) => (int)Math.Round((Out(entry, now) - entry.ClockedInAtUtc).TotalMinutes);

    private static int Over(TimeSpan gap) => gap.TotalMinutes > GetScheduleVarianceQuery.ToleranceMinutes ? (int)Math.Round(gap.TotalMinutes) : 0;
}
