using Integrations.Application.Abstractions;
using Integrations.Application.Webhooks;
using Integrations.Domain;

namespace Integrations.Application.Commands;

/// <summary>
/// Un événement d'un autre module (traduit par l'hôte, par l'outbox) devient une livraison pour chaque endpoint ACTIF du
/// restaurant du CONTEXTE abonné à son type. <paramref name="EventId"/> est stable pour un même message : un rejeu ne
/// crée rien de plus (vérification puis index unique). Renvoie le nombre de livraisons créées.
/// </summary>
/// <param name="DataJson">Les données publiques de l'événement : un objet JSON, sans donnée personnelle.</param>
public sealed record PublishWebhookEventCommand(string EventType, Guid EventId, DateTimeOffset OccurredAtUtc, string DataJson) : ICommand<int>;

public sealed class PublishWebhookEventHandler(IntegrationsWorkspace workspace, IClock clock) : ICommandHandler<PublishWebhookEventCommand, int>
{
    public async Task<int> HandleAsync(PublishWebhookEventCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var eventType = WebhookEvents.Require(command.EventType);
        var eventId = EventId.From(command.EventId);
        var endpoints = (await workspace.Endpoints.ListActiveAsync(tenant, cancellationToken)).Where(endpoint => endpoint.ShouldReceive(eventType)).ToList();
        if (endpoints.Count == 0)
        {
            return 0;
        }

        var payload = WebhookPayloadSnapshot.Create(
            eventType, WebhookEvents.SchemaVersion, WebhookEnvelope.Build(eventId.Value, eventType.Value, command.OccurredAtUtc, command.DataJson));
        var created = 0;
        foreach (var endpoint in endpoints)
        {
            if (await workspace.Deliveries.HasLiveDeliveryAsync(tenant, endpoint.Id, eventId, cancellationToken))
            {
                continue;
            }

            workspace.Deliveries.Add(WebhookDelivery.CreateFor(endpoint, eventId, payload, clock, workspace.Settings.RetryPolicy));
            created++;
        }

        if (created > 0)
        {
            await workspace.SaveAsync(cancellationToken);
        }

        return created;
    }
}

public enum AttemptOutcome
{
    Nothing = 1,
    Delivered = 2,
    Failed = 3,
    DeadLettered = 4,
}

/// <summary>
/// Job : une tentative de livraison due, pour le tenant du contexte. La tentative est enregistrée AVANT l'envoi (deux
/// workers : le second échoue sur la version). L'en-tête <c>webhook-id</c> est l'id de l'événement, identique à chaque
/// tentative : le destinataire dédoublonne. Un endpoint désactivé entre-temps : la livraison est abandonnée.
/// </summary>
public sealed record AttemptWebhookDeliveryCommand(Guid DeliveryId) : ICommand<AttemptOutcome>;

public sealed class AttemptWebhookDeliveryHandler(IntegrationsWorkspace workspace, IWebhookSender sender, IClock clock)
    : ICommandHandler<AttemptWebhookDeliveryCommand, AttemptOutcome>
{
    public const string EndpointDisabled = "endpoint_disabled";
    public const string SecretMissing = "secret_missing";

    public async Task<AttemptOutcome> HandleAsync(AttemptWebhookDeliveryCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var delivery = command.DeliveryId == Guid.Empty
            ? null
            : await workspace.Deliveries.FindAsync(tenant, DeliveryId.From(command.DeliveryId), cancellationToken);
        if (delivery is null || !delivery.IsDueAt(clock.UtcNow))
        {
            return AttemptOutcome.Nothing;
        }

        var endpoint = await workspace.Endpoints.FindAsync(tenant, delivery.EndpointId, cancellationToken);
        if (endpoint is not { Status: WebhookEndpointStatus.Active })
        {
            delivery.DeadLetter(EndpointDisabled, clock);
            await workspace.SaveAsync(cancellationToken);
            return AttemptOutcome.DeadLettered;
        }

        var secrets = await WebhookSecrets.RevealAsync(workspace, endpoint, clock.UtcNow, cancellationToken);
        if (secrets.Count == 0)
        {
            delivery.DeadLetter(SecretMissing, clock);
            await workspace.SaveAsync(cancellationToken);
            return AttemptOutcome.DeadLettered;
        }

        delivery.StartAttempt(clock);
        await workspace.SaveAsync(cancellationToken);

        var body = delivery.Payload.Payload;
        var headers = WebhookSignature.Headers(secrets, delivery.EventId.Value.ToString("D"), clock.UtcNow, body);
        var result = await sender.SendAsync(new WebhookRequest(new Uri(endpoint.Url.Value), body, headers), cancellationToken);
        if (result.Succeeded)
        {
            delivery.Succeed(clock);
        }
        else
        {
            delivery.FailAndScheduleRetry(result.FailureReason ?? "failed", clock);
        }

        await workspace.SaveAsync(cancellationToken);
        var outcome = delivery.Status switch
        {
            WebhookDeliveryStatus.Succeeded => AttemptOutcome.Delivered,
            WebhookDeliveryStatus.DeadLetter => AttemptOutcome.DeadLettered,
            _ => AttemptOutcome.Failed,
        };
        await RecordEndpointHealthAsync(endpoint, outcome, cancellationToken);
        return outcome;
    }

    /// <summary>
    /// Santé de l'endpoint, écrite APRÈS l'issue de la livraison (déjà enregistrée) : une réussite remet le compteur
    /// d'abandons à zéro, un abandon après toutes les tentatives l'incrémente et peut désactiver l'endpoint. Un conflit
    /// de version (autre instance) n'annule rien : le compteur est seulement approximatif.
    /// </summary>
    private async Task RecordEndpointHealthAsync(WebhookEndpoint endpoint, AttemptOutcome outcome, CancellationToken cancellationToken)
    {
        switch (outcome)
        {
            case AttemptOutcome.Delivered when endpoint.ConsecutiveDeadLetters > 0:
                endpoint.RecordDeliverySucceeded();
                break;
            case AttemptOutcome.DeadLettered:
                endpoint.RecordDeliveryAbandoned(workspace.Settings.AutoDisableAfterDeadLetters, clock);
                break;
            default:
                return;
        }

        try
        {
            await workspace.SaveAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            // L'endpoint a changé entre-temps (désactivé, réactivé, autre livraison) : on laisse ce changement-là.
        }
    }
}

/// <summary>Job : une tentative jamais close (worker arrêté en plein envoi) est close en échec « timeout ».</summary>
public sealed record RecoverStuckDeliveryCommand(Guid DeliveryId) : ICommand<bool>;

public sealed class RecoverStuckDeliveryHandler(IntegrationsWorkspace workspace, IClock clock) : ICommandHandler<RecoverStuckDeliveryCommand, bool>
{
    public const string Timeout = "timeout";

    public async Task<bool> HandleAsync(RecoverStuckDeliveryCommand command, CancellationToken cancellationToken = default)
    {
        var delivery = command.DeliveryId == Guid.Empty
            ? null
            : await workspace.Deliveries.FindAsync(workspace.CurrentTenant, DeliveryId.From(command.DeliveryId), cancellationToken);
        if (delivery is not { CurrentAttemptStartedAtUtc: { } startedAt } || startedAt + workspace.Settings.StuckAttemptAfter > clock.UtcNow)
        {
            return false;
        }

        delivery.FailAndScheduleRetry(Timeout, clock);
        await workspace.SaveAsync(cancellationToken);
        return true;
    }
}

/// <summary>
/// Renvoie un événement abandonné : une NOUVELLE livraison du même événement (même id, même corps), si l'endpoint est
/// actif et toujours abonné. Renvoie l'id de la nouvelle livraison.
/// </summary>
public sealed record RetryDeadLetteredDeliveryCommand(Guid DeliveryId) : ICommand<Guid>;

public sealed class RetryDeadLetteredDeliveryHandler(IntegrationsWorkspace workspace, IClock clock) : ICommandHandler<RetryDeadLetteredDeliveryCommand, Guid>
{
    public async Task<Guid> HandleAsync(RetryDeadLetteredDeliveryCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var delivery = await workspace.LoadDeliveryAsync(command.DeliveryId, cancellationToken);
        DomainException.ThrowIf(delivery.Status != WebhookDeliveryStatus.DeadLetter, IntegrationsApplicationErrors.NotDeadLettered);
        var endpoint = await workspace.Endpoints.FindAsync(delivery.TenantId, delivery.EndpointId, cancellationToken)
            ?? throw new NotFoundException(IntegrationsApplicationErrors.EndpointNotFound);
        DomainException.ThrowIf(
            await workspace.Deliveries.HasLiveDeliveryAsync(delivery.TenantId, endpoint.Id, delivery.EventId, cancellationToken),
            IntegrationsApplicationErrors.NotDeadLettered);

        var retry = WebhookDelivery.CreateFor(endpoint, delivery.EventId, delivery.Payload, clock, workspace.Settings.RetryPolicy);
        workspace.Deliveries.Add(retry);
        await workspace.SaveAsync(cancellationToken);
        return retry.Id.Value;
    }
}
