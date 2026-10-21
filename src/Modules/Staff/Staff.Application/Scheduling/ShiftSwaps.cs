using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Scheduling;

public sealed record ShiftSwapDto(
    Guid Id,
    Guid LocationId,
    string? LocationName,
    Guid WeekId,
    Guid ShiftId,
    DateOnly? Date,
    TimeOnly? Start,
    TimeOnly? End,
    string? Position,
    DateTimeOffset ShiftStartsAtUtc,
    Guid FromMemberId,
    string FromName,
    Guid ToMemberId,
    string ToName,
    string? Note,
    ShiftSwapStatus Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? DecidedAtUtc);

internal static class ShiftSwapViews
{
    public static async Task<IReadOnlyList<ShiftSwapDto>> ToDtosAsync(
        SchedulingDesk desk, TenantId tenantId, IReadOnlyList<ShiftSwapRequest> swaps, CancellationToken cancellationToken)
    {
        var names = await desk.NamesAsync(tenantId, swaps.SelectMany(swap => new[] { swap.FromMemberId, swap.ToMemberId }), null, cancellationToken);
        var weeks = new Dictionary<ScheduleWeekId, ScheduleWeek?>();
        var locations = new Dictionary<LocationId, string?>();
        var result = new List<ShiftSwapDto>();
        foreach (var swap in swaps)
        {
            if (!weeks.TryGetValue(swap.WeekId, out var week))
            {
                week = await desk.Schedules.FindByIdAsync(tenantId, swap.WeekId, cancellationToken);
                weeks[swap.WeekId] = week;
            }

            if (!locations.TryGetValue(swap.LocationId, out var locationName))
            {
                locationName = (await desk.FindLocationAsync(tenantId, swap.LocationId, cancellationToken))?.Name;
                locations[swap.LocationId] = locationName;
            }

            var shift = week?.FindPublished(swap.ShiftId);
            result.Add(new ShiftSwapDto(
                swap.Id.Value, swap.LocationId.Value, locationName, swap.WeekId.Value, swap.ShiftId.Value, shift?.Date, shift?.Start, shift?.End, shift?.Position,
                swap.ShiftStartsAtUtc, swap.FromMemberId.Value, names.GetValueOrDefault(swap.FromMemberId, "Membre"), swap.ToMemberId.Value,
                names.GetValueOrDefault(swap.ToMemberId, "Membre"), swap.Note, swap.StatusAt(desk.Clock.UtcNow), swap.RequestedAtUtc, swap.DecidedAtUtc));
        }

        return result;
    }

    public static async Task<ShiftSwapRequest> LoadAsync(IShiftSwapRepository swaps, TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await swaps.FindAsync(tenantId, ShiftSwapRequestId.From(id), cancellationToken))
        ?? throw new NotFoundException(StaffApplicationErrors.ShiftSwapNotFound);
}

/// <summary>Remplacements à venir, pour le gérant ; seulement ceux à traiter si <paramref name="OpenOnly"/>.</summary>
public sealed record ListShiftSwapsQuery(bool OpenOnly) : IQuery<IReadOnlyList<ShiftSwapDto>>;

public sealed class ListShiftSwapsHandler(SchedulingDesk desk, IShiftSwapRepository swaps) : IQueryHandler<ListShiftSwapsQuery, IReadOnlyList<ShiftSwapDto>>
{
    public async Task<IReadOnlyList<ShiftSwapDto>> HandleAsync(ListShiftSwapsQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = desk.Workspace.CurrentTenant;
        var found = await swaps.ListAsync(tenantId, desk.Clock.UtcNow.AddDays(-7), null, query.OpenOnly, 500, cancellationToken);
        var visible = query.OpenOnly ? found.Where(swap => swap.StatusAt(desk.Clock.UtcNow) is ShiftSwapStatus.AwaitingColleague or ShiftSwapStatus.AwaitingManager).ToList() : found;
        return await ShiftSwapViews.ToDtosAsync(desk, tenantId, visible.OrderBy(swap => swap.ShiftStartsAtUtc).ToList(), cancellationToken);
    }
}

/// <summary>Le membre connecté propose un de ses créneaux publiés à un collègue du même établissement.</summary>
public sealed record RequestShiftSwapCommand(Guid UserId, Guid WeekId, Guid ShiftId, Guid ToStaffMemberId, string? Note) : ICommand<Guid>;

public sealed class RequestShiftSwapHandler(SchedulingDesk desk, IShiftSwapRepository swaps) : ICommandHandler<RequestShiftSwapCommand, Guid>
{
    public async Task<Guid> HandleAsync(RequestShiftSwapCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var me = await desk.Workspace.LoadSelfAsync(tenantId, command.UserId, cancellationToken);
        var week = (command.WeekId == Guid.Empty ? null : await desk.Schedules.FindByIdAsync(tenantId, ScheduleWeekId.From(command.WeekId), cancellationToken))
            ?? throw new NotFoundException(StaffApplicationErrors.ShiftNotFound);
        var shift = (command.ShiftId == Guid.Empty ? null : week.FindPublished(ShiftId.From(command.ShiftId)))
            ?? throw new NotFoundException(StaffApplicationErrors.ShiftNotFound);
        var location = await desk.FindLocationAsync(tenantId, week.LocationId, cancellationToken)
            ?? throw new NotFoundException(StaffApplicationErrors.ScheduleLocationNotFound);
        var colleague = await desk.Workspace.LoadMemberAsync(tenantId, command.ToStaffMemberId, cancellationToken);
        DomainException.ThrowIf(
            colleague.Status != StaffStatus.Active || colleague.EffectiveRoleAt(week.LocationId) is null,
            StaffApplicationErrors.ColleagueNotHere);
        DomainException.ThrowIf(await swaps.FindOpenForShiftAsync(tenantId, shift.Id, cancellationToken) is { } open && open.StatusAt(desk.Clock.UtcNow) is ShiftSwapStatus.AwaitingColleague or ShiftSwapStatus.AwaitingManager,
            StaffApplicationErrors.SwapAlreadyOpen);

        var startsAt = SchedulingDesk.ToUtc(shift.StartsAt, SchedulingDesk.Zone(location.TimeZoneId));
        var request = ShiftSwapRequest.Request(week, shift.Id, me.Id, colleague.Id, startsAt, command.Note, desk.Clock);
        swaps.Add(request);
        await desk.Workspace.SaveAsync(cancellationToken);
        return request.Id.Value;
    }
}

/// <summary>Le collègue accepte ou refuse le créneau qu'on lui propose.</summary>
public sealed record AnswerShiftSwapCommand(Guid UserId, Guid Id, bool Accept) : ICommand;

public sealed class AnswerShiftSwapHandler(SchedulingDesk desk, IShiftSwapRepository swaps) : ICommandHandler<AnswerShiftSwapCommand>
{
    public async Task HandleAsync(AnswerShiftSwapCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var me = await desk.Workspace.LoadSelfAsync(tenantId, command.UserId, cancellationToken);
        var swap = await ShiftSwapViews.LoadAsync(swaps, tenantId, command.Id, cancellationToken);
        if (swap.ToMemberId != me.Id && swap.FromMemberId != me.Id)
        {
            throw new NotFoundException(StaffApplicationErrors.ShiftSwapNotFound);
        }

        if (command.Accept)
        {
            swap.Accept(me.Id, desk.Clock);
        }
        else
        {
            swap.Decline(me.Id, desk.Clock);
        }

        await desk.Workspace.SaveAsync(cancellationToken);
    }
}

public sealed record CancelMyShiftSwapCommand(Guid UserId, Guid Id) : ICommand;

public sealed class CancelMyShiftSwapHandler(SchedulingDesk desk, IShiftSwapRepository swaps) : ICommandHandler<CancelMyShiftSwapCommand>
{
    public async Task HandleAsync(CancelMyShiftSwapCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var me = await desk.Workspace.LoadSelfAsync(tenantId, command.UserId, cancellationToken);
        var swap = await ShiftSwapViews.LoadAsync(swaps, tenantId, command.Id, cancellationToken);
        if (swap.ToMemberId != me.Id && swap.FromMemberId != me.Id)
        {
            throw new NotFoundException(StaffApplicationErrors.ShiftSwapNotFound);
        }

        swap.Cancel(me.Id, desk.Clock);
        await desk.Workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le gérant valide (le créneau change de titulaire) ou refuse un remplacement.</summary>
public sealed record DecideShiftSwapCommand(Guid Id, bool Approve) : ICommand;

public sealed class DecideShiftSwapHandler(SchedulingDesk desk, IShiftSwapRepository swaps) : ICommandHandler<DecideShiftSwapCommand>
{
    public async Task HandleAsync(DecideShiftSwapCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var swap = await ShiftSwapViews.LoadAsync(swaps, tenantId, command.Id, cancellationToken);
        if (!command.Approve)
        {
            swap.Reject(desk.Clock);
            await desk.Workspace.SaveAsync(cancellationToken);
            return;
        }

        var week = await desk.Schedules.FindByIdAsync(tenantId, swap.WeekId, cancellationToken)
            ?? throw new NotFoundException(StaffApplicationErrors.ShiftNotFound);
        var colleague = await desk.Workspace.LoadMemberAsync(tenantId, swap.ToMemberId.Value, cancellationToken);
        DomainException.ThrowIf(
            colleague.Status != StaffStatus.Active || colleague.EffectiveRoleAt(week.LocationId) is null,
            StaffApplicationErrors.ColleagueNotHere);
        DomainException.ThrowIf(week.FindPublished(swap.ShiftId)?.StaffMemberId != swap.FromMemberId, ScheduleErrors.NotYourShift);
        swap.Approve(desk.Clock);
        week.Reassign(swap.ShiftId, swap.ToMemberId, desk.Clock);
        await desk.Workspace.SaveAsync(cancellationToken);
    }
}
