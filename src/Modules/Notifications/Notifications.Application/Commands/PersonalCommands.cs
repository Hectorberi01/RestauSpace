using Notifications.Application.Abstractions;
using Notifications.Domain;

namespace Notifications.Application.Commands;

// Ce qui appartient à UNE personne : ses préférences, ses appareils push, sa boîte in-app. Un utilisateur n'agit que sur
// les siens (id de l'access token) ; le personnel gère les préférences des clients de SON restaurant.

/// <summary>À qui s'applique un choix de préférence : un client du restaurant (personnel) ou l'utilisateur connecté.</summary>
public sealed record PreferenceSubject(Guid? CustomerId);

/// <summary>Opt-in ou opt-out d'une finalité sur un canal (§ 19). La source est un code court, jamais une phrase.</summary>
public sealed record SetNotificationPreferenceCommand(
    PreferenceSubject Subject, NotificationPurpose Purpose, NotificationChannel Channel, bool IsAllowed, string? Source = null) : ICommand;

public sealed class SetNotificationPreferenceHandler(
    NotificationsWorkspace workspace, INotificationContacts contacts, INotificationsCurrentUser currentUser, IClock clock)
    : ICommandHandler<SetNotificationPreferenceCommand>
{
    public const string DefaultSource = "back-office";

    public async Task HandleAsync(SetNotificationPreferenceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var owner = await PreferenceOwners.ResolveAsync(tenant, command.Subject, contacts, currentUser, cancellationToken);
        var source = PreferenceSource.Create(string.IsNullOrWhiteSpace(command.Source) ? DefaultSource : command.Source);
        var preference = await workspace.Preferences.FindAsync(tenant, owner, cancellationToken);
        if (preference is null)
        {
            preference = NotificationPreference.Create(tenant, owner, isFirstForOwner: true, clock);
            workspace.Preferences.Add(preference);
        }

        // Un désabonnement n'est jamais bloqué, même pour un restaurant suspendu.
        if (command.IsAllowed)
        {
            preference.OptIn(command.Purpose, command.Channel, source, clock);
        }
        else
        {
            preference.OptOut(command.Purpose, command.Channel, source, clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>L'utilisateur connecté coupe ou rétablit un type d'alerte d'équipe (<c>staff.*</c>), tous canaux confondus.</summary>
public sealed record SetAlertTypeCommand(string Type, bool IsEnabled, string? Source = null) : ICommand;

public sealed class SetAlertTypeHandler(NotificationsWorkspace workspace, INotificationsCurrentUser currentUser, IClock clock)
    : ICommandHandler<SetAlertTypeCommand>
{
    public async Task HandleAsync(SetAlertTypeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var owner = RecipientId.ForUser(currentUser.UserId ?? throw new ForbiddenException(NotificationsApplicationErrors.UserRequired));
        var type = NotificationTypes.Find(TemplateKey.Create(command.Type)) ?? throw new DomainException(NotificationPreferenceErrors.TypeNotMutable);
        var source = PreferenceSource.Create(string.IsNullOrWhiteSpace(command.Source) ? SetNotificationPreferenceHandler.DefaultSource : command.Source);
        var preference = await workspace.Preferences.FindAsync(tenant, owner, cancellationToken);
        if (preference is null)
        {
            preference = NotificationPreference.Create(tenant, owner, isFirstForOwner: true, clock);
            workspace.Preferences.Add(preference);
        }

        preference.SetTypeMuted(type, !command.IsEnabled, source, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

internal static class PreferenceOwners
{
    public static async Task<RecipientId> ResolveAsync(
        TenantId tenant, PreferenceSubject subject, INotificationContacts contacts, INotificationsCurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (subject.CustomerId is { } customerGuid)
        {
            var customer = CustomerId.From(customerGuid);
            return await contacts.FindCustomerAsync(tenant, customer, cancellationToken) is null
                ? throw new NotFoundException(NotificationsApplicationErrors.CustomerNotFound)
                : RecipientId.ForCustomer(customer);
        }

        return RecipientId.ForUser(currentUser.UserId ?? throw new ForbiddenException(NotificationsApplicationErrors.UserRequired));
    }
}

/// <summary>
/// Enregistre (ou rafraîchit) l'appareil push de l'utilisateur connecté (§ 23). Un jeton repris par un autre compte
/// (appareil changé de main) est retiré de l'ancien.
/// </summary>
public sealed record RegisterDeviceCommand(string DeviceId, DevicePlatform Platform, string PushToken) : ICommand<Guid>;

public sealed class RegisterDeviceHandler(
    ITenantContext tenantContext, INotificationsCurrentUser currentUser, IDeviceRegistrationRepository devices, INotificationsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<RegisterDeviceCommand, Guid>
{
    public const string TokenMoved = "token_moved";

    public async Task<Guid> HandleAsync(RegisterDeviceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = currentUser.UserId ?? throw new ForbiddenException(NotificationsApplicationErrors.UserRequired);
        var tenant = tenantContext.CurrentTenantId;
        var device = await devices.FindAsync(user, command.DeviceId?.Trim() ?? string.Empty, cancellationToken);
        if (device is null)
        {
            device = DeviceRegistration.Register(user, tenant, command.DeviceId, command.Platform, command.PushToken, clock);
            devices.Add(device);
        }
        else
        {
            device.Refresh(command.PushToken, tenant, clock);
        }

        foreach (var other in await devices.ListByTokenAsync(device.PushToken, cancellationToken))
        {
            if (other.Id != device.Id)
            {
                other.Deactivate(TokenMoved, clock);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return device.Id.Value;
    }
}

public sealed record UnregisterDeviceCommand(string DeviceId) : ICommand;

public sealed class UnregisterDeviceHandler(
    INotificationsCurrentUser currentUser, IDeviceRegistrationRepository devices, INotificationsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<UnregisterDeviceCommand>
{
    public const string UserRequest = "user_request";

    public async Task HandleAsync(UnregisterDeviceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = currentUser.UserId ?? throw new ForbiddenException(NotificationsApplicationErrors.UserRequired);
        var device = await devices.FindAsync(user, command.DeviceId?.Trim() ?? string.Empty, cancellationToken)
            ?? throw new NotFoundException(NotificationsApplicationErrors.DeviceNotFound);
        device.Deactivate(UserRequest, clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>In-app (§ 24) : l'utilisateur connecté lit ou range une notification de SA boîte dans ce restaurant.</summary>
public sealed record ChangeInAppNotificationCommand(Guid NotificationId, bool Archive) : ICommand;

public sealed class ChangeInAppNotificationHandler(NotificationsWorkspace workspace, INotificationsCurrentUser currentUser, IClock clock)
    : ICommandHandler<ChangeInAppNotificationCommand>
{
    public async Task HandleAsync(ChangeInAppNotificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = currentUser.UserId ?? throw new ForbiddenException(NotificationsApplicationErrors.UserRequired);
        var notification = await workspace.LoadNotificationAsync(workspace.CurrentTenant, command.NotificationId, cancellationToken);

        // La notification d'un autre utilisateur n'existe pas pour celui-ci.
        if (notification.Channel != NotificationChannel.InApp || notification.RecipientId != RecipientId.ForUser(user))
        {
            throw new NotFoundException(NotificationsApplicationErrors.NotificationNotFound);
        }

        if (command.Archive)
        {
            notification.Archive(clock);
        }
        else
        {
            notification.MarkAsRead(clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Ce que montre la page de désinscription : l'enseigne du restaurant et le canal concerné.</summary>
public sealed record UnsubscribeLinkDto(string Restaurant, NotificationChannel Channel);

/// <summary>
/// Lecture d'un lien de désinscription, SANS effet (un antivirus de messagerie ouvre les liens) : la page affiche ce
/// qui va être refusé et demande une confirmation. Un jeton invalide répond comme un lien inconnu.
/// </summary>
public sealed record GetUnsubscribeLinkQuery(string? Token) : IQuery<UnsubscribeLinkDto>;

public sealed class GetUnsubscribeLinkHandler(IUnsubscribeLinks links, INotificationTenants tenants)
    : IQueryHandler<GetUnsubscribeLinkQuery, UnsubscribeLinkDto>
{
    public async Task<UnsubscribeLinkDto> HandleAsync(GetUnsubscribeLinkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var claim = links.Read(query.Token) ?? throw new NotFoundException(NotificationsApplicationErrors.UnsubscribeLinkInvalid);
        var name = await tenants.DisplayNameAsync(claim.TenantId, cancellationToken)
            ?? throw new NotFoundException(NotificationsApplicationErrors.UnsubscribeLinkInvalid);
        return new UnsubscribeLinkDto(name, claim.Channel);
    }
}

/// <summary>
/// Le client confirme sa désinscription : plus aucun message marketing de ce restaurant sur ce canal (source
/// <c>unsubscribe-link</c>). Le tenant et le client viennent du jeton signé, jamais de la requête. Jamais bloqué, même
/// pour un restaurant suspendu ; confirmer deux fois ne change rien.
/// </summary>
public sealed record UnsubscribeCommand(string? Token) : ICommand<UnsubscribeLinkDto>;

public sealed class UnsubscribeHandler(
    IUnsubscribeLinks links, INotificationTenants tenants, INotificationPreferenceRepository preferences, INotificationsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<UnsubscribeCommand, UnsubscribeLinkDto>
{
    public const string Source = "unsubscribe-link";

    public async Task<UnsubscribeLinkDto> HandleAsync(UnsubscribeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var claim = links.Read(command.Token) ?? throw new NotFoundException(NotificationsApplicationErrors.UnsubscribeLinkInvalid);
        var name = await tenants.DisplayNameAsync(claim.TenantId, cancellationToken)
            ?? throw new NotFoundException(NotificationsApplicationErrors.UnsubscribeLinkInvalid);

        var owner = RecipientId.ForCustomer(claim.CustomerId);
        var preference = await preferences.FindForSignedLinkAsync(claim.TenantId, owner, cancellationToken);
        if (preference is null)
        {
            preference = NotificationPreference.Create(claim.TenantId, owner, isFirstForOwner: true, clock);
            preferences.Add(preference);
        }

        preference.OptOut(NotificationPurpose.Marketing, claim.Channel, PreferenceSource.Create(Source), clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new UnsubscribeLinkDto(name, claim.Channel);
    }
}
