using Notifications.Application.Abstractions;
using Notifications.Domain;

namespace Notifications.Application.Sending;

/// <summary>Le prestataire retenu et, pour un restaurant, sa configuration (mode, identifiants).</summary>
public sealed record NotificationRoute(NotificationProviderCode Provider, RestaurantNotificationConfiguration? Configuration);

/// <summary>
/// Choisit le prestataire d'un envoi (Provider Resolver, § 16) : pour un restaurant, ses configurations ACTIVES du canal
/// qui servent l'établissement, dont le prestataire est encore proposé par la plateforme, autorise le mode, délivre dans
/// le pays de l'établissement, accepte la longueur du contenu et a une intégration dans le code ; la plus prioritaire
/// dans l'établissement d'abord, puis la plus ancienne. Pour la plateforme, son premier prestataire du canal qui accepte
/// un compte de la plateforme. In-app : toujours le prestataire interne.
/// </summary>
public sealed class NotificationRouting(
    INotificationProviderRepository providers,
    IRestaurantNotificationConfigurationRepository configurations,
    INotificationSenderResolver resolver)
{
    public async Task<IReadOnlyList<NotificationRoute>> CandidatesAsync(
        TenantId? tenantId, NotificationChannel channel, LocationReference? location, int contentLength, CancellationToken cancellationToken)
    {
        if (channel == NotificationChannel.InApp)
        {
            return [new NotificationRoute(NotificationProviderCode.InApp, null)];
        }

        var catalog = (await providers.ListAsync(cancellationToken)).ToDictionary(provider => provider.Id);
        bool Usable(NotificationProvider provider) =>
            provider.Channel == channel
            && provider.CanDeliverTo(location?.Country)
            && provider.Capability.Accepts(contentLength)
            && resolver.FindIntegration(provider.Code) is { } integration
            && integration.Channels.Contains(channel);

        if (tenantId is not { } tenant)
        {
            return catalog.Values
                .Where(provider => Usable(provider) && provider.ConfigurationType.AllowsPlatformAccount())
                .OrderBy(provider => provider.Name, StringComparer.Ordinal)
                .Select(provider => new NotificationRoute(provider.Code, null))
                .ToList();
        }

        var locationId = location?.LocationId;
        return (await configurations.ListForChannelAsync(tenant, channel, cancellationToken))
            .Where(configuration => configuration.CanServe(locationId)
                && catalog.TryGetValue(configuration.NotificationProviderId, out var provider)
                && Usable(provider)
                && configuration.Mode.IsAllowedBy(provider.ConfigurationType))
            .OrderBy(configuration => locationId is { } id ? configuration.PriorityAt(id) : 0)
            .ThenBy(configuration => configuration.CreatedAtUtc)
            .Select(configuration => new NotificationRoute(configuration.Provider, configuration))
            .ToList();
    }

    /// <summary>
    /// La route d'une tentative : le prestataire actuel de la notification s'il convient encore, sinon le premier qui
    /// convient (repli). Null : aucun prestataire disponible pour l'instant.
    /// </summary>
    public async Task<NotificationRoute?> ForAttemptAsync(
        Notification notification, LocationReference? location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var candidates = await CandidatesAsync(
            notification.TenantId, notification.Channel, location, notification.Content.Body.Length, cancellationToken);
        return candidates.FirstOrDefault(route => route.Provider == notification.ProviderCode) ?? candidates.FirstOrDefault();
    }
}
