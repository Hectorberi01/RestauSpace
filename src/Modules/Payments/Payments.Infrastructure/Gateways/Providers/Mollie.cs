using System.Net;
using System.Text.Json.Nodes;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Gateways.Providers;

/// <summary>
/// Mollie (Europe : cartes, Apple Pay, iDEAL, Bancontact, SEPA…), sur le compte du restaurant. Clé d'API <c>test_…</c> ou
/// <c>live_…</c>. Le client paie sur la page Mollie. Mollie ne signe pas ses webhooks : il n'envoie que l'id du paiement
/// (<c>id=tr_…</c>), relu aussitôt avec notre clé, ce qui fait foi. Remboursement par API, porteur de NOTRE id de
/// remboursement (<c>metadata.refund_id</c>) ; son résultat revient par le même webhook.
/// </summary>
internal sealed class MollieIntegration(ProviderHttp http, PaymentsOptions options) : IPaymentIntegration
{
    public const string Code = "mollie";
    public const string ApiKey = "api_key";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(Code);

    public string Name => "Mollie";

    public IReadOnlyList<string> CredentialFields { get; } = [ApiKey];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) =>
        new MollieGateway(Provider, http, options, MollieApi.From(credentials));

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        new MollieWebhookReader(http, MollieApi.From(credentials));
}

internal sealed record MollieApi(string Key)
{
    public const string BaseUrl = "https://api.mollie.com/v2";

    public bool IsSandbox => Key.StartsWith("test_", StringComparison.Ordinal);

    public static MollieApi From(IReadOnlyDictionary<string, string> credentials) =>
        new(ProviderSupport.Required(credentials, MollieIntegration.Code, MollieIntegration.ApiKey));

    public HttpRequestMessage Request(HttpMethod method, string path, JsonNode? body = null)
    {
        var request = body is null ? new HttpRequestMessage(method, BaseUrl + path) : ProviderHttp.Json(method, BaseUrl + path, body);
        ProviderHttp.Bearer(request, Key);
        return request;
    }

    /// <summary>Le paiement et ses remboursements (<c>_embedded.refunds</c>).</summary>
    public Task<JsonNode> PaymentAsync(ProviderHttp http, string id, CancellationToken cancellationToken) =>
        http.SendAsync("Mollie", "get payment", Request(HttpMethod.Get, $"/payments/{Uri.EscapeDataString(id)}?embed=refunds"), cancellationToken);

    public static GatewayTransactionState State(JsonNode? payment) => ProviderHttp.Text(payment, "status") switch
    {
        "paid" => GatewayTransactionState.Captured,
        "failed" or "canceled" or "expired" => GatewayTransactionState.Failed,
        _ => GatewayTransactionState.Pending,
    };

    public static (decimal? Amount, string? Currency) Amount(JsonNode? node) =>
        (ProviderHttp.Number(node?["amount"], "value"), ProviderHttp.Text(node?["amount"], "currency"));

    public static bool IsPaymentId(string? value) =>
        value is { Length: > 3 and <= 64 } && value.StartsWith("tr_", StringComparison.Ordinal) && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}

internal sealed class MollieGateway(PaymentProviderCode provider, ProviderHttp http, PaymentsOptions options, MollieApi api) : IPaymentGateway
{
    public PaymentProviderCode Provider => provider;

    public async Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var back = ProviderSupport.ReturnUrl(options, request)
            ?? throw new PaymentGatewayException("Mollie: a return URL is required.", isPermanent: true);
        var body = new JsonObject
        {
            ["amount"] = new JsonObject { ["currency"] = request.Amount.Currency.Code, ["value"] = ProviderSupport.Decimal(request.Amount) },
            ["description"] = ProviderSupport.Label(request),
            ["redirectUrl"] = back,
            ["metadata"] = new JsonObject { ["payment_id"] = request.PaymentId.Value.ToString("D") },
        };

        // Mollie n'appelle qu'une adresse publique en HTTPS ; sans elle, l'état est relu par le job.
        if (ProviderSupport.WebhookUrl(options, MollieIntegration.Code, request.ConfigurationId) is { } notify
            && notify.StartsWith("https://", StringComparison.Ordinal))
        {
            body["webhookUrl"] = notify;
        }

        var created = await http.SendAsync("Mollie", "create payment", api.Request(HttpMethod.Post, "/payments", body), cancellationToken);
        var id = ProviderHttp.Text(created, "id") ?? throw new PaymentGatewayException("Mollie create payment: no id returned.");
        var url = ProviderHttp.Text(created["_links"]?["checkout"], "href");
        return new GatewayCheckout(ProviderReference.Create(id), Uri.TryCreate(url, UriKind.Absolute, out var checkout) ? checkout : null);
    }

    public async Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        try
        {
            await http.SendAsync("Mollie", "cancel payment", api.Request(HttpMethod.Delete, $"/payments/{Uri.EscapeDataString(reference.Value)}"), cancellationToken);
        }
        catch (PaymentGatewayException exception) when (exception.StatusCode is 404 or 422)
        {
            // Déjà payé, expiré ou non annulable : rien à faire.
        }
    }

    public async Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new JsonObject
        {
            ["amount"] = new JsonObject { ["currency"] = request.Amount.Currency.Code, ["value"] = ProviderSupport.Decimal(request.Amount) },
            ["metadata"] = new JsonObject { ["refund_id"] = request.RefundId.Value.ToString("D") },
        };
        var message = api.Request(HttpMethod.Post, $"/payments/{Uri.EscapeDataString(request.CapturedReference.Value)}/refunds", body);
        message.Headers.Add("Idempotency-Key", "refund-" + request.RefundId.Value.ToString("D"));
        _ = await http.SendAsync("Mollie", "create refund", message, cancellationToken);
    }

    public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default) =>
        ProviderSupport.CheckAsync(
            async () => _ = await http.SendAsync("Mollie", "list methods", api.Request(HttpMethod.Get, "/methods"), cancellationToken),
            api.IsSandbox,
            ProviderSupport.Warnings(options, needsReturnUrl: true, needsHttpsApi: true));

    public async Task<GatewayTransactionStatus?> FetchStatusAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var payment = await api.PaymentAsync(http, reference.Value, cancellationToken);
        var (amount, currency) = MollieApi.Amount(payment);
        return new GatewayTransactionStatus(MollieApi.State(payment), amount, currency);
    }
}

internal sealed class MollieWebhookReader(ProviderHttp http, MollieApi api) : IPaymentWebhookReader
{
    public string Provider => MollieIntegration.Code;

    /// <summary>
    /// Le corps ne porte que l'id : le paiement relu dit ce qui a changé. Un remboursement terminé (<c>refunded</c> ou
    /// <c>failed</c>) qui porte notre id l'emporte ; sinon l'état du paiement.
    /// </summary>
    public async Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var id = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == "id")
            .Select(pair => WebUtility.UrlDecode(pair[1]))
            .FirstOrDefault();
        if (!MollieApi.IsPaymentId(id))
        {
            return WebhookReadResult.Invalid;
        }

        JsonNode payment;
        try
        {
            payment = await api.PaymentAsync(http, id!, cancellationToken);
        }
        catch (PaymentGatewayException exception) when (exception.StatusCode == 404)
        {
            // Un id inventé : Mollie ne le connaît pas avec notre clé.
            return WebhookReadResult.Invalid;
        }

        var refunds = (payment["_embedded"]?["refunds"] as JsonArray)?.OfType<JsonNode>() ?? [];
        foreach (var refund in refunds.Reverse())
        {
            var refundStatus = ProviderHttp.Text(refund, "status");
            if (refundStatus is "refunded" or "failed" && Guid.TryParse(ProviderHttp.Text(refund["metadata"], "refund_id"), out var refundId))
            {
                var (refunded, refundCurrency) = MollieApi.Amount(refund);
                return WebhookReadResult.Verified(new VerifiedWebhook(
                    $"{ProviderHttp.Text(refund, "id")}:{refundStatus}",
                    refundStatus == "refunded" ? ProviderResultKind.RefundSucceeded : ProviderResultKind.RefundFailed,
                    id!, refunded, refundCurrency, refundId, refundStatus == "failed" ? "failed" : null));
            }
        }

        var status = ProviderHttp.Text(payment, "status");
        ProviderResultKind? kind = MollieApi.State(payment) switch
        {
            GatewayTransactionState.Captured => ProviderResultKind.Captured,
            GatewayTransactionState.Failed => ProviderResultKind.Failed,
            _ => null,
        };
        var (amount, currency) = MollieApi.Amount(payment);
        return kind is { } known
            ? WebhookReadResult.Verified(new VerifiedWebhook($"{id}:{status}", known, id!, amount, currency, Reason: known == ProviderResultKind.Failed ? status : null))
            : WebhookReadResult.Ignored;
    }
}
