using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Notifications.Application;
using Notifications.Application.Commands;
using Notifications.Application.Queries;
using Notifications.Domain;
using Notifications.Infrastructure.Webhooks;
using SaasRestaurant.BuildingBlocks.Api;

namespace Notifications.Api;

/// <param name="Restaurant">Routes du personnel : l'hôte y chaîne <c>Tenancy.TenantMemberPolicy</c>.</param>
/// <param name="Admin">Administration de la plateforme (catalogue, templates de la plateforme, dead letter) : policy d'administrateur.</param>
/// <param name="Webhooks">Routes des prestataires : anonymes, l'authenticité vient de la signature.</param>
/// <param name="Public">Lien de désinscription des clients : anonyme, l'autorisation est la signature du jeton.</param>
public sealed record NotificationsRouteGroups(RouteGroupBuilder Restaurant, RouteGroupBuilder Admin, RouteGroupBuilder Webhooks, RouteGroupBuilder Public);

/// <summary>
/// Routes du module (§ 34-35). Le tenant n'est JAMAIS une valeur de requête : il vient de l'access token. Préfixe : la
/// convention de la solution (pas de <c>/api/v1</c>, comme les autres modules). Aucune réponse ne contient
/// d'identifiant de compte, de contenu rendu, d'OTP ni d'adresse non masquée (sauf la boîte in-app de son destinataire).
/// </summary>
public static class NotificationsEndpoints
{
    /// <summary>Un webhook plus gros n'est pas lu.</summary>
    public const int MaxWebhookBytes = 256 * 1024;

    private const string View = NotificationsPermissions.View;
    private const string Send = NotificationsPermissions.Send;
    private const string Configure = NotificationsPermissions.Configure;
    private const string ManagePreferences = NotificationsPermissions.ManagePreferences;

    public static NotificationsRouteGroups MapNotificationsEndpoints(this IEndpointRouteBuilder routes) =>
        new(MapRestaurant(routes), MapAdmin(routes), MapWebhooks(routes), MapPublic(routes));

    // Le jeton voyage dans le corps (jamais dans l'URL de l'API, souvent journalisée) ; la lecture n'a aucun effet.
    private static RouteGroupBuilder MapPublic(IEndpointRouteBuilder routes)
    {
        var unsubscribe = routes.MapGroup("/public/notifications/unsubscribe").WithTags("Public");

        unsubscribe.MapPost("/preview", async (UnsubscribeRequest request, IQueryHandler<GetUnsubscribeLinkQuery, UnsubscribeLinkDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetUnsubscribeLinkQuery(request.Token), ct)));

        unsubscribe.MapPost("/", async (UnsubscribeRequest request, ICommandHandler<UnsubscribeCommand, UnsubscribeLinkDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new UnsubscribeCommand(request.Token), ct)));

        return unsubscribe;
    }

    private static RouteGroupBuilder MapRestaurant(IEndpointRouteBuilder routes)
    {
        var restaurant = routes.MapGroup(string.Empty).WithTags("Notifications");
        MapConfigurations(restaurant.MapGroup("/notification-configurations"));
        MapTemplates(restaurant.MapGroup("/notification-templates"), TemplateOwner.Tenant, Configure);
        MapHistory(restaurant.MapGroup("/notifications"));
        MapCampaigns(restaurant.MapGroup("/campaigns"));
        MapPersonal(restaurant);
        return restaurant;
    }

    private static void MapConfigurations(RouteGroupBuilder configurations)
    {
        configurations.MapGet("/", async (
            IQueryHandler<ListNotificationConfigurationsQuery, IReadOnlyList<NotificationConfigurationDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListNotificationConfigurationsQuery(), ct)))
            .RequirePermission(View);

        // Les prestataires que la plateforme propose (actifs) : de quoi choisir.
        configurations.MapGet("/providers", async (
            IQueryHandler<ListNotificationProvidersQuery, IReadOnlyList<NotificationProviderDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListNotificationProvidersQuery(OnlyEnabled: true), ct)));

        configurations.MapPost("/{providerId:guid}/configure", async (
            Guid providerId, CreateConfigurationRequest request, ICommandHandler<CreateNotificationConfigurationCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateNotificationConfigurationCommand(providerId, request.Mode ?? NotificationProviderMode.TenantManaged), ct);
            return TypedResults.Created($"/notification-configurations/{id}", new CreatedResponse(id));
        }).RequirePermission(Configure);

        configurations.MapPut("/{id:guid}/credentials", async (
            Guid id, CredentialsRequest request, ICommandHandler<SetConfigurationCredentialsCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetConfigurationCredentialsCommand(id, request.Credentials ?? new Dictionary<string, string>()), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        configurations.MapPost("/{id:guid}/test", async (
            Guid id, TestConfigurationRequest request, ICommandHandler<TestNotificationConfigurationCommand, NotificationConfigurationTestDto> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new TestNotificationConfigurationCommand(id, request.Address), ct)))
            .RequirePermission(Configure)
            .RequireRateLimiting(NotificationsRateLimiting.ConfigurationTestPolicy);

        configurations.MapPut("/{id:guid}/mode", async (
            Guid id, ModeRequest request, ICommandHandler<ChangeNotificationModeCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeNotificationModeCommand(id, request.Mode), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        configurations.MapPost("/{id:guid}/enable", async (
            Guid id, ICommandHandler<EnableNotificationConfigurationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new EnableNotificationConfigurationCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        configurations.MapPost("/{id:guid}/disable", async (
            Guid id, ICommandHandler<DisableNotificationConfigurationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new DisableNotificationConfigurationCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        configurations.MapPut("/{id:guid}/locations", async (
            Guid id, AssignLocationRequest request, ICommandHandler<AssignNotificationLocationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new AssignNotificationLocationCommand(id, request.LocationId, request.Priority, request.IsEnabled), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        configurations.MapDelete("/{id:guid}/locations/{locationId:guid}", async (
            Guid id, Guid locationId, ICommandHandler<RemoveNotificationLocationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RemoveNotificationLocationCommand(id, locationId), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);
    }

    /// <summary>Les mêmes routes pour les templates d'un restaurant et ceux de la plateforme ; le propriétaire vient du groupe, jamais de la requête.</summary>
    private static void MapTemplates(RouteGroupBuilder templates, TemplateOwner owner, string? permission)
    {
        RouteHandlerBuilder Protect(RouteHandlerBuilder route) => permission is null ? route : route.RequirePermission(permission);

        templates.MapGet("/", async (IQueryHandler<ListTemplatesQuery, IReadOnlyList<NotificationTemplateDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListTemplatesQuery(owner), ct)));

        templates.MapGet("/types", async (IQueryHandler<ListNotificationTypesQuery, IReadOnlyList<NotificationTypeDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListNotificationTypesQuery(owner), ct)));

        templates.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetTemplateQuery, NotificationTemplateDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetTemplateQuery(owner, id), ct)));

        Protect(templates.MapPost("/", async (CreateTemplateRequest request, ICommandHandler<CreateTemplateCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateTemplateCommand(owner, request.Key ?? string.Empty, request.Channel, request.Language ?? string.Empty, request.Subject, request.Body), ct);
            return TypedResults.Created($"{id}", new CreatedResponse(id));
        }));

        Protect(templates.MapPut("/{id:guid}", async (Guid id, UpdateTemplateRequest request, ICommandHandler<UpdateTemplateCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UpdateTemplateCommand(owner, id, request.Subject, request.Body), ct);
            return TypedResults.NoContent();
        }));

        Protect(templates.MapPost("/{id:guid}/versions", async (Guid id, ICommandHandler<CreateTemplateVersionCommand, Guid> handler, CancellationToken ct) =>
        {
            var next = await handler.HandleAsync(new CreateTemplateVersionCommand(owner, id), ct);
            return TypedResults.Created($"{next}", new CreatedResponse(next));
        }));

        foreach (var (route, action) in new[]
        {
            ("publish", TemplateAction.Publish), ("enable", TemplateAction.Enable), ("disable", TemplateAction.Disable),
        })
        {
            Protect(templates.MapPost("/{id:guid}/" + route, async (Guid id, ICommandHandler<ChangeTemplateCommand> handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(new ChangeTemplateCommand(owner, id, action), ct);
                return TypedResults.NoContent();
            }));
        }

        // DELETE = archiver : une version publiée prouve ce qui a été envoyé.
        Protect(templates.MapDelete("/{id:guid}", async (Guid id, ICommandHandler<ChangeTemplateCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeTemplateCommand(owner, id, TemplateAction.Archive), ct);
            return TypedResults.NoContent();
        }));
    }

    // Campagnes marketing : préparées et lancées par qui peut envoyer, règle de fréquence par qui configure.
    private static void MapCampaigns(RouteGroupBuilder campaigns)
    {
        campaigns.MapGet("/", async (IQueryHandler<ListCampaignsQuery, IReadOnlyList<CampaignDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListCampaignsQuery(), ct)))
            .RequirePermission(View);

        campaigns.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetCampaignQuery, CampaignDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetCampaignQuery(id), ct)))
            .RequirePermission(View);

        campaigns.MapGet("/audience", async (
            NotificationChannel channel, string? tag, Guid? customersOf, IQueryHandler<PreviewCampaignAudienceQuery, CampaignAudienceDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new PreviewCampaignAudienceQuery(channel, tag, customersOf), ct)))
            .RequirePermission(Send);

        campaigns.MapPost("/", async (CampaignRequest request, ICommandHandler<SaveCampaignCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new SaveCampaignCommand(
                    null, request.Name, request.Type ?? string.Empty, request.Channel, request.Tag, request.LocationId, request.Variables, request.OnlyLocationCustomers), ct);
            return TypedResults.Created($"/campaigns/{id}", new CreatedResponse(id));
        }).RequirePermission(Send);

        campaigns.MapPut("/{id:guid}", async (Guid id, CampaignRequest request, ICommandHandler<SaveCampaignCommand, Guid> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new SaveCampaignCommand(
                    id, request.Name, request.Type ?? string.Empty, request.Channel, request.Tag, request.LocationId, request.Variables, request.OnlyLocationCustomers), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Send);

        campaigns.MapPost("/{id:guid}/schedule", async (Guid id, ScheduleCampaignRequest request, ICommandHandler<ScheduleCampaignCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ScheduleCampaignCommand(id, request.At), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Send);

        campaigns.MapPost("/{id:guid}/unschedule", async (Guid id, ICommandHandler<UnscheduleCampaignCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UnscheduleCampaignCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Send);

        campaigns.MapPost("/{id:guid}/cancel", async (Guid id, ICommandHandler<CancelCampaignCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new CancelCampaignCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Send);

        campaigns.MapGet("/rules", async (IQueryHandler<GetMarketingRulesQuery, MarketingRulesDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetMarketingRulesQuery(), ct)))
            .RequirePermission(View);

        campaigns.MapPut("/rules", async (MarketingRulesRequest request, ICommandHandler<SetMarketingRulesCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetMarketingRulesCommand(request.MinimumIntervalDays), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);
    }

    private static void MapHistory(RouteGroupBuilder notifications)
    {
        notifications.MapGet("/", async (
            [AsParameters] HistoryFilter filter, IQueryHandler<ListNotificationsQuery, IReadOnlyList<NotificationDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new ListNotificationsQuery(
                    filter.Status, filter.Channel, filter.Type, filter.CustomerId, filter.From, filter.To, filter.Skip ?? 0, filter.Take ?? 50),
                ct)))
            .RequirePermission(View);

        notifications.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetNotificationQuery, NotificationDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetNotificationQuery(id), ct)))
            .RequirePermission(View);

        notifications.MapPost("/", async (
            SendRequest request, ICommandHandler<SendNotificationCommand, SendNotificationResult> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new SendNotificationCommand(
                    request.Type ?? string.Empty, request.Channel, request.CustomerId, request.UserId, request.Address, request.LocationId,
                    request.Language, request.Variables, request.ScheduledAtUtc, request.IdempotencyKey),
                ct)))
            .RequirePermission(Send);

        notifications.MapPost("/{id:guid}/cancel", async (Guid id, ICommandHandler<CancelNotificationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new CancelNotificationCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Send);

        // Préférences des clients du restaurant.
        notifications.MapGet("/preferences/customers/{customerId:guid}", async (
            Guid customerId, IQueryHandler<GetNotificationPreferencesQuery, NotificationPreferencesDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetNotificationPreferencesQuery(new PreferenceSubject(customerId)), ct)))
            .RequirePermission(ManagePreferences);

        notifications.MapPut("/preferences/customers/{customerId:guid}", async (
            Guid customerId, PreferenceRequest request, ICommandHandler<SetNotificationPreferenceCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new SetNotificationPreferenceCommand(new PreferenceSubject(customerId), request.Purpose, request.Channel, request.IsAllowed, request.Source), ct);
            return TypedResults.NoContent();
        }).RequirePermission(ManagePreferences);

        // Codes à usage unique (vérification d'un téléphone ou d'une adresse par le personnel).
        notifications.MapPost("/otp", async (OtpRequest request, ICommandHandler<RequestOtpCommand, OtpRequested> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new RequestOtpCommand(request.Purpose ?? string.Empty, request.Channel, request.Address ?? string.Empty, request.Language), ct)))
            .RequirePermission(Send);

        notifications.MapPost("/otp/{id:guid}/verify", async (
            Guid id, OtpVerifyRequest request, ICommandHandler<VerifyOtpCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new VerifyOtpCommand(id, request.Code ?? string.Empty), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Send);
    }

    /// <summary>Ce qui appartient à l'utilisateur connecté : sa boîte in-app, ses préférences, ses appareils.</summary>
    private static void MapPersonal(RouteGroupBuilder restaurant)
    {
        var me = restaurant.MapGroup("/me");

        me.MapGet("/notifications", async (
            bool? includeArchived, int? page, int? pageSize, IQueryHandler<ListMyInAppNotificationsQuery, IReadOnlyList<InAppNotificationDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) =>
                handler.HandleAsync(new ListMyInAppNotificationsQuery(includeArchived ?? false, take, skip), ct))));

        me.MapPost("/notifications/{id:guid}/read", async (Guid id, ICommandHandler<ChangeInAppNotificationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeInAppNotificationCommand(id, Archive: false), ct);
            return TypedResults.NoContent();
        });

        me.MapPost("/notifications/{id:guid}/archive", async (Guid id, ICommandHandler<ChangeInAppNotificationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeInAppNotificationCommand(id, Archive: true), ct);
            return TypedResults.NoContent();
        });

        me.MapGet("/notification-preferences", async (
            IQueryHandler<GetNotificationPreferencesQuery, NotificationPreferencesDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetNotificationPreferencesQuery(new PreferenceSubject(null)), ct)));

        me.MapPut("/notification-preferences", async (
            PreferenceRequest request, ICommandHandler<SetNotificationPreferenceCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new SetNotificationPreferenceCommand(new PreferenceSubject(null), request.Purpose, request.Channel, request.IsAllowed, request.Source), ct);
            return TypedResults.NoContent();
        });

        me.MapPut("/notification-preferences/alerts/{type}", async (
            string type, AlertTypeRequest request, ICommandHandler<SetAlertTypeCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetAlertTypeCommand(type, request.IsEnabled, request.Source), ct);
            return TypedResults.NoContent();
        });

        me.MapPut("/devices", async (DeviceRequest request, ICommandHandler<RegisterDeviceCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(
                new RegisterDeviceCommand(request.DeviceId ?? string.Empty, request.Platform, request.PushToken ?? string.Empty), ct))));

        me.MapDelete("/devices/{deviceId}", async (string deviceId, ICommandHandler<UnregisterDeviceCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UnregisterDeviceCommand(deviceId), ct);
            return TypedResults.NoContent();
        });
    }

    private static RouteGroupBuilder MapAdmin(IEndpointRouteBuilder routes)
    {
        var admin = routes.MapGroup("/admin").WithTags("Notifications administration");
        var providers = admin.MapGroup("/notification-providers");

        providers.MapGet("/", async (IQueryHandler<ListNotificationProvidersQuery, IReadOnlyList<NotificationProviderDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListNotificationProvidersQuery(), ct)));

        providers.MapGet("/integrations", async (
            IQueryHandler<ListNotificationIntegrationsQuery, IReadOnlyList<NotificationIntegrationDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListNotificationIntegrationsQuery(), ct)));

        providers.MapPost("/", async (CreateProviderRequest request, ICommandHandler<CreateNotificationProviderCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateNotificationProviderCommand(
                    request.Code ?? string.Empty, request.Name ?? string.Empty, request.Channel, request.ConfigurationType,
                    request.Capability ?? new ProviderCapabilityInput([])),
                ct);
            return TypedResults.Created($"/admin/notification-providers/{id}", new CreatedResponse(id));
        });

        providers.MapPut("/{id:guid}", async (
            Guid id, UpdateProviderRequest request, ICommandHandler<UpdateNotificationProviderCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new UpdateNotificationProviderCommand(id, request.Name ?? string.Empty, request.ConfigurationType, request.Capability ?? new ProviderCapabilityInput([])),
                ct);
            return TypedResults.NoContent();
        });

        providers.MapPost("/{id:guid}/enable", async (Guid id, ICommandHandler<EnableNotificationProviderCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new EnableNotificationProviderCommand(id), ct);
            return TypedResults.NoContent();
        });

        providers.MapPost("/{id:guid}/disable", async (Guid id, ICommandHandler<DisableNotificationProviderCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new DisableNotificationProviderCommand(id), ct);
            return TypedResults.NoContent();
        });

        // Compte de la PLATEFORME chez le prestataire : rangé au coffre, jamais renvoyé.
        providers.MapPut("/{id:guid}/credentials", async (
            Guid id, CredentialsRequest request, ICommandHandler<SetPlatformCredentialsCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetPlatformCredentialsCommand(id, request.Credentials ?? new Dictionary<string, string>()), ct);
            return TypedResults.NoContent();
        });

        MapTemplates(admin.MapGroup("/notification-templates"), TemplateOwner.Platform, permission: null);

        // Journal d'envoi et indicateurs de tous les restaurants : jamais de destinataire ni de contenu.
        admin.MapGet("/notifications", async (
            NotificationChannel? channel, NotificationStatus? status, int? page, int? pageSize,
            IQueryHandler<ListPlatformNotificationsQuery, IReadOnlyList<NotificationLogDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) =>
                handler.HandleAsync(new ListPlatformNotificationsQuery(channel, status, take, skip), ct))));

        admin.MapGet("/notifications/overview", async (
            DateTimeOffset? since, IQueryHandler<GetNotificationsOverviewQuery, NotificationsOverviewDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetNotificationsOverviewQuery(since), ct)));

        var deadLetters = admin.MapGroup("/notification-dead-letters");
        deadLetters.MapGet("/", async (int? page, int? pageSize, IQueryHandler<ListDeadLettersQuery, IReadOnlyList<DeadLetterDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(new ListDeadLettersQuery(take, skip), ct))));
        deadLetters.MapPost("/{id:guid}/retry", async (Guid id, ICommandHandler<RetryDeadLetterCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RetryDeadLetterCommand(id), ct);
            return TypedResults.NoContent();
        });
        deadLetters.MapPost("/{id:guid}/abandon", async (Guid id, ICommandHandler<AbandonDeadLetterCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new AbandonDeadLetterCommand(id), ct);
            return TypedResults.NoContent();
        });

        return admin;
    }

    // Réponses voulues par les prestataires : 2xx dès que le webhook est conservé (ou déjà reçu), sinon ils renvoient.
    private static RouteGroupBuilder MapWebhooks(IEndpointRouteBuilder routes)
    {
        var webhooks = routes.MapGroup("/notifications/webhooks").WithTags("Notifications webhooks");

        // Compte de la plateforme.
        webhooks.MapPost("/{provider}", (string provider, HttpRequest request, NotificationWebhookReceiver receiver, CancellationToken ct) =>
            ReceiveAsync(provider, null, request, receiver, ct));

        // Compte d'un restaurant (mode restaurant) : chaque compte signe avec SON secret.
        webhooks.MapPost("/{provider}/{configurationId:guid}", (
            string provider, Guid configurationId, HttpRequest request, NotificationWebhookReceiver receiver, CancellationToken ct) =>
            ReceiveAsync(provider, configurationId, request, receiver, ct));

        // Vérification d'abonnement (Meta) : le prestataire envoie un jeton connu du compte et attend le défi en retour.
        webhooks.MapGet("/{provider}", (string provider, HttpRequest request, NotificationWebhookReceiver receiver, CancellationToken ct) =>
            ChallengeAsync(provider, null, request, receiver, ct));

        webhooks.MapGet("/{provider}/{configurationId:guid}", (
            string provider, Guid configurationId, HttpRequest request, NotificationWebhookReceiver receiver, CancellationToken ct) =>
            ChallengeAsync(provider, configurationId, request, receiver, ct));

        return webhooks;
    }

    private static async Task<Results<ContentHttpResult, StatusCodeHttpResult>> ChallengeAsync(
        string provider, Guid? configurationId, HttpRequest request, NotificationWebhookReceiver receiver, CancellationToken ct)
    {
        var query = request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
        if (await receiver.AnswerChallengeAsync(provider, configurationId, query, ct) is { } answer)
        {
            return TypedResults.Text(answer, "text/plain");
        }

        return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<Results<Ok, NotFound, UnauthorizedHttpResult, StatusCodeHttpResult>> ReceiveAsync(
        string provider, Guid? configurationId, HttpRequest request, NotificationWebhookReceiver receiver, CancellationToken ct)
    {
        if (await ReadBodyAsync(request, ct) is not { } body)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var headers = request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        return await receiver.ReceiveAsync(provider, configurationId, headers, body, ct) switch
        {
            WebhookReception.Accepted => TypedResults.Ok(),
            WebhookReception.UnknownProvider => TypedResults.NotFound(),
            _ => TypedResults.Unauthorized(),
        };
    }

    /// <summary>Le corps EXACT reçu (la signature porte dessus), ou null s'il dépasse <see cref="MaxWebhookBytes"/>.</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxWebhookBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxWebhookBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}

/// <summary>Filtres de l'historique, en paramètres de requête.</summary>
public sealed record HistoryFilter(
    NotificationStatus? Status, NotificationChannel? Channel, string? Type, Guid? CustomerId, DateTimeOffset? From, DateTimeOffset? To, int? Skip, int? Take);
