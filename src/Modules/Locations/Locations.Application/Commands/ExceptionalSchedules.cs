namespace Locations.Application.Commands;

// Une exception par date : un jour fermé, ou une ouverture dont les plages remplacent celles de la semaine pour cette date.

public sealed record AddExceptionalClosureCommand(Guid LocationId, DateOnly Date, string? Reason = null) : ICommand;

public sealed class AddExceptionalClosureHandler(LocationEditor editor, IClock clock) : ICommandHandler<AddExceptionalClosureCommand>
{
    public Task HandleAsync(AddExceptionalClosureCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(
            command.LocationId, location => location.AddExceptionalClosure(command.Date, clock, command.Reason), cancellationToken);
}

public sealed record AddExceptionalOpeningCommand(
    Guid LocationId, DateOnly Date, IReadOnlyList<TimeRangeInput>? Ranges, string? Reason = null) : ICommand;

public sealed class AddExceptionalOpeningHandler(LocationEditor editor, IClock clock) : ICommandHandler<AddExceptionalOpeningCommand>
{
    public Task HandleAsync(AddExceptionalOpeningCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(
            command.LocationId,
            location => location.AddExceptionalSchedule(command.Date, LocationInputs.ToRanges(command.Ranges), clock, command.Reason),
            cancellationToken);
}

public sealed record RemoveExceptionalScheduleCommand(Guid LocationId, DateOnly Date) : ICommand;

public sealed class RemoveExceptionalScheduleHandler(LocationEditor editor, IClock clock)
    : ICommandHandler<RemoveExceptionalScheduleCommand>
{
    public Task HandleAsync(RemoveExceptionalScheduleCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(command.LocationId, location => location.RemoveExceptionalSchedule(command.Date, clock), cancellationToken);
}
