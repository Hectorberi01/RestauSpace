using Notifications.Application.Abstractions;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application.Commands;

// Administration du catalogue de la PLATEFORME (§ 34) : quels prestataires la plateforme sait utiliser, leurs
// capacités, comment un restaurant peut les configurer, et le compte de la plateforme chez eux.

/// <summary>Ce qu'un prestataire sait faire, saisi par l'administrateur.</summary>
public sealed record ProviderCapabilityInput(
    IReadOnlyList<NotificationFeature> Features, IReadOnlyList<string>? Countries = null, int? MaxContentLength = null)
{
    public NotificationProviderCapability ToCapability(NotificationChannel channel)
    {
        ArgumentNullException.ThrowIfNull(Features);
        return NotificationProviderCapability.Create(channel, Features, Countries?.Select(CountryCode.Create), MaxContentLength);
    }
}

/// <summary>
/// Ajoute un prestataire au catalogue, NON proposé aux restaurants (<see cref="EnableNotificationProviderCommand"/> ensuite).
/// Son code doit correspondre à une intégration présente dans le code de la plateforme (sauf le prestataire interne).
/// </summary>
public sealed record CreateNotificationProviderCommand(
    string Code,
    string Name,
    NotificationChannel Channel,
    NotificationProviderConfigurationType ConfigurationType,
    ProviderCapabilityInput Capability) : ICommand<Guid>;

public sealed class CreateNotificationProviderHandler(
    INotificationProviderRepository providers, INotificationSenderResolver resolver, INotificationsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<CreateNotificationProviderCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateNotificationProviderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var code = NotificationProviderCode.Create(command.Code);
        DomainException.ThrowIf(
            code != NotificationProviderCode.InApp && resolver.FindIntegration(code) is null,
            NotificationsApplicationErrors.IntegrationMissing);
        var provider = NotificationProvider.Create(
            code,
            command.Name,
            command.Channel,
            command.ConfigurationType,
            command.Capability.ToCapability(command.Channel),
            await providers.FindByCodeAsync(code, cancellationToken) is null,
            clock);
        providers.Add(provider);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return provider.Id.Value;
    }
}

/// <summary>Renomme, remplace les capacités ou le type de configuration (chaque changement seulement s'il diffère).</summary>
public sealed record UpdateNotificationProviderCommand(
    Guid ProviderId, string Name, NotificationProviderConfigurationType ConfigurationType, ProviderCapabilityInput Capability) : ICommand;

public sealed class UpdateNotificationProviderHandler(NotificationsWorkspace workspace, IClock clock)
    : ICommandHandler<UpdateNotificationProviderCommand>
{
    public async Task HandleAsync(UpdateNotificationProviderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var provider = await workspace.LoadProviderAsync(command.ProviderId, cancellationToken);
        provider.Rename(command.Name, clock);
        provider.UpdateCapabilities(command.Capability.ToCapability(provider.Channel), clock);
        provider.ChangeConfigurationType(command.ConfigurationType, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record EnableNotificationProviderCommand(Guid ProviderId) : ICommand;

public sealed class EnableNotificationProviderHandler(NotificationsWorkspace workspace, IClock clock)
    : ICommandHandler<EnableNotificationProviderCommand>
{
    public async Task HandleAsync(EnableNotificationProviderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        (await workspace.LoadProviderAsync(command.ProviderId, cancellationToken)).Enable(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Plus aucun nouvel envoi par ce prestataire, pour aucun restaurant ; ceux en cours restent suivis.</summary>
public sealed record DisableNotificationProviderCommand(Guid ProviderId) : ICommand;

public sealed class DisableNotificationProviderHandler(NotificationsWorkspace workspace, IClock clock)
    : ICommandHandler<DisableNotificationProviderCommand>
{
    public async Task HandleAsync(DisableNotificationProviderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        (await workspace.LoadProviderAsync(command.ProviderId, cancellationToken)).Disable(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Range au coffre le compte de la PLATEFORME chez ce prestataire (mode PlatformManaged, notifications de la
/// plateforme). Jamais renvoyé ensuite, par aucune route.
/// </summary>
public sealed record SetPlatformCredentialsCommand(Guid ProviderId, IReadOnlyDictionary<string, string> Credentials) : ICommand;

public sealed class SetPlatformCredentialsHandler(NotificationsWorkspace workspace, CredentialsCheck check, INotificationSecretStore secrets)
    : ICommandHandler<SetPlatformCredentialsCommand>
{
    public async Task HandleAsync(SetPlatformCredentialsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var provider = await workspace.LoadProviderAsync(command.ProviderId, cancellationToken);
        DomainException.ThrowIf(!provider.ConfigurationType.AllowsPlatformAccount(), NotificationConfigurationErrors.CredentialsNotApplicable);
        var credentials = check.Validate(provider.Code, command.Credentials);
        await secrets.StoreAsync(NotificationCredentialReference.ForPlatform(provider.Code), credentials, cancellationToken);
    }
}

/// <summary>Les champs saisis sont EXACTEMENT ceux de l'intégration, non vides : rien d'inattendu n'entre au coffre.</summary>
public sealed class CredentialsCheck(INotificationSenderResolver resolver)
{
    public const int MaxValueLength = 8192;

    public IReadOnlyDictionary<string, string> Validate(NotificationProviderCode provider, IReadOnlyDictionary<string, string>? credentials)
    {
        var integration = resolver.FindIntegration(provider) ?? throw new DomainException(NotificationsApplicationErrors.IntegrationMissing);
        var expected = integration.CredentialFields.ToHashSet(StringComparer.Ordinal);
        DomainException.ThrowIf(
            credentials is null
            || credentials.Count != expected.Count
            || credentials.Any(pair => !expected.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > MaxValueLength),
            NotificationsApplicationErrors.CredentialsIncomplete);
        return credentials!;
    }
}
