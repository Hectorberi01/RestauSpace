using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Payments.Application;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Application.Queries;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;
using SaasRestaurant.BuildingBlocks.Api;

namespace Payments.Api;

/// <param name="Payments">Routes du personnel : l'hôte y chaîne <c>Tenancy.TenantMemberPolicy</c>.</param>
/// <param name="Webhooks">Routes des prestataires : anonymes, l'authenticité vient de la signature du webhook.</param>
/// <param name="Platform">Catalogue des prestataires de la plateforme : l'hôte y chaîne sa policy d'administrateur.</param>
public sealed record PaymentsRouteGroups(RouteGroupBuilder Payments, RouteGroupBuilder Webhooks, RouteGroupBuilder Platform);

/// <summary>
/// Paiements du restaurant sélectionné dans l'access token. Aucune route du personnel ne prend de tenant : ce n'est
/// jamais une valeur de requête. Lectures ouvertes à tout membre ; encaisser : <see cref="PaymentsPermissions.Collect"/> ;
/// rembourser : <see cref="PaymentsPermissions.Refund"/>.
/// </summary>
public static class PaymentsEndpoints
{
    /// <summary>Un webhook plus gros n'est pas lu (aucun prestataire n'en envoie de tels).</summary>
    public const int MaxWebhookBytes = 256 * 1024;

    private const string Collect = PaymentsPermissions.Collect;
    private const string Refund = PaymentsPermissions.Refund;
    private const string Configure = PaymentsPermissions.Configure;
    private const string GetPaymentRoute = "GetPayment";
    private const string GetCashSessionRoute = "GetCashSession";

    public static PaymentsRouteGroups MapPaymentsEndpoints(this IEndpointRouteBuilder routes) =>
        new(MapPayments(routes), MapWebhooks(routes), MapPlatform(routes));

    private static RouteGroupBuilder MapPayments(IEndpointRouteBuilder routes)
    {
        var payments = routes.MapGroup("/payments").WithTags("Payments")
            .AllowApiKeyRead(PaymentsPermissions.Collect, PaymentsPermissions.Refund);

        payments.MapPost("/offline", async (
            OfflinePaymentRequest request, ICommandHandler<RecordOfflinePaymentCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new RecordOfflinePaymentCommand(request.OrderId, request.Provider, request.Reference, request.Amount, request.PaymentMethod, request.Items), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetPaymentRoute, new { id });
        }).RequirePermission(Collect).RequireIdempotencyKey();

        payments.MapPost("/online", async (
            OnlinePaymentRequest request, ICommandHandler<StartOnlinePaymentCommand, OnlinePaymentStarted> handler, CancellationToken ct) =>
        {
            var started = await handler.HandleAsync(new StartOnlinePaymentCommand(request.OrderId, request.Provider, request.Amount, request.PaymentMethod, request.PayerPhone, Items: request.Items), ct);
            return TypedResults.CreatedAtRoute(started, GetPaymentRoute, new { id = started.PaymentId });
        }).RequirePermission(Collect).RequireIdempotencyKey();

        // Acompte d'une réservation : encaissé par l'équipe, puis déduit de l'addition du repas.
        payments.MapGet("/deposits/{reservationId:guid}", async (
            Guid reservationId, IQueryHandler<GetDepositPaymentsQuery, DepositPaymentsDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetDepositPaymentsQuery(reservationId), ct)));

        payments.MapPost("/deposits/{reservationId:guid}/offline", async (
            Guid reservationId, OfflineDepositRequest request, ICommandHandler<RecordOfflineDepositCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new RecordOfflineDepositCommand(reservationId, request.Provider, request.Reference, request.PaymentMethod), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetPaymentRoute, new { id });
        }).RequirePermission(Collect).RequireIdempotencyKey();

        payments.MapPost("/deposits/{reservationId:guid}/apply", async (
            Guid reservationId, ApplyDepositRequest request, ICommandHandler<ApplyDepositCommand, decimal> handler, CancellationToken ct) =>
            TypedResults.Ok(new { applied = await handler.HandleAsync(new ApplyDepositCommand(reservationId, request.OrderId), ct) }))
            .RequirePermission(Collect);

        payments.MapGet("/", async (
            Guid orderId, IQueryHandler<GetOrderPaymentsQuery, OrderPaymentsDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetOrderPaymentsQuery(orderId), ct)));

        // Les moyens proposés pour une commande : établissement, montant et devise de la commande, au comptoir ou en ligne.
        payments.MapGet("/payment-methods/available", async (
            Guid locationId, decimal amount, string? currency, PaymentChannel? channel,
            IQueryHandler<GetAvailablePaymentMethodsQuery, IReadOnlyList<AvailablePaymentMethodDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new GetAvailablePaymentMethodsQuery(locationId, amount, currency, channel ?? PaymentChannel.InPerson), ct)));

        payments.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetPaymentByIdQuery, PaymentDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetPaymentByIdQuery(id), ct)))
            .WithName(GetPaymentRoute);

        payments.MapPost("/{id:guid}/retry", async (
            Guid id, RetryPaymentRequest? request, ICommandHandler<RetryPaymentCommand, OnlinePaymentStarted> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new RetryPaymentCommand(id, request?.PayerPhone), ct)))
            .RequirePermission(Collect)
            .RequireIdempotencyKey();

        payments.MapPost("/{id:guid}/cancel", async (
            Guid id, ReasonRequest request, ICommandHandler<CancelPaymentCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new CancelPaymentCommand(id, request.Reason), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Collect);

        payments.MapPost("/{id:guid}/refunds", async (
            Guid id, RefundRequest request, ICommandHandler<RequestRefundCommand, Guid> handler, CancellationToken ct) =>
        {
            var refundId = await handler.HandleAsync(new RequestRefundCommand(id, request.Reason, request.Amount), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(refundId), GetPaymentRoute, new { id });
        }).RequirePermission(Refund).RequireIdempotencyKey();

        // Le code de l'avoir n'est renvoyé qu'ici, une fois.
        payments.MapPost("/{id:guid}/refunds/store-credit", async (
            Guid id, RefundRequest request, ICommandHandler<RefundAsStoreCreditCommand, StoreCreditRefundDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new RefundAsStoreCreditCommand(id, request.Reason, request.Amount), ct)))
            .RequirePermission(Refund).RequireIdempotencyKey();

        MapConfigurations(payments);
        MapCashDesk(payments);
        return payments;
    }

    // Ce que le restaurant accepte réellement, parmi ce que la plateforme propose : une configuration par prestataire.
    // Les identifiants saisis partent au coffre ; aucune réponse ne les renvoie.
    private static void MapConfigurations(RouteGroupBuilder payments)
    {
        payments.MapGet("/providers", async (
            IQueryHandler<ListOfferedPaymentProvidersQuery, IReadOnlyList<PaymentProviderDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListOfferedPaymentProvidersQuery(), ct)));

        var configurations = payments.MapGroup("/configurations").AllowApiKeyRead(PaymentsPermissions.Configure);

        configurations.MapGet("/", async (
            IQueryHandler<ListPaymentConfigurationsQuery, IReadOnlyList<RestaurantPaymentConfigurationDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListPaymentConfigurationsQuery(), ct)));

        configurations.MapPost("/", async (
            CreateConfigurationRequest request, ICommandHandler<CreatePaymentConfigurationCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreatePaymentConfigurationCommand(request.Provider, request.Mode ?? Payments.Domain.PaymentMode.TenantManaged), ct);
            return TypedResults.Created($"/payments/configurations/{id}", new CreatedResponse(id));
        }).RequirePermission(Configure);

        configurations.MapPut("/{id:guid}/credentials", async (
            Guid id, CredentialsRequest request, ICommandHandler<ConfigurePaymentConfigurationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ConfigurePaymentConfigurationCommand(id, request.Credentials), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        configurations.MapPut("/{id:guid}/mode", async (
            Guid id, ModeRequest request, ICommandHandler<ChangePaymentModeCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangePaymentModeCommand(id, request.Mode), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        configurations.MapPost("/{id:guid}/test", async (
            Guid id, ICommandHandler<TestPaymentConfigurationCommand, PaymentConfigurationTestDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new TestPaymentConfigurationCommand(id), ct)))
            .RequirePermission(Configure)
            .RequireRateLimiting(PaymentsRateLimiting.ConfigurationTestPolicy);

        configurations.MapPost("/{id:guid}/enable", async (
            Guid id, ICommandHandler<EnablePaymentConfigurationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new EnablePaymentConfigurationCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        configurations.MapPost("/{id:guid}/disable", async (
            Guid id, ICommandHandler<DisablePaymentConfigurationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new DisablePaymentConfigurationCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        configurations.MapPut("/{id:guid}/locations/{locationId:guid}", async (
            Guid id, Guid locationId, LocationAssignmentRequest request, ICommandHandler<AssignPaymentMethodToLocationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new AssignPaymentMethodToLocationCommand(
                    id, locationId, request.Method, request.Priority, request.MinAmount, request.MaxAmount, request.Currency, request.IsEnabled),
                ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        configurations.MapDelete("/{id:guid}/locations/{locationId:guid}", async (
            Guid id, Guid locationId, Payments.Domain.PaymentMethodType? method, ICommandHandler<RemovePaymentMethodFromLocationCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RemovePaymentMethodFromLocationCommand(id, locationId, method), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);

        configurations.MapPut("/{id:guid}/methods", async (
            Guid id, MethodStateRequest request, ICommandHandler<SetPaymentMethodActiveCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetPaymentMethodActiveCommand(id, request.Method, request.IsActive), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Configure);
    }

    // Ce que la plateforme supporte techniquement : son catalogue de prestataires.
    // Caisse : réservée à ceux qui encaissent. Le journal et les sessions ne disent rien d'un autre tenant.
    private static void MapCashDesk(RouteGroupBuilder payments)
    {
        payments.MapGet("/journal", async (
            Guid? locationId, DateTimeOffset from, DateTimeOffset to, int? limit,
            IQueryHandler<GetPaymentJournalQuery, PaymentJournalDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetPaymentJournalQuery(locationId, from, to, limit), ct)))
            .RequirePermission(Collect);

        var sessions = payments.MapGroup("/cash-sessions").RequirePermission(Collect);

        sessions.MapPost("/", async (
            OpenCashSessionRequest request, ICommandHandler<OpenCashSessionCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new OpenCashSessionCommand(request.LocationId, request.OpeningFloat), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetCashSessionRoute, new { id });
        }).RequireIdempotencyKey();

        sessions.MapGet("/", async (
            Guid? locationId, DateTimeOffset? from, DateTimeOffset? to, int? limit,
            IQueryHandler<ListCashSessionsQuery, IReadOnlyList<CashSessionSummaryDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListCashSessionsQuery(locationId, from, to, limit), ct)));

        sessions.MapGet("/open", async (
            Guid locationId, IQueryHandler<GetOpenCashSessionQuery, CashSessionDto?> handler, CancellationToken ct) =>
            await handler.HandleAsync(new GetOpenCashSessionQuery(locationId), ct) is { } session
                ? Results.Ok(session)
                : Results.NoContent());

        sessions.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetCashSessionQuery, CashSessionDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetCashSessionQuery(id), ct)))
            .WithName(GetCashSessionRoute);

        sessions.MapPost("/{id:guid}/movements", async (
            Guid id, CashMovementRequest request, ICommandHandler<RecordCashMovementCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RecordCashMovementCommand(id, request.Kind, request.Amount, request.Reason), ct);
            return TypedResults.NoContent();
        }).RequireIdempotencyKey();

        sessions.MapPost("/{id:guid}/close", async (
            Guid id, CloseCashSessionRequest request, ICommandHandler<CloseCashSessionCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new CloseCashSessionCommand(id, request.Counted, request.Denominations, request.Note), ct);
            return TypedResults.NoContent();
        });
    }

    private static RouteGroupBuilder MapPlatform(IEndpointRouteBuilder routes)
    {
        var platform = routes.MapGroup("/admin/payment-providers").WithTags("Payments platform");

        platform.MapGet("/", async (
            IQueryHandler<ListPaymentProvidersQuery, IReadOnlyList<PaymentProviderDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListPaymentProvidersQuery(), ct)));

        platform.MapGet("/integrations", async (
            IQueryHandler<ListPaymentIntegrationsQuery, IReadOnlyList<PaymentIntegrationDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListPaymentIntegrationsQuery(), ct)));

        platform.MapPost("/", async (
            RegisterProviderRequest request, ICommandHandler<RegisterPaymentProviderCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new RegisterPaymentProviderCommand(request.Code, request.Type, request.DisplayName, request.Capability!), ct);
            return TypedResults.Created($"/platform/payment-providers/{id}", new CreatedResponse(id));
        });

        platform.MapPut("/{id:guid}", async (
            Guid id, UpdateProviderRequest request, ICommandHandler<UpdatePaymentProviderCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UpdatePaymentProviderCommand(id, request.DisplayName, request.Capability!), ct);
            return TypedResults.NoContent();
        });

        // Compte marchand de la PLATEFORME (mode PlatformManaged) : rangé au coffre, jamais renvoyé.
        platform.MapPut("/{id:guid}/credentials", async (
            Guid id, CredentialsRequest request, ICommandHandler<SetPlatformCredentialsCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetPlatformCredentialsCommand(id, request.Credentials), ct);
            return TypedResults.NoContent();
        });

        platform.MapPost("/{id:guid}/enable", async (
            Guid id, ICommandHandler<EnablePaymentProviderCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new EnablePaymentProviderCommand(id), ct);
            return TypedResults.NoContent();
        });

        platform.MapPost("/{id:guid}/disable", async (
            Guid id, ICommandHandler<DisablePaymentProviderCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new DisablePaymentProviderCommand(id), ct);
            return TypedResults.NoContent();
        });

        return platform;
    }

    // Réponses voulues par les prestataires : 2xx dès que le webhook est conservé (ou déjà reçu), sinon ils renvoient.
    private static RouteGroupBuilder MapWebhooks(IEndpointRouteBuilder routes)
    {
        var webhooks = routes.MapGroup("/payments/webhooks").WithTags("Payments webhooks");

        // Une adresse par configuration de restaurant : chaque compte marchand signe avec SON secret. PUT : MTN MoMo
        // rappelle l'adresse de notification par cette méthode.
        webhooks.MapPost("/{provider}/{configurationId:guid}", ReceiveAsync);
        webhooks.MapPut("/{provider}/{configurationId:guid}", ReceiveAsync);

        // Compte de la plateforme (abonnements) : une seule adresse par prestataire.
        webhooks.MapPost("/{provider}", ReceivePlatformAsync);

        return webhooks;
    }

    private static async Task<Results<Ok, NotFound, BadRequest, StatusCodeHttpResult>> ReceivePlatformAsync(
        string provider, HttpRequest request, PaymentWebhookReceiver receiver, IPlatformCollectionListener listener, CancellationToken ct)
    {
        if (await ReadBodyAsync(request, ct) is not { } body)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var headers = request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var (receipt, reference) = await receiver.ReceivePlatformAsync(provider, headers, body, ct);
        if (reference is not null)
        {
            await listener.TransactionChangedAsync(provider, reference, ct);
        }

        return receipt switch
        {
            WebhookReceipt.Accepted or WebhookReceipt.Duplicate or WebhookReceipt.Ignored => TypedResults.Ok(),
            WebhookReceipt.UnknownEndpoint => TypedResults.NotFound(),
            WebhookReceipt.Unavailable => TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => TypedResults.BadRequest(),
        };
    }

    private static async Task<Results<Ok, NotFound, BadRequest, StatusCodeHttpResult>> ReceiveAsync(
        string provider, Guid configurationId, HttpRequest request, PaymentWebhookReceiver receiver, CancellationToken ct)
    {
        if (await ReadBodyAsync(request, ct) is not { } body)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var headers = request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        return await receiver.ReceiveAsync(provider, configurationId, headers, body, ct) switch
        {
            WebhookReceipt.Accepted or WebhookReceipt.Duplicate or WebhookReceipt.Ignored => TypedResults.Ok(),
            WebhookReceipt.UnknownEndpoint => TypedResults.NotFound(),
            WebhookReceipt.Unavailable => TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => TypedResults.BadRequest(),
        };
    }

    /// <summary>Le corps EXACT reçu (la signature porte dessus), ou null s'il dépasse <see cref="MaxWebhookBytes"/>.</summary>
    private static async Task<string?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
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

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
