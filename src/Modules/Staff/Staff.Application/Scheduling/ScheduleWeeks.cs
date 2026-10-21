using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Scheduling;

public static class ShiftWarnings
{
    /// <summary>Le membre a une absence validée ce jour-là.</summary>
    public const string TimeOff = "time_off";

    /// <summary>Le membre a demandé une absence ce jour-là, pas encore décidée.</summary>
    public const string PendingTimeOff = "pending_time_off";

    /// <summary>Le membre n'est plus actif ou ne travaille plus dans l'établissement.</summary>
    public const string NotAssigned = "not_assigned";
}

public sealed record ShiftDto(
    Guid Id,
    Guid StaffMemberId,
    string Name,
    DateOnly Date,
    TimeOnly Start,
    TimeOnly End,
    bool EndsNextDay,
    int BreakMinutes,
    string? Position,
    string? Note,
    int PlannedMinutes,
    IReadOnlyList<string> Warnings)
{
    public static ShiftDto From(Shift shift, string name, IReadOnlyList<string>? warnings = null) =>
        new(shift.Id.Value, shift.StaffMemberId.Value, name, shift.Date, shift.Start, shift.End, shift.EndsNextDay, shift.BreakMinutes,
            shift.Position, shift.Note, shift.PlannedMinutes, warnings ?? []);
}

public sealed record ScheduleMemberDto(Guid StaffMemberId, string Name, int PlannedMinutes, bool OverLimit, bool IsAssigned);

public sealed record ScheduleWeekDto(
    Guid? Id,
    Guid LocationId,
    DateOnly WeekStart,
    int PublishedVersion,
    DateTimeOffset? PublishedAtUtc,
    bool HasUnpublishedChanges,
    IReadOnlyList<Guid> ChangedMembers,
    IReadOnlyList<ShiftDto> Shifts,
    IReadOnlyList<ScheduleMemberDto> Members,
    IReadOnlyList<TimeOffDto> Absences,
    int OpenSwaps,
    int WeeklyHoursLimit,
    IReadOnlyList<string> Positions);

public sealed record ShiftInput(Guid StaffMemberId, DateOnly Date, TimeOnly Start, TimeOnly End, int BreakMinutes, string? Position, string? Note);

// --- Réglages ---

public sealed record ScheduleSettingsDto(IReadOnlyList<string> Positions, int WeeklyHoursLimit);

public sealed record GetScheduleSettingsQuery(Guid LocationId) : IQuery<ScheduleSettingsDto>;

public sealed class GetScheduleSettingsHandler(SchedulingDesk desk) : IQueryHandler<GetScheduleSettingsQuery, ScheduleSettingsDto>
{
    public async Task<ScheduleSettingsDto> HandleAsync(GetScheduleSettingsQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = desk.Workspace.CurrentTenant;
        var location = await desk.RequireLocationAsync(tenantId, query.LocationId, cancellationToken);
        var settings = await desk.SettingsAsync(tenantId, location.Id, cancellationToken);
        return new ScheduleSettingsDto(settings.Positions, settings.WeeklyHoursLimit);
    }
}

public sealed record SetScheduleSettingsCommand(Guid LocationId, IReadOnlyList<string?>? Positions, int WeeklyHoursLimit) : ICommand;

public sealed class SetScheduleSettingsHandler(SchedulingDesk desk) : ICommandHandler<SetScheduleSettingsCommand>
{
    public async Task HandleAsync(SetScheduleSettingsCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var location = await desk.RequireLocationAsync(tenantId, command.LocationId, cancellationToken);
        var settings = await desk.Schedules.FindSettingsAsync(tenantId, location.Id, cancellationToken);
        if (settings is null)
        {
            settings = ScheduleSettings.Default(tenantId, location.Id);
            desk.Schedules.AddSettings(settings);
        }

        settings.Update(command.Positions, command.WeeklyHoursLimit);
        await desk.Workspace.SaveAsync(cancellationToken);
    }
}

// --- Semaine (gérant) ---

/// <summary>Le brouillon de la semaine avec ce qu'il faut pour le remplir : membres, absences, alertes.</summary>
public sealed record GetScheduleWeekQuery(Guid LocationId, DateOnly WeekStart) : IQuery<ScheduleWeekDto>;

public sealed class GetScheduleWeekHandler(SchedulingDesk desk, ITimeOffRepository absences, IShiftSwapRepository swaps)
    : IQueryHandler<GetScheduleWeekQuery, ScheduleWeekDto>
{
    public async Task<ScheduleWeekDto> HandleAsync(GetScheduleWeekQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = desk.Workspace.CurrentTenant;
        var location = await desk.RequireLocationAsync(tenantId, query.LocationId, cancellationToken);
        var weekStart = ScheduleWeek.MondayOf(query.WeekStart);
        var week = await desk.Schedules.FindAsync(tenantId, location.Id, weekStart, cancellationToken);
        var settings = await desk.SettingsAsync(tenantId, location.Id, cancellationToken);
        var here = await desk.MembersAtAsync(tenantId, location.Id, cancellationToken);
        var shifts = week?.Shifts ?? [];
        var names = await desk.NamesAsync(tenantId, shifts.Select(shift => shift.StaffMemberId).Concat(here.Select(member => member.Id)), here, cancellationToken);
        var weekAbsences = (await absences.ListAsync(tenantId, weekStart, weekStart.AddDays(6), null, null, 500, cancellationToken))
            .Where(absence => absence.Status is TimeOffStatus.Approved or TimeOffStatus.Pending)
            .ToList();
        var assigned = here.Select(member => member.Id).ToHashSet();

        var limitMinutes = settings.WeeklyHoursLimit * 60;
        var planned = shifts.GroupBy(shift => shift.StaffMemberId).ToDictionary(group => group.Key, group => group.Sum(shift => shift.PlannedMinutes));
        var members = here.Select(member => member.Id)
            .Union(planned.Keys)
            .Select(id => new ScheduleMemberDto(id.Value, names.GetValueOrDefault(id, "Membre"), planned.GetValueOrDefault(id), planned.GetValueOrDefault(id) > limitMinutes, assigned.Contains(id)))
            .OrderBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var openSwaps = week is null
            ? 0
            : (await swaps.ListAsync(tenantId, desk.Clock.UtcNow, null, openOnly: true, 500, cancellationToken)).Count(swap => swap.WeekId == week.Id);

        return new ScheduleWeekDto(
            week?.Id.Value,
            location.Id.Value,
            weekStart,
            week?.PublishedVersion ?? 0,
            week?.PublishedAtUtc,
            week?.HasUnpublishedChanges ?? false,
            week?.ChangedMembers().Select(member => member.Value).ToList() ?? [],
            shifts.OrderBy(shift => shift.StartsAt)
                .Select(shift => ShiftDto.From(shift, names.GetValueOrDefault(shift.StaffMemberId, "Membre"), Warnings(shift, weekAbsences, assigned)))
                .ToList(),
            members,
            weekAbsences.Where(absence => names.ContainsKey(absence.StaffMemberId))
                .Select(absence => TimeOffDto.From(absence, names[absence.StaffMemberId]))
                .ToList(),
            openSwaps,
            settings.WeeklyHoursLimit,
            settings.Positions);
    }

    private static List<string> Warnings(Shift shift, IReadOnlyList<TimeOffRequest> absences, HashSet<StaffMemberId> assigned)
    {
        var warnings = new List<string>();
        var mine = absences.Where(absence => absence.StaffMemberId == shift.StaffMemberId && absence.Covers(shift.Date)).ToList();
        if (mine.Any(absence => absence.Status == TimeOffStatus.Approved))
        {
            warnings.Add(ShiftWarnings.TimeOff);
        }
        else if (mine.Count > 0)
        {
            warnings.Add(ShiftWarnings.PendingTimeOff);
        }

        if (!assigned.Contains(shift.StaffMemberId))
        {
            warnings.Add(ShiftWarnings.NotAssigned);
        }

        return warnings;
    }
}

public sealed record AddShiftCommand(Guid LocationId, DateOnly WeekStart, ShiftInput Shift) : ICommand<Guid>;

public sealed class AddShiftHandler(SchedulingDesk desk) : ICommandHandler<AddShiftCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddShiftCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command.Shift);
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var location = await desk.RequireLocationAsync(tenantId, command.LocationId, cancellationToken);
        var member = await desk.RequirePlannableAsync(tenantId, command.Shift.StaffMemberId, location.Id, cancellationToken);
        var week = await desk.LoadOrCreateWeekAsync(tenantId, location.Id, ScheduleWeek.MondayOf(command.WeekStart), cancellationToken);
        var shift = week.AddShift(Spec(member.Id, command.Shift), desk.Clock);
        await desk.Workspace.SaveAsync(cancellationToken);
        return shift.Id.Value;
    }

    internal static ShiftSpec Spec(StaffMemberId member, ShiftInput input) =>
        new(member, input.Date, input.Start, input.End, input.BreakMinutes, input.Position, input.Note);
}

public sealed record UpdateShiftCommand(Guid LocationId, DateOnly WeekStart, Guid ShiftId, ShiftInput Shift) : ICommand;

public sealed class UpdateShiftHandler(SchedulingDesk desk) : ICommandHandler<UpdateShiftCommand>
{
    public async Task HandleAsync(UpdateShiftCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command.Shift);
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var location = await desk.RequireLocationAsync(tenantId, command.LocationId, cancellationToken);
        var week = await desk.LoadWeekAsync(tenantId, location.Id, ScheduleWeek.MondayOf(command.WeekStart), cancellationToken);
        var shiftId = RemoveShiftHandler.Find(week, command.ShiftId);
        var current = week.Shifts.Single(shift => shift.Id == shiftId);
        var member = current.StaffMemberId.Value == command.Shift.StaffMemberId
            ? current.StaffMemberId
            : (await desk.RequirePlannableAsync(tenantId, command.Shift.StaffMemberId, location.Id, cancellationToken)).Id;
        week.UpdateShift(shiftId, AddShiftHandler.Spec(member, command.Shift), desk.Clock);
        await desk.Workspace.SaveAsync(cancellationToken);
    }
}

public sealed record RemoveShiftCommand(Guid LocationId, DateOnly WeekStart, Guid ShiftId) : ICommand;

public sealed class RemoveShiftHandler(SchedulingDesk desk) : ICommandHandler<RemoveShiftCommand>
{
    public async Task HandleAsync(RemoveShiftCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var location = await desk.RequireLocationAsync(tenantId, command.LocationId, cancellationToken);
        var week = await desk.LoadWeekAsync(tenantId, location.Id, ScheduleWeek.MondayOf(command.WeekStart), cancellationToken);
        week.RemoveShift(Find(week, command.ShiftId), desk.Clock);
        await desk.Workspace.SaveAsync(cancellationToken);
    }

    internal static ShiftId Find(ScheduleWeek week, Guid shiftId) =>
        shiftId != Guid.Empty && week.Shifts.Any(shift => shift.Id.Value == shiftId)
            ? ShiftId.From(shiftId)
            : throw new NotFoundException(StaffApplicationErrors.ShiftNotFound);
}

/// <summary>Reprend le brouillon de la semaine précédente dans une semaine vide ; renvoie le nombre de créneaux copiés.</summary>
public sealed record CopyPreviousWeekCommand(Guid LocationId, DateOnly WeekStart) : ICommand<int>;

public sealed class CopyPreviousWeekHandler(SchedulingDesk desk) : ICommandHandler<CopyPreviousWeekCommand, int>
{
    public async Task<int> HandleAsync(CopyPreviousWeekCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var location = await desk.RequireLocationAsync(tenantId, command.LocationId, cancellationToken);
        var weekStart = ScheduleWeek.MondayOf(command.WeekStart);
        var source = await desk.Schedules.FindAsync(tenantId, location.Id, weekStart.AddDays(-7), cancellationToken)
            ?? throw new DomainException(ScheduleErrors.NothingToCopy);
        var here = (await desk.MembersAtAsync(tenantId, location.Id, cancellationToken)).Select(member => member.Id).ToHashSet();
        var week = await desk.LoadOrCreateWeekAsync(tenantId, location.Id, weekStart, cancellationToken);
        var copied = week.CopyFrom(source, here.Contains, desk.Clock);
        await desk.Workspace.SaveAsync(cancellationToken);
        return copied;
    }
}

/// <summary>Publie la semaine : renvoie le nombre de membres prévenus (ceux dont les créneaux ont changé).</summary>
public sealed record PublishScheduleCommand(Guid LocationId, DateOnly WeekStart) : ICommand<int>;

public sealed class PublishScheduleHandler(SchedulingDesk desk) : ICommandHandler<PublishScheduleCommand, int>
{
    public async Task<int> HandleAsync(PublishScheduleCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var location = await desk.RequireLocationAsync(tenantId, command.LocationId, cancellationToken);
        var week = await desk.LoadWeekAsync(tenantId, location.Id, ScheduleWeek.MondayOf(command.WeekStart), cancellationToken);
        var told = week.Publish(desk.Clock);
        await desk.Workspace.SaveAsync(cancellationToken);
        return told.Count;
    }
}

/// <summary>
/// Pour l'hôte, à la publication : la semaine et le compte de chaque membre à prévenir, avec le nombre de ses
/// créneaux publiés (0 : ses créneaux ont été retirés).
/// </summary>
public sealed record GetSchedulePublicationQuery(Guid WeekId, IReadOnlyList<Guid> StaffMemberIds) : IQuery<SchedulePublicationDto?>;

public sealed record SchedulePublicationDto(Guid LocationId, DateOnly WeekStart, int Version, IReadOnlyList<SchedulePublicationRecipient> Recipients);

public sealed record SchedulePublicationRecipient(Guid StaffMemberId, Guid UserId, int ShiftCount, int PlannedMinutes);

public sealed class GetSchedulePublicationHandler(StaffWorkspace workspace, IScheduleRepository schedules)
    : IQueryHandler<GetSchedulePublicationQuery, SchedulePublicationDto?>
{
    public async Task<SchedulePublicationDto?> HandleAsync(GetSchedulePublicationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query.StaffMemberIds);
        var tenantId = workspace.CurrentTenant;
        if (query.WeekId == Guid.Empty || await schedules.FindByIdAsync(tenantId, ScheduleWeekId.From(query.WeekId), cancellationToken) is not { } week)
        {
            return null;
        }

        var recipients = new List<SchedulePublicationRecipient>();
        foreach (var id in query.StaffMemberIds.Where(id => id != Guid.Empty).Distinct())
        {
            if (await workspace.Members.FindAsync(tenantId, StaffMemberId.From(id), cancellationToken) is { Status: StaffStatus.Active } member)
            {
                var mine = week.PublishedShifts.Where(shift => shift.StaffMemberId == member.Id).ToList();
                recipients.Add(new SchedulePublicationRecipient(id, member.UserId.Value, mine.Count, mine.Sum(shift => shift.PlannedMinutes)));
            }
        }

        return new SchedulePublicationDto(week.LocationId.Value, week.WeekStart, week.PublishedVersion, recipients);
    }
}
