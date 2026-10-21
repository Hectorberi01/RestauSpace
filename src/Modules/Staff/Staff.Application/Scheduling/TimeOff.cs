using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Scheduling;

public sealed record TimeOffDto(
    Guid Id,
    Guid StaffMemberId,
    string Name,
    DateOnly FirstDay,
    DateOnly LastDay,
    TimeOffKind Kind,
    string? Comment,
    TimeOffStatus Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNote)
{
    public static TimeOffDto From(TimeOffRequest request, string name) =>
        new(request.Id.Value, request.StaffMemberId.Value, name, request.FirstDay, request.LastDay, request.Kind, request.Comment, request.Status,
            request.RequestedAtUtc, request.DecidedAtUtc, request.DecisionNote);
}

/// <summary>Absences qui touchent la période (92 jours au plus), pour le gérant.</summary>
public sealed record ListTimeOffQuery(DateOnly From, DateOnly To, TimeOffStatus? Status) : IQuery<IReadOnlyList<TimeOffDto>>;

public sealed class ListTimeOffHandler(SchedulingDesk desk, ITimeOffRepository absences) : IQueryHandler<ListTimeOffQuery, IReadOnlyList<TimeOffDto>>
{
    public async Task<IReadOnlyList<TimeOffDto>> HandleAsync(ListTimeOffQuery query, CancellationToken cancellationToken = default)
    {
        DomainException.ThrowIf(query.To < query.From || query.To.DayNumber - query.From.DayNumber > 92, StaffApplicationErrors.TimeRangeInvalid);
        var tenantId = desk.Workspace.CurrentTenant;
        var found = await absences.ListAsync(tenantId, query.From, query.To, null, query.Status, 1000, cancellationToken);
        var names = await desk.NamesAsync(tenantId, found.Select(absence => absence.StaffMemberId), null, cancellationToken);
        return found.OrderBy(absence => absence.FirstDay)
            .Select(absence => TimeOffDto.From(absence, names.GetValueOrDefault(absence.StaffMemberId, "Membre")))
            .ToList();
    }
}

/// <summary>Le membre connecté demande une absence ; le gérant la valide ou la refuse.</summary>
public sealed record RequestMyTimeOffCommand(Guid UserId, DateOnly FirstDay, DateOnly LastDay, TimeOffKind Kind, string? Comment) : ICommand<Guid>;

public sealed class RequestMyTimeOffHandler(SchedulingDesk desk, ITimeOffRepository absences) : ICommandHandler<RequestMyTimeOffCommand, Guid>
{
    public async Task<Guid> HandleAsync(RequestMyTimeOffCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await desk.Workspace.LoadSelfAsync(tenantId, command.UserId, cancellationToken);
        DomainException.ThrowIf(member.Status != StaffStatus.Active, StaffMemberErrors.NotActive);
        var request = TimeOffRequest.Request(
            tenantId, member.Id, command.FirstDay, command.LastDay, command.Kind, command.Comment, byManager: false, Yesterday(desk), desk.Clock);
        absences.Add(request);
        await desk.Workspace.SaveAsync(cancellationToken);
        return request.Id.Value;
    }

    // Sans établissement ici : la veille en UTC, pour qu'aucun fuseau ne refuse « aujourd'hui ».
    internal static DateOnly Yesterday(SchedulingDesk desk) => DateOnly.FromDateTime(desk.Clock.UtcNow.UtcDateTime).AddDays(-1);
}

/// <summary>Le gérant saisit une absence pour un membre : validée d'emblée.</summary>
public sealed record RecordTimeOffCommand(Guid StaffMemberId, DateOnly FirstDay, DateOnly LastDay, TimeOffKind Kind, string? Comment) : ICommand<Guid>;

public sealed class RecordTimeOffHandler(SchedulingDesk desk, ITimeOffRepository absences) : ICommandHandler<RecordTimeOffCommand, Guid>
{
    public async Task<Guid> HandleAsync(RecordTimeOffCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await desk.Workspace.LoadMemberAsync(tenantId, command.StaffMemberId, cancellationToken);
        var request = TimeOffRequest.Request(
            tenantId, member.Id, command.FirstDay, command.LastDay, command.Kind, command.Comment, byManager: true,
            RequestMyTimeOffHandler.Yesterday(desk), desk.Clock);
        absences.Add(request);
        await desk.Workspace.SaveAsync(cancellationToken);
        return request.Id.Value;
    }
}

public enum TimeOffDecision
{
    Approve = 1,
    Reject = 2,
    Cancel = 3,
}

public sealed record DecideTimeOffCommand(Guid Id, TimeOffDecision Decision, string? Note) : ICommand;

public sealed class DecideTimeOffHandler(SchedulingDesk desk, ITimeOffRepository absences) : ICommandHandler<DecideTimeOffCommand>
{
    public async Task HandleAsync(DecideTimeOffCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var request = await LoadAsync(absences, tenantId, command.Id, cancellationToken);
        switch (command.Decision)
        {
            case TimeOffDecision.Approve:
                request.Approve(desk.Clock);
                break;
            case TimeOffDecision.Reject:
                request.Reject(command.Note, desk.Clock);
                break;
            case TimeOffDecision.Cancel:
                request.Cancel(desk.Clock);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Decision, "Unknown decision.");
        }

        await desk.Workspace.SaveAsync(cancellationToken);
    }

    internal static async Task<TimeOffRequest> LoadAsync(ITimeOffRepository absences, TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await absences.FindAsync(tenantId, TimeOffRequestId.From(id), cancellationToken))
        ?? throw new NotFoundException(StaffApplicationErrors.TimeOffNotFound);
}

/// <summary>Le membre retire sa demande encore en attente.</summary>
public sealed record CancelMyTimeOffCommand(Guid UserId, Guid Id) : ICommand;

public sealed class CancelMyTimeOffHandler(SchedulingDesk desk, ITimeOffRepository absences) : ICommandHandler<CancelMyTimeOffCommand>
{
    public async Task HandleAsync(CancelMyTimeOffCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await desk.Workspace.LoadSelfAsync(tenantId, command.UserId, cancellationToken);
        var request = await DecideTimeOffHandler.LoadAsync(absences, tenantId, command.Id, cancellationToken);
        if (request.StaffMemberId != member.Id)
        {
            throw new NotFoundException(StaffApplicationErrors.TimeOffNotFound);
        }

        DomainException.ThrowIf(request.Status != TimeOffStatus.Pending, ScheduleErrors.TimeOffClosed);
        request.Cancel(desk.Clock);
        await desk.Workspace.SaveAsync(cancellationToken);
    }
}
