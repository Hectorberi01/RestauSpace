using System.Diagnostics;
using Notifications.Application.Abstractions;
using Notifications.Domain;

namespace Notifications.Application.Sending;

/// <summary>
/// Une tentative d'envoi, de bout en bout : consentement relu (marketing), prestataire choisi (repli si le prestataire
/// actuel n'est plus disponible), tentative ouverte, appel au prestataire, issue enregistrée, relance planifiée pour un
/// échec temporaire, appareil désactivé pour un jeton push refusé, consommation comptée. Utilisée par le worker et par
/// l'OTP (envoi immédiat). Ne sauvegarde pas : l'appelant décide (le worker sauvegarde aussi juste après l'ouverture
/// de la tentative, l'OTP jamais avant l'effacement du code).
/// </summary>
public sealed class NotificationSending(
    NotificationRouting routing,
    INotificationSenderResolver resolver,
    INotificationLocations locations,
    INotificationConsents consents,
    IDeviceRegistrationRepository devices,
    INotificationUsage usage,
    IClock clock)
{
    public const string ProviderUnavailable = NotificationFailure.ProviderUnavailable;
    public const string ProviderError = "PROVIDER_ERROR";

    /// <param name="afterStart">Appelé une fois la tentative ouverte, avant l'appel au prestataire.</param>
    /// <returns>Null si rien n'a été tenté (pas à échéance, ou annulée faute de consentement).</returns>
    public async Task<NotificationSendResult?> SendAsync(
        Notification notification, Func<CancellationToken, Task>? afterStart, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!notification.IsDueAt(clock.UtcNow))
        {
            return null;
        }

        var consent = await ConsentAsync(notification, cancellationToken);
        var location = notification is { TenantId: { } tenant, LocationId: { } locationId }
            ? await locations.FindAsync(tenant, locationId, cancellationToken)
            : null;
        var route = await routing.ForAttemptAsync(notification, location, cancellationToken);
        var sender = route is null
            ? null
            : await resolver.ResolveAsync(route.Provider, notification.Channel, route.Configuration, cancellationToken);

        try
        {
            notification.StartProcessing(clock, consent, sender is null ? null : route!.Provider);
        }
        catch (DomainException exception) when (exception.Code.StartsWith("notifications.notification.consent", StringComparison.Ordinal)
            || exception.Code == NotificationErrors.CustomerRequired.Code)
        {
            // Consentement retiré depuis la création : le message ne part jamais.
            notification.Cancel(clock);
            return null;
        }

        if (afterStart is not null)
        {
            await afterStart(cancellationToken);
        }

        var delivery = notification.Deliveries.Last();
        var result = sender is null
            ? NotificationSendResult.Failed(NotificationFailure.Temporary(ProviderUnavailable))
            : await CallAsync(sender, notification, delivery.Id, cancellationToken);
        await ApplyAsync(notification, result, cancellationToken);
        return result;
    }

    private async Task<NotificationSendResult> CallAsync(
        INotificationSender sender, Notification notification, NotificationDeliveryId deliveryId, CancellationToken cancellationToken)
    {
        using var activity = NotificationsTelemetry.StartSend(notification, deliveryId);
        var started = Stopwatch.GetTimestamp();
        NotificationSendResult result;
        try
        {
            result = await sender.SendAsync(
                new NotificationSendRequest(
                    notification.Id, deliveryId, notification.TenantId, notification.TemplateKey.Value, notification.Priority,
                    notification.Recipient, notification.Content),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Ni le message ni la réponse du prestataire : ils peuvent citer l'adresse ou un secret.
            NotificationsTelemetry.RecordProviderError(sender.Provider, sender.Channel);
            activity?.SetStatus(ActivityStatusCode.Error, ProviderError);
            result = NotificationSendResult.Failed(NotificationFailure.Temporary(ProviderError));
        }

        var status = result switch
        {
            { IsDelivered: true } => "delivered",
            { IsAccepted: true } => "sent",
            _ => "failed",
        };
        activity?.SetTag("notification.status", status);
        NotificationsTelemetry.RecordOutcome(sender.Provider, sender.Channel, status, Stopwatch.GetElapsedTime(started));
        return result;
    }

    private async Task ApplyAsync(Notification notification, NotificationSendResult result, CancellationToken cancellationToken)
    {
        if (result.IsAccepted)
        {
            notification.MarkAsSent(clock, result.MessageId);
            if (result.IsDelivered)
            {
                notification.MarkAsDelivered(clock, result.MessageId);
            }

            if (notification.TenantId is { } tenant)
            {
                await usage.RecordAsync(tenant, notification.Channel, notification.ProviderCode, clock.UtcNow, cancellationToken);
            }

            return;
        }

        var failure = result.Failure ?? NotificationFailure.Temporary(ProviderError);
        notification.MarkAsFailed(failure, clock);
        if (notification.CanRetry)
        {
            notification.ScheduleRetry(clock);
            NotificationsTelemetry.RecordRetry(notification.ProviderCode, notification.Channel);
        }

        if (notification.Channel == NotificationChannel.Push && failure.Code == NotificationFailure.InvalidDeviceToken)
        {
            foreach (var device in await devices.ListByTokenAsync(notification.Recipient.Address, cancellationToken))
            {
                device.Deactivate(NotificationFailure.InvalidDeviceToken, clock);
            }
        }
    }

    private async Task<ConsentProof?> ConsentAsync(Notification notification, CancellationToken cancellationToken) =>
        notification.Purpose == NotificationPurpose.Marketing
        && notification.TenantId is { } tenant
        && notification.CustomerId is { } customer
        && notification.Channel.ToConsentChannel() is { } channel
            ? await consents.FindAsync(tenant, customer, channel, cancellationToken)
            : null;
}
