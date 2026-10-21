using Integrations.Application;
using Integrations.Application.Commands;
using Integrations.Application.Queries;
using Integrations.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Integrations.Api;

/// <summary>Création d'un endpoint : URL HTTPS publique et événements du catalogue.</summary>
public sealed record CreateWebhookEndpointRequest(string? Url, IReadOnlyList<string>? Events);

public sealed record SubscribeWebhookEventRequest(string? EventType);

/// <summary>Émission d'une clé : nom, scopes du catalogue, expiration facultative (UTC).</summary>
public sealed record IssueApiCredentialRequest(string? Name, IReadOnlyList<string>? Scopes, DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// Webhooks et clés d'API du restaurant sélectionné dans l'access token (jamais un tenant de requête). L'hôte chaîne
/// <c>Tenancy.TenantMemberPolicy</c> sur le groupe. Lecture : <see cref="IntegrationsPermissions.View"/> ; gestion :
/// <see cref="IntegrationsPermissions.Manage"/>. Un secret de signature ou une clé n'est renvoyé qu'UNE fois, dans la
/// réponse qui le crée ; aucune route ne le relit. Ces permissions ne sont jamais accordées à une clé d'API (l'hôte les
/// exclut du catalogue des scopes) : une clé ne peut pas créer d'autres clés.
/// </summary>
public static class IntegrationsEndpoints
{
    private const string View = IntegrationsPermissions.View;
    private const string Manage = IntegrationsPermissions.Manage;
    private const string GetEndpointRoute = "GetWebhookEndpoint";
    private const string GetDeliveryRoute = "GetWebhookDelivery";
    private const string GetCredentialRoute = "GetApiCredential";

    public static RouteGroupBuilder MapIntegrationsEndpoints(this IEndpointRouteBuilder routes)
    {
        var integrations = routes.MapGroup("/integrations").WithTags("Integrations");

        integrations.MapGet("/catalog", async (IQueryHandler<GetIntegrationsCatalogQuery, IntegrationsCatalogDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetIntegrationsCatalogQuery(), ct)))
            .RequirePermission(View);

        MapWebhooks(integrations.MapGroup("/webhooks"));
        MapDeliveries(integrations.MapGroup("/webhook-deliveries"));
        MapApiKeys(integrations.MapGroup("/api-keys"));
        return integrations;
    }

    private static void MapWebhooks(RouteGroupBuilder webhooks)
    {
        webhooks.MapGet("/", async (IQueryHandler<ListWebhookEndpointsQuery, IReadOnlyList<WebhookEndpointDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListWebhookEndpointsQuery(), ct)))
            .RequirePermission(View);

        webhooks.MapPost("/", async (
            CreateWebhookEndpointRequest request, ICommandHandler<CreateWebhookEndpointCommand, WebhookSecretIssued> handler, CancellationToken ct) =>
        {
            var issued = await handler.HandleAsync(new CreateWebhookEndpointCommand(request.Url, request.Events), ct);
            return TypedResults.CreatedAtRoute(issued, GetEndpointRoute, new { id = issued.EndpointId });
        }).RequirePermission(Manage);

        webhooks.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetWebhookEndpointQuery, WebhookEndpointDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetWebhookEndpointQuery(id), ct)))
            .WithName(GetEndpointRoute)
            .RequirePermission(View);

        webhooks.MapPost("/{id:guid}/events", async (
            Guid id, SubscribeWebhookEventRequest request, ICommandHandler<SubscribeWebhookEventCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SubscribeWebhookEventCommand(id, request.EventType), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        webhooks.MapDelete("/{id:guid}/events/{eventType}", async (
            Guid id, string eventType, ICommandHandler<UnsubscribeWebhookEventCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UnsubscribeWebhookEventCommand(id, eventType), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        webhooks.MapPost("/{id:guid}/enable", (Guid id, ICommandHandler<SetWebhookEndpointEnabledCommand> handler, CancellationToken ct) =>
            SetEnabledAsync(handler, id, true, ct))
            .RequirePermission(Manage);

        webhooks.MapPost("/{id:guid}/disable", (Guid id, ICommandHandler<SetWebhookEndpointEnabledCommand> handler, CancellationToken ct) =>
            SetEnabledAsync(handler, id, false, ct))
            .RequirePermission(Manage);

        // Par défaut, l'ancien secret signe encore pendant la période de grâce ; ?immediate=true le détruit aussitôt.
        webhooks.MapPost("/{id:guid}/rotate-secret", async (
            Guid id, bool? immediate, ICommandHandler<RotateWebhookSecretCommand, WebhookSecretIssued> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new RotateWebhookSecretCommand(id, immediate ?? false), ct)))
            .RequirePermission(Manage);

        // Envoi de test (événement « integrations.ping » signé), tout de suite, sans livraison enregistrée.
        webhooks.MapPost("/{id:guid}/test", async (Guid id, ICommandHandler<SendWebhookTestCommand, WebhookTestResult> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new SendWebhookTestCommand(id), ct)))
            .RequirePermission(Manage);

        webhooks.MapGet("/{id:guid}/deliveries", async (
            Guid id,
            WebhookDeliveryStatus? status,
            int? page,
            int? pageSize,
            IQueryHandler<ListWebhookDeliveriesQuery, IReadOnlyList<WebhookDeliverySummaryDto>> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(new ListWebhookDeliveriesQuery(id, status, skip, take), ct))))
            .RequirePermission(View);
    }

    private static void MapDeliveries(RouteGroupBuilder deliveries)
    {
        deliveries.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetWebhookDeliveryQuery, WebhookDeliveryDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetWebhookDeliveryQuery(id), ct)))
            .WithName(GetDeliveryRoute)
            .RequirePermission(View);

        // Renvoi d'une livraison abandonnée : une NOUVELLE livraison du même événement.
        deliveries.MapPost("/{id:guid}/retry", async (Guid id, ICommandHandler<RetryDeadLetteredDeliveryCommand, Guid> handler, CancellationToken ct) =>
        {
            var retryId = await handler.HandleAsync(new RetryDeadLetteredDeliveryCommand(id), ct);
            return TypedResults.CreatedAtRoute(new { id = retryId }, GetDeliveryRoute, new { id = retryId });
        }).RequirePermission(Manage);
    }

    private static void MapApiKeys(RouteGroupBuilder keys)
    {
        // ?dormant=true : seulement les clés utilisables mais inutilisées depuis longtemps.
        keys.MapGet("/", async (bool? dormant, IQueryHandler<ListApiCredentialsQuery, IReadOnlyList<ApiCredentialDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListApiCredentialsQuery(dormant ?? false), ct)))
            .RequirePermission(View);

        keys.MapPost("/", async (IssueApiCredentialRequest request, ICommandHandler<IssueApiCredentialCommand, IssuedApiKey> handler, CancellationToken ct) =>
        {
            var issued = await handler.HandleAsync(new IssueApiCredentialCommand(request.Name, request.Scopes, request.ExpiresAtUtc), ct);
            return TypedResults.CreatedAtRoute(issued, GetCredentialRoute, new { id = issued.CredentialId });
        }).RequirePermission(Manage);

        keys.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetApiCredentialQuery, ApiCredentialDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetApiCredentialQuery(id), ct)))
            .WithName(GetCredentialRoute)
            .RequirePermission(View);

        keys.MapPost("/{id:guid}/rotate", async (Guid id, ICommandHandler<RotateApiCredentialCommand, IssuedApiKey> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new RotateApiCredentialCommand(id), ct)))
            .RequirePermission(Manage);

        keys.MapPost("/{id:guid}/revoke", async (Guid id, ICommandHandler<RevokeApiCredentialCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RevokeApiCredentialCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);
    }

    private static async Task<NoContent> SetEnabledAsync(ICommandHandler<SetWebhookEndpointEnabledCommand> handler, Guid id, bool enabled, CancellationToken ct)
    {
        await handler.HandleAsync(new SetWebhookEndpointEnabledCommand(id, enabled), ct);
        return TypedResults.NoContent();
    }
}
