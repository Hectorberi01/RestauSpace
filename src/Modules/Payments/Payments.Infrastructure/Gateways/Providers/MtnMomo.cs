using System.Text.Json.Nodes;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Gateways.Providers;

/// <summary>
/// MTN Mobile Money (API Collection, « request to pay »), sur le compte du restaurant : utilisateur et clé d'API, clé
/// d'abonnement (<c>Ocp-Apim-Subscription-Key</c>) et environnement cible (<c>sandbox</c>, <c>mtnbenin</c>,
/// <c>mtnivorycoast</c>…). Pas de page de paiement : la demande est POUSSÉE sur le téléphone du payeur, dont le numéro est
/// exigé. Le rappel de MTN n'est pas signé : il ne sert qu'à savoir quoi relire, et la demande est relue chez MTN, qui seule
/// fait foi. Le remboursement passe par l'API Disbursement de MTN, hors de cette intégration : depuis le portail MTN.
/// </summary>
internal sealed class MtnMomoIntegration(ProviderHttp http, PaymentsOptions options) : IPaymentIntegration
{
    public const string Code = "mtn_momo";
    public const string ApiUser = "api_user";
    public const string ApiKey = "api_key";
    public const string SubscriptionKey = "subscription_key";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(Code);

    public string Name => "MTN Mobile Money";

    public IReadOnlyList<string> CredentialFields { get; } = [ApiUser, ApiKey, SubscriptionKey, ProviderSupport.Environment];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) =>
        new MtnMomoGateway(Provider, http, options, MtnMomoApi.From(credentials));

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        new MtnMomoWebhookReader(http, MtnMomoApi.From(credentials));
}

internal sealed record MtnMomoApi(string ApiUser, string ApiKey, string SubscriptionKey, string TargetEnvironment)
{
    public string BaseUrl => TargetEnvironment == ProviderSupport.Sandbox ? "https://sandbox.momodeveloper.mtn.com" : "https://proxy.momoapi.mtn.com";

    public static MtnMomoApi From(IReadOnlyDictionary<string, string> credentials)
    {
        var environment = ProviderSupport.Optional(credentials, ProviderSupport.Environment)?.ToLowerInvariant() ?? ProviderSupport.Sandbox;
        if (environment.Length > 40 || !environment.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            throw new PaymentGatewayException("MTN MoMo: the target environment is not valid.", isPermanent: true);
        }

        return new MtnMomoApi(
            ProviderSupport.Required(credentials, MtnMomoIntegration.Code, MtnMomoIntegration.ApiUser),
            ProviderSupport.Required(credentials, MtnMomoIntegration.Code, MtnMomoIntegration.ApiKey),
            ProviderSupport.Required(credentials, MtnMomoIntegration.Code, MtnMomoIntegration.SubscriptionKey),
            environment);
    }

    public async Task<string> TokenAsync(ProviderHttp http, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/collection/token/") { Content = new StringContent(string.Empty) };
        ProviderHttp.Basic(request, ApiUser, ApiKey);
        request.Headers.Add("Ocp-Apim-Subscription-Key", SubscriptionKey);
        var response = await http.SendAsync("MTN MoMo", "token", request, cancellationToken);
        return ProviderHttp.Text(response, "access_token") ?? throw new PaymentGatewayException("MTN MoMo token: no access token returned.");
    }

    public HttpRequestMessage Request(HttpMethod method, string path, string token, JsonNode? body = null)
    {
        var request = body is null ? new HttpRequestMessage(method, BaseUrl + path) : ProviderHttp.Json(method, BaseUrl + path, body);
        ProviderHttp.Bearer(request, token);
        request.Headers.Add("Ocp-Apim-Subscription-Key", SubscriptionKey);
        request.Headers.Add("X-Target-Environment", TargetEnvironment);
        return request;
    }
}

internal sealed class MtnMomoGateway(PaymentProviderCode provider, ProviderHttp http, PaymentsOptions options, MtnMomoApi api) : IPaymentGateway
{
    public PaymentProviderCode Provider => provider;

    public async Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payer = request.PayerPhone
            ?? throw new PaymentGatewayException("MTN MoMo: the payer phone number is required.", isPermanent: true);
        var reference = Guid.NewGuid().ToString("D");
        var token = await api.TokenAsync(http, cancellationToken);
        var message = api.Request(HttpMethod.Post, "/collection/v1_0/requesttopay", token, new JsonObject
        {
            ["amount"] = ProviderSupport.Decimal(request.Amount),
            ["currency"] = request.Amount.Currency.Code,
            ["externalId"] = reference,
            ["payer"] = new JsonObject { ["partyIdType"] = "MSISDN", ["partyId"] = payer },
            ["payerMessage"] = ProviderSupport.Label(request),
            ["payeeNote"] = ProviderSupport.Label(request),
        });
        message.Headers.Add("X-Reference-Id", reference);
        if (ProviderSupport.WebhookUrl(options, MtnMomoIntegration.Code, request.ConfigurationId) is { } callback
            && callback.StartsWith("https://", StringComparison.Ordinal))
        {
            message.Headers.Add("X-Callback-Url", callback);
        }

        _ = await http.SendAsync("MTN MoMo", "request to pay", message, cancellationToken);
        return new GatewayCheckout(ProviderReference.Create(reference), null);
    }

    public Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default) =>
        throw new PaymentGatewayException("MTN MoMo: refunds are made from the MTN MoMo partner portal.", isPermanent: true);

    public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default) =>
        ProviderSupport.CheckAsync(
            async () => _ = await api.TokenAsync(http, cancellationToken),
            api.TargetEnvironment == ProviderSupport.Sandbox,
            ProviderSupport.Warnings(options, needsHttpsApi: true));

    public async Task<GatewayTransactionStatus?> FetchStatusAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var token = await api.TokenAsync(http, cancellationToken);
        var transaction = await http.SendAsync(
            "MTN MoMo", "request to pay status", api.Request(HttpMethod.Get, $"/collection/v1_0/requesttopay/{reference.Value}", token), cancellationToken);
        var state = ProviderHttp.Text(transaction, "status") switch
        {
            "SUCCESSFUL" => GatewayTransactionState.Captured,
            "FAILED" or "REJECTED" or "TIMEOUT" => GatewayTransactionState.Failed,
            _ => GatewayTransactionState.Pending,
        };
        return new GatewayTransactionStatus(state, ProviderHttp.Number(transaction, "amount"), ProviderHttp.Text(transaction, "currency"));
    }
}

internal sealed class MtnMomoWebhookReader(ProviderHttp http, MtnMomoApi api) : IPaymentWebhookReader
{
    public string Provider => MtnMomoIntegration.Code;

    public async Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (ProviderSupport.ParseJson(body) is not { } root
            || !Guid.TryParse(ProviderHttp.Text(root, "externalId") ?? ProviderHttp.Text(root, "referenceId"), out var id))
        {
            return WebhookReadResult.Invalid;
        }

        var reference = id.ToString("D");
        var token = await api.TokenAsync(http, cancellationToken);
        var transaction = await http.SendAsync(
            "MTN MoMo", "request to pay status", api.Request(HttpMethod.Get, $"/collection/v1_0/requesttopay/{reference}", token), cancellationToken);
        var status = ProviderHttp.Text(transaction, "status");
        ProviderResultKind? kind = status switch
        {
            "SUCCESSFUL" => ProviderResultKind.Captured,
            "FAILED" or "REJECTED" or "TIMEOUT" => ProviderResultKind.Failed,
            _ => null,
        };
        if (kind is not { } known)
        {
            return WebhookReadResult.Ignored;
        }

        return WebhookReadResult.Verified(new VerifiedWebhook(
            $"{reference}:{status}",
            known,
            reference,
            ProviderHttp.Number(transaction, "amount"),
            ProviderHttp.Text(transaction, "currency"),
            Reason: known == ProviderResultKind.Failed ? ProviderHttp.Text(transaction, "reason") ?? status!.ToLowerInvariant() : null));
    }
}
