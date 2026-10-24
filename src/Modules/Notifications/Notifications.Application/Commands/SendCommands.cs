using Notifications.Application.Abstractions;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application.Commands;

/// <summary>Résultat d'une demande d'envoi : les notifications créées, ou pourquoi rien ne part.</summary>
public sealed record SendNotificationResult(IReadOnlyList<Guid> NotificationIds, NotificationSkipReason? Skipped);

/// <summary>
/// Le personnel envoie une notification d'un type du catalogue (§ 35). Le destinataire est un CLIENT du restaurant (son
/// adresse est lue dans Customers pour le canal), un UTILISATEUR (in-app, ou push vers chacun de ses appareils actifs),
/// ou une adresse saisie (invité sans fiche, transactionnel seulement). Les variables sont rendues dans le template du
/// restaurant ou de la plateforme ; la politique d'envoi peut écarter la demande (raison renvoyée).
/// </summary>
/// <param name="IdempotencyKey">Rejouer la même demande ne crée rien de plus.</param>
/// <param name="Anonymous">Déclenchée par un visiteur du site, jamais par le personnel (posé par l'hôte, pas par l'API).</param>
public sealed record SendNotificationCommand(
    string Type,
    NotificationChannel Channel,
    Guid? CustomerId = null,
    Guid? UserId = null,
    string? Address = null,
    Guid? LocationId = null,
    string? Language = null,
    IReadOnlyDictionary<string, string>? Variables = null,
    DateTimeOffset? ScheduledAtUtc = null,
    string? IdempotencyKey = null,
    bool Anonymous = false) : ICommand<SendNotificationResult>;

public sealed class SendNotificationHandler(NotificationsWorkspace workspace, NotificationRequests requests)
    : ICommandHandler<SendNotificationCommand, SendNotificationResult>
{
    public async Task<SendNotificationResult> HandleAsync(SendNotificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var result = await requests.CreateAsync(workspace.CurrentTenant, command, cancellationToken);
        if (result.NotificationIds.Count > 0)
        {
            await workspace.SaveAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>Une intention de notification produite par un événement d'un autre module (§ 17).</summary>
/// <param name="Channels">Canaux à essayer DANS L'ORDRE : le premier que la politique accepte est le seul utilisé.</param>
/// <param name="IdempotencyKey">Clé stable choisie par l'appelant (un rappel annulable plus tard) ; sinon dérivée de l'événement.</param>
/// <param name="Anonymous">L'événement vient d'une action d'un visiteur du site (commande en ligne...).</param>
public sealed record NotificationTrigger(
    string Type,
    IReadOnlyList<NotificationChannel> Channels,
    Guid? CustomerId,
    Guid? UserId,
    Guid? LocationId,
    IReadOnlyDictionary<string, string> Variables,
    DateTimeOffset? ScheduledAtUtc = null,
    string? IdempotencyKey = null,
    bool Anonymous = false);

/// <summary>
/// Traduit un événement d'intégration en notifications (Transactional Inbox, § 27-28) : le même événement reçu deux fois
/// (livraison « au moins une fois ») ne crée rien la seconde fois, grâce à (consommateur, id d'événement) écrit dans la
/// même transaction que les notifications. Agit pour le tenant de l'événement (contexte d'arrière-plan).
/// </summary>
public sealed record TriggerNotificationsCommand(
    string Consumer, string EventId, string EventType, IReadOnlyList<NotificationTrigger> Triggers) : ICommand<int>;

public sealed class TriggerNotificationsHandler(
    NotificationsWorkspace workspace, NotificationRequests requests, INotificationInbox inbox, IClock clock)
    : ICommandHandler<TriggerNotificationsCommand, int>
{
    public async Task<int> HandleAsync(TriggerNotificationsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        if (await inbox.HasProcessedAsync(command.Consumer, command.EventId, cancellationToken))
        {
            return 0;
        }

        var created = 0;
        foreach (var (trigger, index) in command.Triggers.Select((item, position) => (item, position)))
        {
            foreach (var channel in trigger.Channels)
            {
                var result = await requests.CreateAsync(
                    tenant,
                    new SendNotificationCommand(
                        trigger.Type, channel, trigger.CustomerId, trigger.UserId, null, trigger.LocationId, null, trigger.Variables,
                        trigger.ScheduledAtUtc, trigger.IdempotencyKey ?? $"{command.Consumer}:{command.EventId}:{index}", trigger.Anonymous),
                    cancellationToken);
                if (result.NotificationIds.Count > 0)
                {
                    created += result.NotificationIds.Count;
                    break;
                }
            }
        }

        inbox.MarkProcessed(command.Consumer, command.EventId, command.EventType, clock.UtcNow);
        try
        {
            await workspace.SaveAsync(cancellationToken);
        }
        catch (DomainException exception) when (exception.Code == NotificationsApplicationErrors.DuplicateEvent.Code
            || exception.Code == NotificationsApplicationErrors.DuplicateRequest.Code)
        {
            // Le même événement traité au même moment par une autre instance : elle a déjà tout créé.
            return 0;
        }

        return created;
    }
}

/// <summary>
/// Transforme une demande (personnel, événement) en notifications décidées par la politique d'envoi, AJOUTÉES au
/// repository mais pas sauvegardées. Destinataire : client (adresse lue dans Customers), utilisateur (in-app, ou push
/// vers chacun de ses appareils actifs) ou adresse saisie.
/// </summary>
public sealed class NotificationRequests(
    INotificationRepository notifications,
    INotificationLocations locations,
    INotificationContacts contacts,
    IDeviceRegistrationRepository devices,
    NotificationPolicy policy,
    NotificationsSettings settings,
    IUnsubscribeLinks unsubscribeLinks)
{
    public async Task<SendNotificationResult> CreateAsync(TenantId tenant, SendNotificationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var type = NotificationTypes.Require(TemplateKey.Create(command.Type));
        NotificationChannels.Require(command.Channel);
        var location = await LocationAsync(tenant, command.LocationId, cancellationToken);
        var (recipients, recipientId, contact) = await RecipientsAsync(tenant, command, cancellationToken);
        if (recipients.Count == 0)
        {
            return new SendNotificationResult([], NotificationSkipReason.NoContact);
        }

        var variables = new Dictionary<string, string>(command.Variables ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        if (contact?.FirstName is { } firstName && type.Allows("customer.firstName"))
        {
            variables.TryAdd("customer.firstName", firstName);
        }

        // Le lien de désinscription est toujours produit ici, jamais repris de la demande.
        variables.Remove(NotificationTypes.UnsubscribeUrl);
        if (type.Allows(NotificationTypes.UnsubscribeUrl) && UnsubscribeUrl(tenant, recipientId, command.Channel) is { } unsubscribe)
        {
            variables[NotificationTypes.UnsubscribeUrl] = unsubscribe;
        }

        var payload = NotificationPayload.Create(variables);
        var language = NotificationLanguage.Create(command.Language ?? contact?.Language ?? settings.DefaultLanguage);
        var ids = new List<Guid>();
        NotificationSkipReason? skipped = null;
        foreach (var (recipient, index) in recipients.Select((item, position) => (item, position)))
        {
            var key = command.IdempotencyKey is { } baseKey ? (recipients.Count == 1 ? baseKey : $"{baseKey}:{index}") : null;
            var decision = await policy.DecideAsync(
                new NotificationIntent(tenant, type, recipient, recipientId, location, language, payload, command.ScheduledAtUtc, key, command.Anonymous),
                cancellationToken);
            if (decision.Notification is { } notification)
            {
                notifications.Add(notification, key);
                ids.Add(notification.Id.Value);
            }
            else
            {
                skipped ??= decision.Skipped;
            }
        }

        return new SendNotificationResult(ids, ids.Count == 0 ? skipped : null);
    }

    private string? UnsubscribeUrl(TenantId tenant, RecipientId? recipient, NotificationChannel channel)
    {
        var page = settings.UnsubscribePageUrl?.Trim();
        if (string.IsNullOrEmpty(page) || recipient?.ToCustomerId() is not { } customer || channel.ToConsentChannel() is null)
        {
            return null;
        }

        var token = unsubscribeLinks.Issue(new UnsubscribeClaim(tenant, customer, channel));
        return $"{page}{(page.Contains('?', StringComparison.Ordinal) ? '&' : '?')}jeton={Uri.EscapeDataString(token)}";
    }

    private async Task<LocationReference?> LocationAsync(TenantId tenant, Guid? locationId, CancellationToken cancellationToken)
    {
        if (locationId is not { } id)
        {
            return null;
        }

        return (id == Guid.Empty ? null : await locations.FindAsync(tenant, LocationId.From(id), cancellationToken))
            ?? throw new NotFoundException(NotificationsApplicationErrors.LocationNotFound);
    }

    private async Task<(IReadOnlyList<NotificationRecipient> Recipients, RecipientId? RecipientId, CustomerContact? Contact)> RecipientsAsync(
        TenantId tenant, SendNotificationCommand command, CancellationToken cancellationToken)
    {
        if (command.CustomerId is { } customerGuid)
        {
            var customer = CustomerId.From(customerGuid);
            var contact = await contacts.FindCustomerAsync(tenant, customer, cancellationToken);
            if (contact is null || contact.IsAnonymized)
            {
                throw new NotFoundException(NotificationsApplicationErrors.CustomerNotFound);
            }

            var address = command.Address is { } given
                ? NotificationRecipient.Create(command.Channel, given)
                : command.Channel switch
                {
                    NotificationChannel.Email when contact.Email is { } email => NotificationRecipient.ForEmail(email),
                    NotificationChannel.Sms when contact.Phone is { } phone => NotificationRecipient.ForSms(phone),
                    NotificationChannel.WhatsApp when contact.Phone is { } phone => NotificationRecipient.ForWhatsApp(phone),
                    _ => null,
                };
            return (address is null ? Array.Empty<NotificationRecipient>() : new[] { address }, RecipientId.ForCustomer(customer), contact);
        }

        if (command.UserId is { } userGuid)
        {
            var user = UserId.From(userGuid);
            var recipients = command.Channel switch
            {
                NotificationChannel.InApp => new[] { NotificationRecipient.ForInApp(user) },
                NotificationChannel.Push => (await devices.ListActiveAsync(user, cancellationToken)).Select(device => device.Recipient).ToArray(),
                _ when command.Address is { } given => new[] { NotificationRecipient.Create(command.Channel, given) },
                _ => Array.Empty<NotificationRecipient>(),
            };
            return (recipients, RecipientId.ForUser(user), null);
        }

        DomainException.ThrowIf(command.Address is null, NotificationsApplicationErrors.RecipientRequired);
        return (new[] { NotificationRecipient.Create(command.Channel, command.Address) }, null, null);
    }
}

/// <summary>
/// Un événement annule des notifications encore en attente créées plus tôt sous ces clés (le rappel d'une réservation
/// annulée). Idempotent comme <see cref="TriggerNotificationsCommand"/> ; une notification déjà partie reste partie.
/// </summary>
public sealed record CancelTriggeredNotificationsCommand(
    string Consumer, string EventId, string EventType, IReadOnlyList<string> IdempotencyKeys) : ICommand<int>;

public sealed class CancelTriggeredNotificationsHandler(
    NotificationsWorkspace workspace, INotificationInbox inbox, IClock clock) : ICommandHandler<CancelTriggeredNotificationsCommand, int>
{
    public async Task<int> HandleAsync(CancelTriggeredNotificationsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        if (await inbox.HasProcessedAsync(command.Consumer, command.EventId, cancellationToken))
        {
            return 0;
        }

        var cancelled = 0;
        foreach (var key in command.IdempotencyKeys)
        {
            if (await workspace.Notifications.FindByIdempotencyKeyAsync(tenant, key, cancellationToken) is
                { Status: NotificationStatus.Pending or NotificationStatus.Scheduled } notification)
            {
                notification.Cancel(clock);
                cancelled++;
            }
        }

        inbox.MarkProcessed(command.Consumer, command.EventId, command.EventType, clock.UtcNow);
        try
        {
            await workspace.SaveAsync(cancellationToken);
        }
        catch (DomainException exception) when (exception.Code == NotificationsApplicationErrors.DuplicateEvent.Code)
        {
            return 0;
        }

        return cancelled;
    }
}
