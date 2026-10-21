using Integrations.Application.Abstractions;
using Integrations.Application.Webhooks;
using Integrations.Domain;

namespace Integrations.Application.Commands;

// Endpoints de webhooks du restaurant du contexte. Le secret de signature est créé ici et montré UNE seule fois (à la
// création et à chaque rotation) ; il est ensuite seulement utilisé pour signer, jamais relu par une route.

/// <summary>Un endpoint créé (ou son secret renouvelé) : le secret en clair, à recopier maintenant, ne sera plus affiché.</summary>
public sealed record WebhookSecretIssued(Guid EndpointId, string Secret);

public sealed record CreateWebhookEndpointCommand(string? Url, IReadOnlyList<string>? Events) : ICommand<WebhookSecretIssued>;

public sealed class CreateWebhookEndpointHandler(IntegrationsWorkspace workspace, IClock clock) : ICommandHandler<CreateWebhookEndpointCommand, WebhookSecretIssued>
{
    public async Task<WebhookSecretIssued> HandleAsync(CreateWebhookEndpointCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var url = WebhookUrl.Create(command.Url);
        var events = (command.Events ?? Array.Empty<string>()).Select(WebhookEvents.Require).ToList();
        var (reference, secret) = workspace.Secrets.Create(tenant);
        var endpoint = WebhookEndpoint.Create(tenant, url, events, reference, clock, workspace.Actor);
        workspace.Endpoints.Add(endpoint);
        await workspace.SaveAsync(cancellationToken);
        return new WebhookSecretIssued(endpoint.Id.Value, secret);
    }
}

public sealed record SubscribeWebhookEventCommand(Guid EndpointId, string? EventType) : ICommand;

public sealed class SubscribeWebhookEventHandler(IntegrationsWorkspace workspace) : ICommandHandler<SubscribeWebhookEventCommand>
{
    public async Task HandleAsync(SubscribeWebhookEventCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var eventType = WebhookEvents.Require(command.EventType);
        (await workspace.LoadEndpointAsync(command.EndpointId, cancellationToken)).Subscribe(eventType);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record UnsubscribeWebhookEventCommand(Guid EndpointId, string? EventType) : ICommand;

public sealed class UnsubscribeWebhookEventHandler(IntegrationsWorkspace workspace) : ICommandHandler<UnsubscribeWebhookEventCommand>
{
    public async Task HandleAsync(UnsubscribeWebhookEventCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var eventType = EventTypeCode.Create(command.EventType);
        (await workspace.LoadEndpointAsync(command.EndpointId, cancellationToken)).Unsubscribe(eventType);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Désactive (les livraisons en attente sont abandonnées à leur prochaine échéance) ou réactive un endpoint.</summary>
public sealed record SetWebhookEndpointEnabledCommand(Guid EndpointId, bool Enabled) : ICommand;

public sealed class SetWebhookEndpointEnabledHandler(IntegrationsWorkspace workspace, IClock clock) : ICommandHandler<SetWebhookEndpointEnabledCommand>
{
    public async Task HandleAsync(SetWebhookEndpointEnabledCommand command, CancellationToken cancellationToken = default)
    {
        // Désactiver reste permis à un restaurant suspendu (couper un envoi n'est jamais refusé) ; réactiver, non.
        if (command.Enabled)
        {
            await workspace.RequireWritableTenantAsync(cancellationToken);
        }

        var endpoint = await workspace.LoadEndpointAsync(command.EndpointId, cancellationToken);
        if (command.Enabled)
        {
            endpoint.Enable(clock, workspace.Actor);
        }
        else
        {
            endpoint.Disable(clock, workspace.Actor);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Nouveau secret de signature, effectif immédiatement. Par défaut, l'ancien signe encore pendant
/// <c>SecretGracePeriod</c> (deux signatures par envoi) : le destinataire a le temps de changer de secret.
/// <paramref name="Immediate"/> : l'ancien est détruit aussitôt (secret compromis).
/// </summary>
public sealed record RotateWebhookSecretCommand(Guid EndpointId, bool Immediate = false) : ICommand<WebhookSecretIssued>;

public sealed class RotateWebhookSecretHandler(IntegrationsWorkspace workspace, IClock clock) : ICommandHandler<RotateWebhookSecretCommand, WebhookSecretIssued>
{
    public async Task<WebhookSecretIssued> HandleAsync(RotateWebhookSecretCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var endpoint = await workspace.LoadEndpointAsync(command.EndpointId, cancellationToken);
        var current = endpoint.SecretReference;
        // Un seul ancien secret à la fois : celui d'une rotation précédente encore en grâce disparaît maintenant.
        var olderPrevious = endpoint.PreviousSecretReference;
        var grace = command.Immediate ? TimeSpan.Zero : workspace.Settings.SecretGracePeriod;
        var (reference, secret) = workspace.Secrets.Create(endpoint.TenantId);
        endpoint.RotateSecretReference(reference, clock, grace, workspace.Actor);
        if (olderPrevious is not null)
        {
            await workspace.Secrets.DeleteAsync(endpoint.TenantId, olderPrevious, cancellationToken);
        }

        if (grace == TimeSpan.Zero)
        {
            await workspace.Secrets.DeleteAsync(endpoint.TenantId, current, cancellationToken);
        }

        await workspace.SaveAsync(cancellationToken);
        return new WebhookSecretIssued(endpoint.Id.Value, secret);
    }
}

/// <summary>Job : la période de grâce de l'ancien secret est finie, il est oublié et supprimé du coffre.</summary>
public sealed record ExpirePreviousWebhookSecretCommand(Guid EndpointId) : ICommand<bool>;

public sealed class ExpirePreviousWebhookSecretHandler(IntegrationsWorkspace workspace, IClock clock) : ICommandHandler<ExpirePreviousWebhookSecretCommand, bool>
{
    public async Task<bool> HandleAsync(ExpirePreviousWebhookSecretCommand command, CancellationToken cancellationToken = default)
    {
        var endpoint = command.EndpointId == Guid.Empty
            ? null
            : await workspace.Endpoints.FindAsync(workspace.CurrentTenant, WebhookEndpointId.From(command.EndpointId), cancellationToken);
        if (endpoint?.ForgetExpiredPreviousSecret(clock) is not { } expired)
        {
            return false;
        }

        await workspace.Secrets.DeleteAsync(endpoint.TenantId, expired, cancellationToken);
        await workspace.SaveAsync(cancellationToken);
        return true;
    }
}

/// <summary>Résultat d'un envoi de test : l'id de l'événement factice, et l'issue telle que le job l'enregistrerait.</summary>
public sealed record WebhookTestResult(Guid EventId, bool Succeeded, string? FailureReason);

/// <summary>
/// Envoi de test : un événement <c>integrations.ping</c> signé comme un vrai (ancien secret compris pendant la grâce),
/// envoyé tout de suite, sans livraison enregistrée ni nouvelle tentative. Permis sur un endpoint désactivé (vérifier
/// avant de le réactiver). Même protection réseau que les vrais envois.
/// </summary>
public sealed record SendWebhookTestCommand(Guid EndpointId) : ICommand<WebhookTestResult>;

public sealed class SendWebhookTestHandler(IntegrationsWorkspace workspace, IWebhookSender sender, IClock clock)
    : ICommandHandler<SendWebhookTestCommand, WebhookTestResult>
{
    public async Task<WebhookTestResult> HandleAsync(SendWebhookTestCommand command, CancellationToken cancellationToken = default)
    {
        var endpoint = await workspace.LoadEndpointAsync(command.EndpointId, cancellationToken);
        var now = clock.UtcNow;
        var secrets = await WebhookSecrets.RevealAsync(workspace, endpoint, now, cancellationToken);
        var eventId = Guid.NewGuid();
        if (secrets.Count == 0)
        {
            return new WebhookTestResult(eventId, false, AttemptWebhookDeliveryHandler.SecretMissing);
        }

        var data = new System.Text.Json.Nodes.JsonObject { ["endpoint_id"] = endpoint.Id.Value.ToString("D") }.ToJsonString();
        var body = WebhookEnvelope.Build(eventId, WebhookEvents.Ping, now, data);
        var headers = WebhookSignature.Headers(secrets, eventId.ToString("D"), now, body);
        var result = await sender.SendAsync(new WebhookRequest(new Uri(endpoint.Url.Value), body, headers), cancellationToken);
        return new WebhookTestResult(eventId, result.Succeeded, result.FailureReason);
    }
}

/// <summary>Les secrets qui signent un envoi à cet instant : l'actuel, puis l'ancien s'il est encore en grâce.</summary>
internal static class WebhookSecrets
{
    public static async Task<IReadOnlyList<string>> RevealAsync(
        IntegrationsWorkspace workspace, WebhookEndpoint endpoint, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var secrets = new List<string>(2);
        if (await workspace.Secrets.RevealAsync(endpoint.TenantId, endpoint.SecretReference, cancellationToken) is not { } current)
        {
            return secrets;
        }

        secrets.Add(current);
        if (endpoint.PreviousSecretActiveAt(nowUtc) is { } previous
            && await workspace.Secrets.RevealAsync(endpoint.TenantId, previous, cancellationToken) is { } old)
        {
            secrets.Add(old);
        }

        return secrets;
    }
}
