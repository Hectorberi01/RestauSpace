using Locations.Domain.Locations;

namespace Locations.Application.Commands;

// Chaque réglage est remplacé en entier ; une valeur identique est sans effet et sans événement (règle du domaine).

public sealed record RenameLocationCommand(Guid LocationId, string? Name) : ICommand;

public sealed class RenameLocationHandler(LocationEditor editor, IClock clock) : ICommandHandler<RenameLocationCommand>
{
    public Task HandleAsync(RenameLocationCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(command.LocationId, location => location.Rename(LocationName.Create(command.Name), clock), cancellationToken);
}

public sealed record ChangeLocationAddressCommand(Guid LocationId, AddressInput? Address) : ICommand;

public sealed class ChangeLocationAddressHandler(LocationEditor editor, IClock clock) : ICommandHandler<ChangeLocationAddressCommand>
{
    public Task HandleAsync(ChangeLocationAddressCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(
            command.LocationId,
            location => location.ChangeAddress(
                LocationInputs.ToAddress(command.Address ?? new AddressInput(null, null, null, null)), clock),
            cancellationToken);
}

public sealed record SetLocationContactCommand(Guid LocationId, ContactInput? Contact) : ICommand
{
    public override string ToString() => $"SetLocationContactCommand({LocationId}, ***)";
}

public sealed class SetLocationContactHandler(LocationEditor editor, IClock clock) : ICommandHandler<SetLocationContactCommand>
{
    public Task HandleAsync(SetLocationContactCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(command.LocationId, location => location.SetContact(LocationInputs.ToContact(command.Contact), clock), cancellationToken);
}

public sealed record SetLocationCapacityCommand(Guid LocationId, int Seats) : ICommand;

public sealed class SetLocationCapacityHandler(LocationEditor editor, IClock clock) : ICommandHandler<SetLocationCapacityCommand>
{
    public Task HandleAsync(SetLocationCapacityCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(command.LocationId, location => location.SetCapacity(Capacity.Create(command.Seats), clock), cancellationToken);
}

public sealed record ChangeLocationTimeZoneCommand(Guid LocationId, string? TimeZoneId) : ICommand;

public sealed class ChangeLocationTimeZoneHandler(LocationEditor editor, IClock clock) : ICommandHandler<ChangeLocationTimeZoneCommand>
{
    public Task HandleAsync(ChangeLocationTimeZoneCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(
            command.LocationId, location => location.ChangeTimeZone(TimeZoneId.Create(command.TimeZoneId), clock), cancellationToken);
}

public sealed record SetBusinessHoursCommand(Guid LocationId, IReadOnlyList<WeeklySlotInput>? WeeklyHours) : ICommand;

public sealed class SetBusinessHoursHandler(LocationEditor editor, IClock clock) : ICommandHandler<SetBusinessHoursCommand>
{
    public Task HandleAsync(SetBusinessHoursCommand command, CancellationToken cancellationToken = default) =>
        editor.EditAsync(
            command.LocationId, location => location.SetBusinessHours(LocationInputs.ToHours(command.WeeklyHours), clock), cancellationToken);
}
