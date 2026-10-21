using System.Text.Json.Nodes;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Gateways.Providers;

/// <summary>
/// PayPal (API Orders v2), sur le compte du restaurant : identifiant et secret de son application REST, id du webhook
/// déclaré dans son tableau de bord développeur (à l'adresse de la configuration) et environnement (<c>sandbox</c> ou
/// <c>live</c>). Le client approuve sur la page PayPal ; à la notification <c>CHECKOUT.ORDER.APPROVED</c> la commande PayPal
/// est capturée. Chaque notification est authentifiée par PayPal lui-même (vérification de signature côté PayPal).
/// Remboursement par l'API, à partir de la capture.
/// </summary>
internal sealed class PayPalIntegration(ProviderHttp http, PaymentsOptions options) : IPaymentIntegration
{
    public const string Code = "paypal";
    public const string ClientId = "client_id";
    public const string ClientSecret = "client_secret";
    public const string WebhookId = "webhook_id";
    public const string WebhookUnknown = "webhook_id_unknown";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(Code);

    public string Name => "PayPal";

    public IReadOnlyList<string> CredentialFields { get; } = [ClientId, ClientSecret, WebhookId, ProviderSupport.Environment];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) =>
        new PayPalGateway(Provider, http, options, PayPalApi.From(credentials), ProviderSupport.Optional(credentials, WebhookId));

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        new PayPalWebhookReader(http, PayPalApi.From(credentials), ProviderSupport.Required(credentials, Code, WebhookId));
}

internal sealed record PayPalApi(string ClientId, string ClientSecret, bool Sandbox)
{
    public string BaseUrl => Sandbox ? "https://api-m.sandbox.paypal.com" : "https://api-m.paypal.com";

    public static PayPalApi From(IReadOnlyDictionary<string, string> credentials) =>
        new(ProviderSupport.Required(credentials, PayPalIntegration.Code, PayPalIntegration.ClientId),
            ProviderSupport.Required(credentials, PayPalIntegration.Code, PayPalIntegration.ClientSecret),
            ProviderSupport.IsSandbox(credentials));

    public async Task<string> TokenAsync(ProviderHttp http, CancellationToken cancellationToken)
    {
        var request = ProviderHttp.Form(BaseUrl + "/v1/oauth2/token", [new("grant_type", "client_credentials")]);
        ProviderHttp.Basic(request, ClientId, ClientSecret);
        var response = await http.SendAsync("PayPal", "token", request, cancellationToken);
        return ProviderHttp.Text(response, "access_token") ?? throw new PaymentGatewayException("PayPal token: no access token returned.");
    }

    public HttpRequestMessage Request(HttpMethod method, string path, string token, JsonNode? body = null, string? requestId = null)
    {
        var request = body is null ? new HttpRequestMessage(method, BaseUrl + path) : ProviderHttp.Json(method, BaseUrl + path, body);
        ProviderHttp.Bearer(request, token);
        if (requestId is not null)
        {
            request.Headers.Add("PayPal-Request-Id", requestId);
        }

        return request;
    }

    /// <summary>La capture réussie d'une commande PayPal, ou null.</summary>
    public static JsonNode? Capture(JsonNode? order) =>
        (order?["purchase_units"] as JsonArray)?.FirstOrDefault()?["payments"]?["captures"] is JsonArray captures
            ? captures.FirstOrDefault(capture => ProviderHttp.Text(capture, "status") == "COMPLETED")
            : null;
}

internal sealed class PayPalGateway(PaymentProviderCode provider, ProviderHttp http, PaymentsOptions options, PayPalApi api, string? webhookId = null) : IPaymentGateway
{
    public PaymentProviderCode Provider => provider;

    public async Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var back = ProviderSupport.ReturnUrl(options, request)
            ?? throw new PaymentGatewayException("PayPal: 'Payments:CheckoutReturnUrl' is not configured.", isPermanent: true);
        var payment = request.PaymentId.Value.ToString("D");
        var token = await api.TokenAsync(http, cancellationToken);
        var order = await http.SendAsync("PayPal", "create order", api.Request(HttpMethod.Post, "/v2/checkout/orders", token, new JsonObject
        {
            ["intent"] = "CAPTURE",
            ["purchase_units"] = new JsonArray(new JsonObject
            {
                ["reference_id"] = payment,
                ["custom_id"] = payment,
                ["description"] = ProviderSupport.Label(request),
                ["amount"] = new JsonObject { ["currency_code"] = request.Amount.Currency.Code, ["value"] = ProviderSupport.Decimal(request.Amount) },
            }),
            ["payment_source"] = new JsonObject
            {
                ["paypal"] = new JsonObject
                {
                    ["experience_context"] = new JsonObject
                    {
                        ["return_url"] = back,
                        ["cancel_url"] = back,
                        ["user_action"] = "PAY_NOW",
                        ["shipping_preference"] = "NO_SHIPPING",
                    },
                },
            },
        }, Guid.NewGuid().ToString("D")), cancellationToken);
        var id = ProviderHttp.Text(order, "id") ?? throw new PaymentGatewayException("PayPal create order: no id returned.");
        var link = (order["links"] as JsonArray)?
            .FirstOrDefault(item => ProviderHttp.Text(item, "rel") is "payer-action" or "approve") is { } action
            ? ProviderHttp.Text(action, "href")
            : null;
        return new GatewayCheckout(ProviderReference.Create(id), Uri.TryCreate(link, UriKind.Absolute, out var url) ? url : null);
    }

    public Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Un jeton OAuth, puis le webhook déclaré : un id de webhook inconnu de ce compte est signalé.</summary>
    public async Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var warnings = ProviderSupport.Warnings(options, needsReturnUrl: true);
        string? token = null;
        var check = await ProviderSupport.CheckAsync(
            async () => token = await api.TokenAsync(http, cancellationToken), api.Sandbox, warnings);
        if (check.Status != GatewayCredentialStatus.Verified || token is null || webhookId is null)
        {
            return check;
        }

        try
        {
            _ = await http.SendAsync(
                "PayPal", "get webhook", api.Request(HttpMethod.Get, $"/v1/notifications/webhooks/{Uri.EscapeDataString(webhookId)}", token), cancellationToken);
        }
        catch (PaymentGatewayException exception) when (exception.StatusCode is 400 or 404)
        {
            warnings.Add(PayPalIntegration.WebhookUnknown);
        }

        return check;
    }

    public async Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = await api.TokenAsync(http, cancellationToken);
        var order = await http.SendAsync(
            "PayPal", "get order", api.Request(HttpMethod.Get, $"/v2/checkout/orders/{Uri.EscapeDataString(request.CapturedReference.Value)}", token), cancellationToken);
        var capture = ProviderHttp.Text(PayPalApi.Capture(order), "id")
            ?? throw new PaymentGatewayException("PayPal refund: the order has no completed capture.", isPermanent: true);
        _ = await http.SendAsync("PayPal", "refund capture", api.Request(HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(capture)}/refund", token, new JsonObject
        {
            ["amount"] = new JsonObject { ["currency_code"] = request.Amount.Currency.Code, ["value"] = ProviderSupport.Decimal(request.Amount) },
            ["invoice_id"] = request.RefundId.Value.ToString("D"),
            ["custom_id"] = request.CapturedReference.Value,
        }, "refund-" + request.RefundId.Value.ToString("D")), cancellationToken);
    }
}

internal sealed class PayPalWebhookReader(ProviderHttp http, PayPalApi api, string webhookId) : IPaymentWebhookReader
{
    private static readonly string[] SignatureHeaders =
        ["PAYPAL-AUTH-ALGO", "PAYPAL-CERT-URL", "PAYPAL-TRANSMISSION-ID", "PAYPAL-TRANSMISSION-SIG", "PAYPAL-TRANSMISSION-TIME"];

    public string Provider => PayPalIntegration.Code;

    public async Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var values = SignatureHeaders.Select(name => ProviderSupport.Header(headers, name)).ToArray();
        if (values.Any(string.IsNullOrEmpty) || ProviderSupport.ParseJson(body) is not { } root)
        {
            return WebhookReadResult.Invalid;
        }

        var token = await api.TokenAsync(http, cancellationToken);
        var verification = await http.SendAsync("PayPal", "verify webhook", api.Request(HttpMethod.Post, "/v1/notifications/verify-webhook-signature", token, new JsonObject
        {
            ["auth_algo"] = values[0],
            ["cert_url"] = values[1],
            ["transmission_id"] = values[2],
            ["transmission_sig"] = values[3],
            ["transmission_time"] = values[4],
            ["webhook_id"] = webhookId,
            ["webhook_event"] = root.DeepClone(),
        }), cancellationToken);
        if (ProviderHttp.Text(verification, "verification_status") != "SUCCESS")
        {
            return WebhookReadResult.Invalid;
        }

        var eventId = ProviderHttp.Text(root, "id");
        var resource = root["resource"];
        if (eventId is not { Length: > 0 and <= PaymentWebhook.MaxEventIdLength } || resource is null)
        {
            return WebhookReadResult.Invalid;
        }

        switch (ProviderHttp.Text(root, "event_type"))
        {
            case "CHECKOUT.ORDER.APPROVED" when ProviderHttp.Text(resource, "id") is { Length: > 0 } orderId:
                return await CaptureAsync(eventId, orderId, token, cancellationToken);
            case "PAYMENT.CAPTURE.COMPLETED" when OrderOf(resource) is { } orderId:
                return Verified(eventId, ProviderResultKind.Captured, orderId, resource["amount"]);
            case "PAYMENT.CAPTURE.DENIED" or "PAYMENT.CAPTURE.DECLINED" when OrderOf(resource) is { } orderId:
                return Verified(eventId, ProviderResultKind.Failed, orderId, null, "denied");
            case "PAYMENT.CAPTURE.REFUNDED" or "PAYMENT.REFUND.COMPLETED"
                when ProviderHttp.Text(resource, "custom_id") is { Length: > 0 } orderId && Guid.TryParse(ProviderHttp.Text(resource, "invoice_id"), out var refundId):
                return Verified(eventId, ProviderResultKind.RefundSucceeded, orderId, resource["amount"], refundId: refundId);
            case "PAYMENT.REFUND.FAILED" or "PAYMENT.REFUND.DENIED"
                when ProviderHttp.Text(resource, "custom_id") is { Length: > 0 } orderId && Guid.TryParse(ProviderHttp.Text(resource, "invoice_id"), out var refundId):
                return Verified(eventId, ProviderResultKind.RefundFailed, orderId, null, "refund_failed", refundId);
            default:
                return WebhookReadResult.Ignored;
        }
    }

    /// <summary>Commande approuvée par le client : on la capture (idempotent côté PayPal par la clé de requête).</summary>
    private async Task<WebhookReadResult> CaptureAsync(string eventId, string orderId, string token, CancellationToken cancellationToken)
    {
        JsonNode order;
        try
        {
            order = await http.SendAsync(
                "PayPal", "capture order",
                api.Request(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture", token, new JsonObject(), "capture-" + orderId),
                cancellationToken);
        }
        catch (PaymentGatewayException exception) when (exception.IsPermanent)
        {
            // Déjà capturée ou refusée : la notification PAYMENT.CAPTURE.* qui suit porte le résultat.
            return WebhookReadResult.Ignored;
        }

        return PayPalApi.Capture(order) is { } capture
            ? Verified(eventId, ProviderResultKind.Captured, orderId, capture["amount"])
            : WebhookReadResult.Ignored;
    }

    private static string? OrderOf(JsonNode resource) => ProviderHttp.Text(resource["supplementary_data"]?["related_ids"], "order_id");

    private static WebhookReadResult Verified(
        string eventId, ProviderResultKind kind, string orderId, JsonNode? amount, string? reason = null, Guid? refundId = null) =>
        WebhookReadResult.Verified(new VerifiedWebhook(
            eventId, kind, orderId, ProviderHttp.Number(amount, "value"), ProviderHttp.Text(amount, "currency_code"), refundId, reason));
}
