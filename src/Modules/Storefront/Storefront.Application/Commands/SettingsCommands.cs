using Storefront.Domain;

namespace Storefront.Application.Commands;

/// <summary>La règle d'un canal telle que saisie dans la console.</summary>
public sealed record ChannelPolicyInput(SalesAppKind Channel, bool PayOnline, bool PayAtCounter, decimal? CounterLimit, OrderAcceptance Acceptance)
{
    public ChannelPolicy ToPolicy() => ChannelPolicy.Create(Channel, PayOnline, PayAtCounter, CounterLimit, Acceptance);
}

/// <summary>Enregistre les réglages de vente en ligne d'un établissement du restaurant courant (créés au premier enregistrement).</summary>
public sealed record SaveOnlineOrderingSettingsCommand(
    Guid LocationId,
    bool IsPaused,
    IReadOnlyList<ServiceMode>? ServiceModes,
    int PreparationMinutes,
    decimal? MinimumOrderAmount,
    int ManualAcceptanceMinutes,
    int UnpaidExpiryMinutes,
    IReadOnlyList<ChannelPolicyInput>? Policies,
    int? SlotCapacity = null) : ICommand;

public sealed class SaveOnlineOrderingSettingsHandler(StorefrontWorkspace workspace, IClock clock) : ICommandHandler<SaveOnlineOrderingSettingsCommand>
{
    public async Task HandleAsync(SaveOnlineOrderingSettingsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var settings = await OnlineOrderingSettingsLoader.LoadOrCreateAsync(workspace, command.LocationId, clock, cancellationToken);
        settings.Configure(
            command.IsPaused,
            command.ServiceModes ?? [],
            command.PreparationMinutes,
            command.MinimumOrderAmount,
            command.ManualAcceptanceMinutes,
            command.UnpaidExpiryMinutes,
            (command.Policies ?? []).Select(policy => policy?.ToPolicy()!),
            clock,
            command.SlotCapacity);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Met en pause ou reprend la prise de commandes en ligne d'un établissement. La pause reste possible pour un restaurant
/// suspendu : arrêter de prendre des commandes ne doit jamais être bloqué.
/// </summary>
public sealed record PauseOnlineOrderingCommand(Guid LocationId, bool IsPaused) : ICommand;

public sealed class PauseOnlineOrderingHandler(StorefrontWorkspace workspace, IClock clock) : ICommandHandler<PauseOnlineOrderingCommand>
{
    public async Task HandleAsync(PauseOnlineOrderingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.IsPaused)
        {
            await workspace.RequireWritableTenantAsync(cancellationToken);
        }

        var settings = await OnlineOrderingSettingsLoader.LoadOrCreateAsync(workspace, command.LocationId, clock, cancellationToken);
        settings.SetPaused(command.IsPaused, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

internal static class OnlineOrderingSettingsLoader
{
    public static async Task<OnlineOrderingSettings> LoadOrCreateAsync(
        StorefrontWorkspace workspace, Guid locationId, IClock clock, CancellationToken cancellationToken)
    {
        var location = (await workspace.RequireLocationsAsync([locationId], clock.UtcNow, cancellationToken)).Single();
        var existing = await workspace.Settings.FindAsync(workspace.CurrentTenant, location.LocationId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = OnlineOrderingSettings.Default(location, clock);
        workspace.Settings.Add(created);
        return created;
    }
}
