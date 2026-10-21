using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;
using Staff.Application;
using Staff.Application.Scheduling;
using Staff.Domain;

namespace Staff.Api;

public sealed record ShiftRequest(Guid StaffMemberId, DateOnly Date, TimeOnly Start, TimeOnly End, int BreakMinutes, string? Position, string? Note)
{
    public ShiftInput ToInput() => new(StaffMemberId, Date, Start, End, BreakMinutes, Position, Note);
}

public sealed record ScheduleSettingsRequest(IReadOnlyList<string?>? Positions, int WeeklyHoursLimit);

public sealed record TimeOffRequestBody(DateOnly FirstDay, DateOnly LastDay, TimeOffKind Kind, string? Comment, Guid? StaffMemberId = null);

public sealed record DecisionNoteRequest(string? Note);

public sealed record ShiftSwapRequestBody(Guid WeekId, Guid ShiftId, Guid ToStaffMemberId, string? Note);

public sealed record CopiedResponse(int Copied);

public sealed record PublishedResponse(int Notified);

public static partial class StaffEndpoints
{
    /// <summary>
    /// Planning : le gérant (<c>staff.schedule</c>) sous <c>/staff/schedule</c>, chaque membre pour lui-même sous
    /// <c>/staff/me</c> (son profil est trouvé par l'utilisateur du token, jamais par un id envoyé).
    /// </summary>
    private static void MapSchedule(RouteGroupBuilder staff)
    {
        var schedule = staff.MapGroup("/schedule");

        schedule.MapGet("/settings/{locationId:guid}", async (
            Guid locationId, IQueryHandler<GetScheduleSettingsQuery, ScheduleSettingsDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetScheduleSettingsQuery(locationId), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapPut("/settings/{locationId:guid}", (
            Guid locationId, ScheduleSettingsRequest request, ICommandHandler<SetScheduleSettingsCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetScheduleSettingsCommand(locationId, request.Positions, request.WeeklyHoursLimit), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapGet("/weeks/{locationId:guid}/{weekStart}", async (
            Guid locationId, DateOnly weekStart, IQueryHandler<GetScheduleWeekQuery, ScheduleWeekDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetScheduleWeekQuery(locationId, weekStart), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapPost("/weeks/{locationId:guid}/{weekStart}/shifts", async (
            Guid locationId, DateOnly weekStart, ShiftRequest request, ICommandHandler<AddShiftCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(new AddShiftCommand(locationId, weekStart, request.ToInput()), ct))))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapPut("/weeks/{locationId:guid}/{weekStart}/shifts/{shiftId:guid}", (
            Guid locationId, DateOnly weekStart, Guid shiftId, ShiftRequest request, ICommandHandler<UpdateShiftCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new UpdateShiftCommand(locationId, weekStart, shiftId, request.ToInput()), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapDelete("/weeks/{locationId:guid}/{weekStart}/shifts/{shiftId:guid}", (
            Guid locationId, DateOnly weekStart, Guid shiftId, ICommandHandler<RemoveShiftCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveShiftCommand(locationId, weekStart, shiftId), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapPost("/weeks/{locationId:guid}/{weekStart}/copy-previous", async (
            Guid locationId, DateOnly weekStart, ICommandHandler<CopyPreviousWeekCommand, int> handler, CancellationToken ct) =>
            TypedResults.Ok(new CopiedResponse(await handler.HandleAsync(new CopyPreviousWeekCommand(locationId, weekStart), ct))))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapPost("/weeks/{locationId:guid}/{weekStart}/publish", async (
            Guid locationId, DateOnly weekStart, ICommandHandler<PublishScheduleCommand, int> handler, CancellationToken ct) =>
            TypedResults.Ok(new PublishedResponse(await handler.HandleAsync(new PublishScheduleCommand(locationId, weekStart), ct))))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapGet("/weeks/{locationId:guid}/{weekStart}/variance", async (
            Guid locationId, DateOnly weekStart, IQueryHandler<GetScheduleVarianceQuery, ScheduleVarianceDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetScheduleVarianceQuery(locationId, weekStart), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapGet("/time-off", async (
            DateOnly from, DateOnly to, TimeOffStatus? status, IQueryHandler<ListTimeOffQuery, IReadOnlyList<TimeOffDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListTimeOffQuery(from, to, status), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapPost("/time-off", async (
            TimeOffRequestBody request, ICommandHandler<RecordTimeOffCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(
                new RecordTimeOffCommand(request.StaffMemberId ?? Guid.Empty, request.FirstDay, request.LastDay, request.Kind, request.Comment), ct))))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapPost("/time-off/{id:guid}/approve", (Guid id, ICommandHandler<DecideTimeOffCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DecideTimeOffCommand(id, TimeOffDecision.Approve, null), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapPost("/time-off/{id:guid}/reject", (Guid id, DecisionNoteRequest? request, ICommandHandler<DecideTimeOffCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DecideTimeOffCommand(id, TimeOffDecision.Reject, request?.Note), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapPost("/time-off/{id:guid}/cancel", (Guid id, ICommandHandler<DecideTimeOffCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DecideTimeOffCommand(id, TimeOffDecision.Cancel, null), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapGet("/swaps", async (bool? openOnly, IQueryHandler<ListShiftSwapsQuery, IReadOnlyList<ShiftSwapDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListShiftSwapsQuery(openOnly ?? true), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapPost("/swaps/{id:guid}/approve", (Guid id, ICommandHandler<DecideShiftSwapCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DecideShiftSwapCommand(id, Approve: true), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        schedule.MapPost("/swaps/{id:guid}/reject", (Guid id, ICommandHandler<DecideShiftSwapCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DecideShiftSwapCommand(id, Approve: false), ct)))
            .RequirePermission(StaffPermissions.Schedule);

        // Tout membre du restaurant, pour lui-même.
        staff.MapGet("/me/schedule", async (
            DateOnly? from, int? weeks, ClaimsPrincipal caller, IQueryHandler<GetMyScheduleQuery, MyScheduleDto> handler, IClock clock, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new GetMyScheduleQuery(UserOf(caller), from ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), weeks ?? 3), ct)));

        staff.MapPost("/me/time-off", async (
            TimeOffRequestBody request, ClaimsPrincipal caller, ICommandHandler<RequestMyTimeOffCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(
                new RequestMyTimeOffCommand(UserOf(caller), request.FirstDay, request.LastDay, request.Kind, request.Comment), ct))));

        staff.MapPost("/me/time-off/{id:guid}/cancel", (
            Guid id, ClaimsPrincipal caller, ICommandHandler<CancelMyTimeOffCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CancelMyTimeOffCommand(UserOf(caller), id), ct)));

        staff.MapPost("/me/swaps", async (
            ShiftSwapRequestBody request, ClaimsPrincipal caller, ICommandHandler<RequestShiftSwapCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(
                new RequestShiftSwapCommand(UserOf(caller), request.WeekId, request.ShiftId, request.ToStaffMemberId, request.Note), ct))));

        staff.MapPost("/me/swaps/{id:guid}/accept", (
            Guid id, ClaimsPrincipal caller, ICommandHandler<AnswerShiftSwapCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AnswerShiftSwapCommand(UserOf(caller), id, Accept: true), ct)));

        staff.MapPost("/me/swaps/{id:guid}/decline", (
            Guid id, ClaimsPrincipal caller, ICommandHandler<AnswerShiftSwapCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AnswerShiftSwapCommand(UserOf(caller), id, Accept: false), ct)));

        staff.MapPost("/me/swaps/{id:guid}/cancel", (
            Guid id, ClaimsPrincipal caller, ICommandHandler<CancelMyShiftSwapCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CancelMyShiftSwapCommand(UserOf(caller), id), ct)));
    }
}
