namespace Locations.Application.Commands;

// Draft -> Active -> TemporarilyClosed -> Active ; tout état -> Archived (terminal). Les règles sont celles de l'agrégat.

public sealed record ActivateLocationCommand(Guid LocationId) : ICommand;

public sealed class ActivateLocationHandler(LocationEditor editor, IClock clock) : ICommandHandler<ActivateLocationCommand>
{
    public Task HandleAsync(ActivateLocationCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(command.LocationId, location => location.Activate(clock), cancellationToken);
}

public sealed record TemporarilyCloseLocationCommand(Guid LocationId, string? Reason) : ICommand;

public sealed class TemporarilyCloseLocationHandler(LocationEditor editor, IClock clock)
    : ICommandHandler<TemporarilyCloseLocationCommand>
{
    public Task HandleAsync(TemporarilyCloseLocationCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(command.LocationId, location => location.TemporarilyClose(clock, command.Reason), cancellationToken);
}

public sealed record ReopenLocationCommand(Guid LocationId) : ICommand;

public sealed class ReopenLocationHandler(LocationEditor editor, IClock clock) : ICommandHandler<ReopenLocationCommand>
{
    public Task HandleAsync(ReopenLocationCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(command.LocationId, location => location.Reopen(clock), cancellationToken);
}

public sealed record ArchiveLocationCommand(Guid LocationId) : ICommand;

public sealed class ArchiveLocationHandler(LocationEditor editor, IClock clock) : ICommandHandler<ArchiveLocationCommand>
{
    public Task HandleAsync(ArchiveLocationCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(command.LocationId, location => location.Archive(clock), cancellationToken);
}
