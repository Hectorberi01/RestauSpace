using Notifications.Application.Abstractions;
using Notifications.Domain;

namespace Notifications.Application.Commands;

// Ce que le restaurant utilise réellement parmi ce que la plateforme propose (§ 35) : une configuration par prestataire.
// Les identifiants saisis partent au coffre, à la référence de CE restaurant ; aucune réponse ne les renvoie.

/// <summary>Le restaurant choisit un prestataire proposé, dans un mode autorisé. Créée inactive.</summary>
public sealed record CreateNotificationConfigurationCommand(Guid ProviderId, NotificationProviderMode Mode) : ICommand<Guid>;

public sealed class CreateNotificationConfigurationHandler(NotificationsWorkspace workspace, IClock clock)
    : ICommandHandler<CreateNotificationConfigurationCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateNotificationConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        await workspace.RequireModeAllowedAsync(command.Mode, cancellationToken);
        var tenant = workspace.CurrentTenant;
        var provider = await workspace.LoadProviderAsync(command.ProviderId, cancellationToken);
        var configuration = RestaurantNotificationConfiguration.Create(
            tenant,
            provider,
            command.Mode,
            await workspace.Configurations.FindByProviderAsync(tenant, provider.Code, cancellationToken) is null,
            clock);
        workspace.Configurations.Add(configuration);
        await workspace.SaveAsync(cancellationToken);
        return configuration.Id.Value;
    }
}

/// <summary>
/// Mode restaurant : range au coffre les identifiants du compte du restaurant (première saisie ou rotation) et rend la
/// configuration prête. La référence est TOUJOURS celle du tenant authentifié : un restaurant ne peut pas écrire chez
/// un autre.
/// </summary>
public sealed record SetConfigurationCredentialsCommand(Guid ConfigurationId, IReadOnlyDictionary<string, string> Credentials) : ICommand;

public sealed class SetConfigurationCredentialsHandler(
    NotificationsWorkspace workspace, CredentialsCheck check, INotificationSecretStore secrets, IClock clock)
    : ICommandHandler<SetConfigurationCredentialsCommand>
{
    public async Task HandleAsync(SetConfigurationCredentialsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var configuration = await workspace.LoadConfigurationAsync(command.ConfigurationId, cancellationToken);
        DomainException.ThrowIf(
            configuration.Mode != NotificationProviderMode.TenantManaged, NotificationConfigurationErrors.CredentialsNotApplicable);
        await workspace.RequireModeAllowedAsync(configuration.Mode, cancellationToken);
        var credentials = check.Validate(configuration.Provider, command.Credentials);
        var reference = NotificationCredentialReference.ForTenant(workspace.CurrentTenant, configuration.Provider);

        // Le coffre d'abord : une configuration ne pointe jamais vers une référence vide.
        await secrets.StoreAsync(reference, credentials, cancellationToken);
        configuration.Configure(reference, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ChangeNotificationModeCommand(Guid ConfigurationId, NotificationProviderMode Mode) : ICommand;

public sealed class ChangeNotificationModeHandler(NotificationsWorkspace workspace, IClock clock) : ICommandHandler<ChangeNotificationModeCommand>
{
    public async Task HandleAsync(ChangeNotificationModeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        await workspace.RequireModeAllowedAsync(command.Mode, cancellationToken);
        var configuration = await workspace.LoadConfigurationAsync(command.ConfigurationId, cancellationToken);
        var provider = await workspace.LoadProviderAsync(configuration.NotificationProviderId.Value, cancellationToken);
        configuration.ChangeMode(command.Mode, provider, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record EnableNotificationConfigurationCommand(Guid ConfigurationId) : ICommand;

public sealed class EnableNotificationConfigurationHandler(NotificationsWorkspace workspace, IClock clock)
    : ICommandHandler<EnableNotificationConfigurationCommand>
{
    public async Task HandleAsync(EnableNotificationConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var configuration = await workspace.LoadConfigurationAsync(command.ConfigurationId, cancellationToken);
        await workspace.RequireModeAllowedAsync(configuration.Mode, cancellationToken);
        var provider = await workspace.LoadProviderAsync(configuration.NotificationProviderId.Value, cancellationToken);
        configuration.Enable(provider, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Toujours possible, même pour un restaurant suspendu : arrêter des envois ne doit jamais être bloqué.</summary>
public sealed record DisableNotificationConfigurationCommand(Guid ConfigurationId) : ICommand;

public sealed class DisableNotificationConfigurationHandler(NotificationsWorkspace workspace, IClock clock)
    : ICommandHandler<DisableNotificationConfigurationCommand>
{
    public async Task HandleAsync(DisableNotificationConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        (await workspace.LoadConfigurationAsync(command.ConfigurationId, cancellationToken)).Disable(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Propose ce canal, par ce prestataire, dans un établissement du MÊME restaurant (rang : le plus petit d'abord). Un
/// établissement d'un autre tenant n'est pas trouvé (404) ; le domaine le revérifie.
/// </summary>
public sealed record AssignNotificationLocationCommand(Guid ConfigurationId, Guid LocationId, int Priority, bool IsEnabled = true) : ICommand;

public sealed class AssignNotificationLocationHandler(NotificationsWorkspace workspace, INotificationLocations locations, IClock clock)
    : ICommandHandler<AssignNotificationLocationCommand>
{
    public async Task HandleAsync(AssignNotificationLocationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var configuration = await workspace.LoadConfigurationAsync(command.ConfigurationId, cancellationToken);
        var location = (command.LocationId == Guid.Empty
                ? null
                : await locations.FindAsync(workspace.CurrentTenant, LocationId.From(command.LocationId), cancellationToken))
            ?? throw new NotFoundException(NotificationsApplicationErrors.LocationNotFound);
        configuration.AssignToLocation(location, command.Priority, clock, command.IsEnabled);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record RemoveNotificationLocationCommand(Guid ConfigurationId, Guid LocationId) : ICommand;

public sealed class RemoveNotificationLocationHandler(NotificationsWorkspace workspace, IClock clock)
    : ICommandHandler<RemoveNotificationLocationCommand>
{
    public async Task HandleAsync(RemoveNotificationLocationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var configuration = await workspace.LoadConfigurationAsync(command.ConfigurationId, cancellationToken);
        configuration.RemoveFromLocation(LocationId.From(command.LocationId), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}
