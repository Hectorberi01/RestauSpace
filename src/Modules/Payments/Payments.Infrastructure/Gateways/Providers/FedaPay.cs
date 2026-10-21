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
/// FedaPay (Bénin, zone UEMOA : mobile money et cartes), sur le compte du restaurant. Clé secrète <c>sk_sandbox_…</c> ou
/// <c>sk_live_…</c> : l'environnement en découle. Le client paie sur la page de FedaPay (lien de la transaction). Les
/// webhooks se déclarent dans le tableau de bord FedaPay, à l'adresse de la configuration ; ils sont signés
/// (<c>X-FEDAPAY-SIGNATURE: t=…,s=…</c>, HMAC-SHA256 de « t.corps ») puis la transaction est RELUE chez FedaPay, qui seule
/// fait foi. Pas d'API de remboursement : il se fait depuis le tableau de bord FedaPay.
/// </summary>
internal sealed class FedaPayIntegration(ProviderHttp http, PaymentsOptions options) : IPaymentIntegration
{
    public const string Code = "fedapay";
    public const string SecretKey = "secret_key";
    public const string WebhookSecret = "webhook_secret";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(Code);

    public string Name => "FedaPay";

    public IReadOnlyList<string> CredentialFields { get; } = [SecretKey, WebhookSecret];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) =>
        new FedaPayGateway(Provider, http, options, FedaPayApi.From(credentials));

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        new FedaPayWebhookReader(http, FedaPayApi.From(credentials), ProviderSupport.Required(credentials, Code, WebhookSecret));
}

internal sealed record FedaPayApi(string BaseUrl, string SecretKey)
{
    public static FedaPayApi From(IReadOnlyDictionary<string, string> credentials)
    {
        var key = ProviderSupport.Required(credentials, FedaPayIntegration.Code, FedaPayIntegration.SecretKey);
        return new FedaPayApi(
            key.StartsWith("sk_live_", StringComparison.Ordinal) ? "https://api.fedapay.com/v1" : "https://sandbox-api.fedapay.com/v1", key);
    }

    public HttpRequestMessage Request(HttpMethod method, string path, JsonNode? body = null)
    {
        var request = body is null ? new HttpRequestMessage(method, BaseUrl + path) : ProviderHttp.Json(method, BaseUrl + path, body);
        ProviderHttp.Bearer(request, SecretKey);
        return request;
    }

    /// <summary>La transaction, qu'elle soit renvoyée à plat ou sous la clé <c>v1/transaction</c>.</summary>
    public static JsonNode? Transaction(JsonNode response) => response["v1/transaction"] ?? response;

    public async Task<JsonNode?> GetAsync(ProviderHttp http, string id, CancellationToken cancellationToken) =>
        Transaction(await http.SendAsync("FedaPay", "get transaction", Request(HttpMethod.Get, $"/transactions/{Uri.EscapeDataString(id)}"), cancellationToken));
}

internal sealed class FedaPayGateway(PaymentProviderCode provider, ProviderHttp http, PaymentsOptions options, FedaPayApi api) : IPaymentGateway
{
    public PaymentProviderCode Provider => provider;

    public async Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new JsonObject
        {
            ["description"] = ProviderSupport.Label(request),
            ["amount"] = ProviderSupport.MinorUnits(request.Amount),
            ["currency"] = new JsonObject { ["iso"] = request.Amount.Currency.Code },
            ["custom_metadata"] = new JsonObject { ["payment_id"] = request.PaymentId.Value.ToString("D"), ["currency"] = request.Amount.Currency.Code },
        };
        if (ProviderSupport.ReturnUrl(options, request) is { } back)
        {
            body["callback_url"] = back;
        }

        var created = FedaPayApi.Transaction(await http.SendAsync("FedaPay", "create transaction", api.Request(HttpMethod.Post, "/transactions", body), cancellationToken));
        var id = ProviderHttp.Text(created, "id") ?? throw new PaymentGatewayException("FedaPay create transaction: no id returned.");
        var token = await http.SendAsync("FedaPay", "create token", api.Request(HttpMethod.Post, $"/transactions/{id}/token", new JsonObject()), cancellationToken);
        var url = ProviderHttp.Text(token, "url");
        return new GatewayCheckout(ProviderReference.Create(id), Uri.TryCreate(url, UriKind.Absolute, out var checkout) ? checkout : null);
    }

    public Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default) =>
        throw new PaymentGatewayException("FedaPay: refunds are made from the FedaPay dashboard.", isPermanent: true);

    public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default) =>
        ProviderSupport.CheckAsync(
            async () => _ = await http.SendAsync("FedaPay", "list transactions", api.Request(HttpMethod.Get, "/transactions?per_page=1"), cancellationToken),
            !api.SecretKey.StartsWith("sk_live_", StringComparison.Ordinal),
            ProviderSupport.Warnings(options));

    public async Task<GatewayTransactionStatus?> FetchStatusAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var transaction = await api.GetAsync(http, reference.Value, cancellationToken);
        var currency = ProviderHttp.Text(transaction?["currency"], "iso") ?? ProviderHttp.Text(transaction?["custom_metadata"], "currency");
        var amount = ProviderHttp.Number(transaction, "amount");
        var state = ProviderHttp.Text(transaction, "status") switch
        {
            "approved" or "transferred" => GatewayTransactionState.Captured,
            "declined" or "canceled" or "cancelled" or "expired" => GatewayTransactionState.Failed,
            _ => GatewayTransactionState.Pending,
        };
        return new GatewayTransactionStatus(
            state, amount is { } minor && currency is not null ? ProviderSupport.FromMinorUnits((long)minor, currency) : null, currency);
    }
}

internal sealed class FedaPayWebhookReader(ProviderHttp http, FedaPayApi api, string secret) : IPaymentWebhookReader
{
    public const string Header = "X-FEDAPAY-SIGNATURE";
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public string Provider => FedaPayIntegration.Code;

    public async Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (!Verify(ProviderSupport.Header(headers, Header), body, secret, nowUtc) || ProviderSupport.ParseJson(body) is not { } root)
        {
            return WebhookReadResult.Invalid;
        }

        var entity = root["entity"] ?? root["object"] ?? root["data"];
        if (ProviderHttp.Text(entity, "id") is not { Length: > 0 } id || !id.All(char.IsAsciiDigit))
        {
            return WebhookReadResult.Ignored;
        }

        var transaction = await api.GetAsync(http, id, cancellationToken);
        var status = ProviderHttp.Text(transaction, "status");
        ProviderResultKind? kind = status switch
        {
            "approved" or "transferred" => ProviderResultKind.Captured,
            "declined" or "canceled" or "cancelled" or "expired" => ProviderResultKind.Failed,
            _ => null,
        };
        if (kind is not { } known)
        {
            return WebhookReadResult.Ignored;
        }

        var currency = ProviderHttp.Text(transaction?["currency"], "iso") ?? ProviderHttp.Text(transaction?["custom_metadata"], "currency");
        var amount = ProviderHttp.Number(transaction, "amount");
        return WebhookReadResult.Verified(new VerifiedWebhook(
            $"{id}:{status}",
            known,
            id,
            amount is { } minor && currency is not null ? ProviderSupport.FromMinorUnits((long)minor, currency) : null,
            currency,
            Reason: known == ProviderResultKind.Failed ? status : null));
    }

    /// <summary>En-tête <c>t=&lt;secondes&gt;,s=&lt;hex&gt;</c> : HMAC-SHA256(secret, « t.corps »), 5 minutes de tolérance.</summary>
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
            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0] == "t" && long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                timestamp = value;
            }
            else if (pair[0] == "s")
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
