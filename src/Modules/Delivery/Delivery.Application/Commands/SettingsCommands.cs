using Delivery.Domain;

namespace Delivery.Application.Commands;

// Réglages d'un établissement (delivery.configure). Les réglages sont créés avec les valeurs par défaut à la première
// modification ; l'établissement est chargé dans le tenant du contexte (404 sinon).

/// <summary>Active ou désactive le retrait et fixe son délai annoncé (0 à 240 minutes).</summary>
public sealed record ConfigurePickupCommand(Guid LocationId, bool Enabled, int LeadMinutes) : ICommand;

public sealed class ConfigurePickupHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<ConfigurePickupCommand>
{
    public async Task HandleAsync(ConfigurePickupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var settings = await workspace.LoadOrCreateSettingsAsync(command.LocationId, cancellationToken);
        settings.ConfigurePickup(command.Enabled, command.LeadMinutes, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Active ou désactive la livraison (au moins une zone active pour l'activer) et fixe le délai de préparation.</summary>
public sealed record ConfigureDeliveryCommand(Guid LocationId, bool Enabled, int LeadMinutes) : ICommand;

public sealed class ConfigureDeliveryHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<ConfigureDeliveryCommand>
{
    public async Task HandleAsync(ConfigureDeliveryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var settings = await workspace.LoadOrCreateSettingsAsync(command.LocationId, cancellationToken);
        settings.ConfigureDelivery(command.Enabled, command.LeadMinutes, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Ajoute une zone de livraison (montants dans la devise du restaurant). Renvoie l'id de la zone.</summary>
public sealed record AddDeliveryZoneCommand(Guid LocationId, string? Name, decimal Fee, decimal MinimumOrder, int EstimatedMinutes) : ICommand<Guid>;

public sealed class AddDeliveryZoneHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<AddDeliveryZoneCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddDeliveryZoneCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var settings = await workspace.LoadOrCreateSettingsAsync(command.LocationId, cancellationToken);
        var zoneId = settings.AddZone(
            command.Name,
            Money.Of(command.Fee, settings.Currency),
            Money.Of(command.MinimumOrder, settings.Currency),
            command.EstimatedMinutes,
            clock);
        await workspace.SaveAsync(cancellationToken);
        return zoneId.Value;
    }
}

public sealed record UpdateDeliveryZoneCommand(Guid LocationId, Guid ZoneId, string? Name, decimal Fee, decimal MinimumOrder, int EstimatedMinutes) : ICommand;

public sealed class UpdateDeliveryZoneHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<UpdateDeliveryZoneCommand>
{
    public async Task HandleAsync(UpdateDeliveryZoneCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var settings = await workspace.LoadOrCreateSettingsAsync(command.LocationId, cancellationToken);
        settings.UpdateZone(
            ZoneIdOf(command.ZoneId),
            command.Name,
            Money.Of(command.Fee, settings.Currency),
            Money.Of(command.MinimumOrder, settings.Currency),
            command.EstimatedMinutes,
            clock);
        await workspace.SaveAsync(cancellationToken);
    }

    internal static DeliveryZoneId ZoneIdOf(Guid id) =>
        id == Guid.Empty ? throw new DomainException(DeliverySettingsErrors.ZoneNotFound) : DeliveryZoneId.From(id);
}

public sealed record SetDeliveryZoneActiveCommand(Guid LocationId, Guid ZoneId, bool Active) : ICommand;

public sealed class SetDeliveryZoneActiveHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<SetDeliveryZoneActiveCommand>
{
    public async Task HandleAsync(SetDeliveryZoneActiveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var settings = await workspace.LoadOrCreateSettingsAsync(command.LocationId, cancellationToken);
        settings.SetZoneActive(UpdateDeliveryZoneHandler.ZoneIdOf(command.ZoneId), command.Active, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record RemoveDeliveryZoneCommand(Guid LocationId, Guid ZoneId) : ICommand;

public sealed class RemoveDeliveryZoneHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<RemoveDeliveryZoneCommand>
{
    public async Task HandleAsync(RemoveDeliveryZoneCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var settings = await workspace.LoadOrCreateSettingsAsync(command.LocationId, cancellationToken);
        settings.RemoveZone(UpdateDeliveryZoneHandler.ZoneIdOf(command.ZoneId), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}
