using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Gateways.Providers;

/// <summary>
/// Stripe (cartes, Apple Pay, Google Pay), sur le compte du restaurant : Stripe Checkout, page hébergée. Clé
/// <c>sk_test_…</c> ou <c>sk_live_…</c>. Webhooks déclarés dans le tableau de bord Stripe à l'adresse de la configuration,
/// signés (<c>Stripe-Signature: t=…,v1=…</c>, HMAC-SHA256 de « t.corps » avec le secret <c>whsec_…</c>). Événements :
/// <c>checkout.session.completed</c>, <c>checkout.session.async_payment_succeeded</c>/<c>_failed</c>,
/// <c>checkout.session.expired</c>, <c>refund.created</c>/<c>refund.updated</c>. La page de retour
/// (<c>Payments:CheckoutReturnUrl</c>) est obligatoire.
/// </summary>
internal sealed class StripeIntegration(ProviderHttp http, PaymentsOptions options) : IPaymentIntegration
{
    public const string Code = "stripe";
    public const string SecretKey = "secret_key";
    public const string WebhookSecret = "webhook_secret";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(Code);

    public string Name => "Stripe";

    public IReadOnlyList<string> CredentialFields { get; } = [SecretKey, WebhookSecret];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) =>
        new StripeGateway(Provider, http, options, ProviderSupport.Required(credentials, Code, SecretKey));

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        new StripeWebhookReader(ProviderSupport.Required(credentials, Code, WebhookSecret));
}

internal sealed class StripeGateway(PaymentProviderCode provider, ProviderHttp http, PaymentsOptions options, string secretKey) : IPaymentGateway
{
    private const string Api = "https://api.stripe.com/v1";

    public PaymentProviderCode Provider => provider;

    public async Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var back = ProviderSupport.ReturnUrl(options, request)
            ?? throw new PaymentGatewayException("Stripe: 'Payments:CheckoutReturnUrl' is not configured.", isPermanent: true);
        var payment = request.PaymentId.Value.ToString("D");
        var message = Post("/checkout/sessions", [
            new("mode", "payment"),
            new("success_url", back),
            new("cancel_url", back),
            new("client_reference_id", payment),
            new("line_items[0][quantity]", "1"),
            new("line_items[0][price_data][currency]", request.Amount.Currency.Code.ToLowerInvariant()),
            new("line_items[0][price_data][unit_amount]", ProviderSupport.MinorUnits(request.Amount).ToString(CultureInfo.InvariantCulture)),
            new("line_items[0][price_data][product_data][name]", ProviderSupport.Label(request)),
            new("metadata[payment_id]", payment),
            new("payment_intent_data[metadata][payment_id]", payment),
        ]);
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var session = await http.SendAsync("Stripe", "create checkout session", message, cancellationToken);
        var id = ProviderHttp.Text(session, "id") ?? throw new PaymentGatewayException("Stripe create checkout session: no id returned.");
        return new GatewayCheckout(ProviderReference.Create(id), Uri.TryCreate(ProviderHttp.Text(session, "url"), UriKind.Absolute, out var url) ? url : null);
    }

    public async Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        _ = await http.SendAsync("Stripe", "expire checkout session", Post($"/checkout/sessions/{Uri.EscapeDataString(reference.Value)}/expire", []), cancellationToken);
    }

    public async Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var get = new HttpRequestMessage(HttpMethod.Get, $"{Api}/checkout/sessions/{Uri.EscapeDataString(request.CapturedReference.Value)}");
        ProviderHttp.Bearer(get, secretKey);
        var session = await http.SendAsync("Stripe", "get checkout session", get, cancellationToken);
        var intent = ProviderHttp.Text(session, "payment_intent")
            ?? throw new PaymentGatewayException("Stripe refund: the session has no payment.", isPermanent: true);
        var message = Post("/refunds", [
            new("payment_intent", intent),
            new("amount", ProviderSupport.MinorUnits(request.Amount).ToString(CultureInfo.InvariantCulture)),
            new("metadata[refund_id]", request.RefundId.Value.ToString("D")),
            new("metadata[reference]", request.CapturedReference.Value),
        ]);
        message.Headers.Add("Idempotency-Key", "refund-" + request.RefundId.Value.ToString("D"));
        _ = await http.SendAsync("Stripe", "create refund", message, cancellationToken);
    }

    public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var get = new HttpRequestMessage(HttpMethod.Get, Api + "/balance");
        ProviderHttp.Bearer(get, secretKey);
        return ProviderSupport.CheckAsync(
            async () => _ = await http.SendAsync("Stripe", "balance", get, cancellationToken),
            secretKey.Contains("_test_", StringComparison.Ordinal),
            ProviderSupport.Warnings(options, needsReturnUrl: true));
    }

    private HttpRequestMessage Post(string path, KeyValuePair<string, string>[] fields)
    {
        var message = ProviderHttp.Form(Api + path, fields);
        ProviderHttp.Bearer(message, secretKey);
        return message;
    }
}

internal sealed class StripeWebhookReader(string secret) : IPaymentWebhookReader
{
    public const string Header = "Stripe-Signature";
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public string Provider => StripeIntegration.Code;

    public Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(headers, body, nowUtc));

    internal WebhookReadResult Read(IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc)
    {
        if (!Verify(ProviderSupport.Header(headers, Header), body, secret, nowUtc) || ProviderSupport.ParseJson(body) is not { } root)
        {
            return WebhookReadResult.Invalid;
        }

        var eventId = ProviderHttp.Text(root, "id");
        var data = root["data"]?["object"];
        if (eventId is not { Length: > 0 and <= PaymentWebhook.MaxEventIdLength } || data is null)
        {
            return WebhookReadResult.Invalid;
        }

        switch (ProviderHttp.Text(root, "type"))
        {
            case "checkout.session.completed" when ProviderHttp.Text(data, "payment_status") == "paid":
            case "checkout.session.async_payment_succeeded":
                return Session(eventId, data, ProviderResultKind.Captured, null);
            case "checkout.session.async_payment_failed":
                return Session(eventId, data, ProviderResultKind.Failed, "payment_failed");
            case "checkout.session.expired":
                return Session(eventId, data, ProviderResultKind.Failed, "expired");
            case "refund.created" or "refund.updated":
                var kind = ProviderHttp.Text(data, "status") switch
                {
                    "succeeded" => ProviderResultKind.RefundSucceeded,
                    "failed" or "canceled" => ProviderResultKind.RefundFailed,
                    _ => (ProviderResultKind?)null,
                };
                var metadata = data["metadata"];
                if (kind is not { } refund
                    || ProviderHttp.Text(metadata, "reference") is not { Length: > 0 } reference
                    || !Guid.TryParse(ProviderHttp.Text(metadata, "refund_id"), out var refundId))
                {
                    return WebhookReadResult.Ignored;
                }

                return WebhookReadResult.Verified(new VerifiedWebhook(
                    eventId, refund, reference, Amount(data, "amount"), Currency(data), refundId, ProviderHttp.Text(data, "failure_reason")));
            default:
                return WebhookReadResult.Ignored;
        }
    }

    private static WebhookReadResult Session(string eventId, JsonNode session, ProviderResultKind kind, string? reason) =>
        ProviderHttp.Text(session, "id") is { Length: > 0 } reference
            ? WebhookReadResult.Verified(new VerifiedWebhook(eventId, kind, reference, Amount(session, "amount_total"), Currency(session), Reason: reason))
            : WebhookReadResult.Invalid;

    private static string? Currency(JsonNode node) => ProviderHttp.Text(node, "currency")?.ToUpperInvariant();

    private static decimal? Amount(JsonNode node, string field) =>
        ProviderHttp.Number(node, field) is { } minor && Currency(node) is { } currency ? ProviderSupport.FromMinorUnits((long)minor, currency) : null;

    /// <summary>En-tête <c>t=&lt;secondes&gt;,v1=&lt;hex&gt;</c> : HMAC-SHA256(secret, « t.corps »), 5 minutes de tolérance.</summary>
    internal static bool Verify(string? header, string body, string secret, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "t" && long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                timestamp = value;
            }
            else if (pair.Length == 2 && pair[0] == "v1")
            {
                signatures.Add(pair[1]);
            }
        }

        if (timestamp is not { } t || Math.Abs(nowUtc.ToUnixTimeSeconds() - t) > (long)Tolerance.TotalSeconds)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secret, t, body));
        return signatures.Any(signature => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(signature), expected));
    }

    internal static string Sign(string secret, long timestamp, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp.ToString(CultureInfo.InvariantCulture)}.{body}")));
}
