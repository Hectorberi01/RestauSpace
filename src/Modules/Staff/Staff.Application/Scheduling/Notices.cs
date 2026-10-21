using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Scheduling;

// Lectures pour l'hôte, qui prévient l'équipe à partir des événements du planning : le compte et le nom court des
// personnes concernées, jamais leurs coordonnées (l'hôte les demande à Identity).

public sealed record ScheduleNoticePerson(Guid StaffMemberId, Guid UserId, string Name);

public sealed record TimeOffNoticeDto(ScheduleNoticePerson Member, DateOnly FirstDay, DateOnly LastDay, TimeOffStatus Status);

public sealed record GetTimeOffNoticeQuery(Guid Id) : IQuery<TimeOffNoticeDto?>;

public sealed class GetTimeOffNoticeHandler(SchedulingDesk desk, ITimeOffRepository absences) : IQueryHandler<GetTimeOffNoticeQuery, TimeOffNoticeDto?>
{
    public async Task<TimeOffNoticeDto?> HandleAsync(GetTimeOffNoticeQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = desk.Workspace.CurrentTenant;
        if (query.Id == Guid.Empty || await absences.FindAsync(tenantId, TimeOffRequestId.From(query.Id), cancellationToken) is not { } request)
        {
            return null;
        }

        var person = await ShiftSwapNoticeHandler.PersonAsync(desk, tenantId, request.StaffMemberId, cancellationToken);
        return person is null ? null : new TimeOffNoticeDto(person, request.FirstDay, request.LastDay, request.Status);
    }
}

public sealed record ShiftSwapNoticeDto(
    Guid LocationId, DateOnly? Date, TimeOnly? Start, TimeOnly? End, ScheduleNoticePerson From, ScheduleNoticePerson To, ShiftSwapStatus Status);

public sealed record GetShiftSwapNoticeQuery(Guid Id) : IQuery<ShiftSwapNoticeDto?>;

public sealed class ShiftSwapNoticeHandler(SchedulingDesk desk, IShiftSwapRepository swaps) : IQueryHandler<GetShiftSwapNoticeQuery, ShiftSwapNoticeDto?>
{
    public async Task<ShiftSwapNoticeDto?> HandleAsync(GetShiftSwapNoticeQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = desk.Workspace.CurrentTenant;
        if (query.Id == Guid.Empty || await swaps.FindAsync(tenantId, ShiftSwapRequestId.From(query.Id), cancellationToken) is not { } swap)
        {
            return null;
        }

        var week = await desk.Schedules.FindByIdAsync(tenantId, swap.WeekId, cancellationToken);
        var shift = week?.FindPublished(swap.ShiftId);
        var from = await PersonAsync(desk, tenantId, swap.FromMemberId, cancellationToken);
        var to = await PersonAsync(desk, tenantId, swap.ToMemberId, cancellationToken);
        return from is null || to is null
            ? null
            : new ShiftSwapNoticeDto(swap.LocationId.Value, shift?.Date, shift?.Start, shift?.End, from, to, swap.StatusAt(desk.Clock.UtcNow));
    }

    internal static async Task<ScheduleNoticePerson?> PersonAsync(SchedulingDesk desk, TenantId tenantId, StaffMemberId id, CancellationToken cancellationToken)
    {
        if (await desk.Workspace.Members.FindAsync(tenantId, id, cancellationToken) is not { } member)
        {
            return null;
        }

        var names = await desk.NamesAsync(tenantId, [id], [member], cancellationToken);
        return new ScheduleNoticePerson(id.Value, member.UserId.Value, names.GetValueOrDefault(id, "Membre"));
    }
}
