using Locations.Application.Abstractions;
using Locations.Domain.Locations;

namespace Locations.Application.Commands;

/// <summary>
/// Crée un établissement du tenant courant, en Draft. <paramref name="TimeZoneId"/> null = le fuseau du tenant.
/// Renvoie l'id du nouvel établissement.
/// </summary>
public sealed record CreateLocationCommand(
    string? Name,
    AddressInput? Address,
    int Seats,
    string? TimeZoneId = null,
    IReadOnlyList<WeeklySlotInput>? WeeklyHours = null) : ICommand<Guid>;

public sealed class CreateLocationHandler(LocationEditor editor, ILocationRepository locations, ILocationPlanLimits limits, IClock clock)
    : ICommandHandler<CreateLocationCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateLocationCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await editor.RequireWritableTenantAsync(cancellationToken);
        if (await limits.MaxLocationsAsync(editor.CurrentTenant, cancellationToken) is { } limit)
        {
            var current = await locations.ListAsync(editor.CurrentTenant, includeArchived: false, 0, 1, cancellationToken);
            DomainException.ThrowIf(current.TotalCount >= limit, LocationsApplicationErrors.PlanLimitReached);
        }

        var location = RestaurantLocation.Create(
            editor.CurrentTenant,
            LocationName.Create(command.Name),
            LocationInputs.ToAddress(command.Address ?? new AddressInput(null, null, null, null)),
            LocationInputs.ToHours(command.WeeklyHours),
            Capacity.Create(command.Seats),
            command.TimeZoneId is null ? tenant.DefaultTimeZone : TimeZoneId.Create(command.TimeZoneId),
            clock);

        editor.Add(location);
        await editor.SaveAsync(cancellationToken);
        return location.Id.Value;
    }
}
