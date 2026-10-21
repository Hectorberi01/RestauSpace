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
/// Wave (Sénégal, Côte d'Ivoire et voisins : portefeuille mobile, XOF), sur le compte marchand du restaurant. Clé d'API
/// <c>wave_sn_prod_…</c> ou <c>wave_ci_prod_…</c> (Bearer) et secret de webhook du tableau de bord. Le client paie dans
/// l'application Wave (lien <c>wave_launch_url</c> de la session de paiement). Les webhooks sont signés
/// (<c>Wave-Signature: t=…,v1=…</c>, HMAC-SHA256 de « t » suivi du corps) ; la session est RELUE chez Wave, qui seule
/// fait foi. Remboursement complet par API.
/// </summary>
internal sealed class WaveIntegration(ProviderHttp http, PaymentsOptions options) : IPaymentIntegration
{
    public const string Code = "wave";
    public const string ApiKey = "api_key";
    public const string WebhookSecret = "webhook_secret";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(Code);

    public string Name => "Wave";

    public IReadOnlyList<string> CredentialFields { get; } = [ApiKey, WebhookSecret];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) =>
        new WaveGateway(Provider, http, options, WaveApi.From(credentials));

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        new WaveWebhookReader(http, WaveApi.From(credentials), ProviderSupport.Required(credentials, Code, WebhookSecret));
}

internal sealed record WaveApi(string Key)
{
    public const string BaseUrl = "https://api.wave.com/v1";

    public static WaveApi From(IReadOnlyDictionary<string, string> credentials) =>
        new(ProviderSupport.Required(credentials, WaveIntegration.Code, WaveIntegration.ApiKey));

    public HttpRequestMessage Request(HttpMethod method, string path, JsonNode? body = null)
    {
        var request = body is null ? new HttpRequestMessage(method, BaseUrl + path) : ProviderHttp.Json(method, BaseUrl + path, body);
        ProviderHttp.Bearer(request, Key);
        return request;
    }

    public Task<JsonNode> SessionAsync(ProviderHttp http, string id, CancellationToken cancellationToken) =>
        http.SendAsync("Wave", "get checkout session", Request(HttpMethod.Get, $"/checkout/sessions/{Uri.EscapeDataString(id)}"), cancellationToken);

    /// <summary>Payée quand le paiement a réussi ; échouée si la session a expiré ou si le paiement a été annulé.</summary>
    public static GatewayTransactionState State(JsonNode? session) =>
        (ProviderHttp.Text(session, "payment_status"), ProviderHttp.Text(session, "checkout_status")) switch
        {
            ("succeeded", _) => GatewayTransactionState.Captured,
            ("cancelled", _) or (_, "expired") => GatewayTransactionState.Failed,
            _ => GatewayTransactionState.Pending,
        };

    public static bool IsSessionId(string? value) =>
        value is { Length: > 0 and <= 100 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

internal sealed class WaveGateway(PaymentProviderCode provider, ProviderHttp http, PaymentsOptions options, WaveApi api) : IPaymentGateway
{
    public PaymentProviderCode Provider => provider;

    public async Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var back = ProviderSupport.ReturnUrl(options, request)
            ?? throw new PaymentGatewayException("Wave: a return URL is required.", isPermanent: true);
        var body = new JsonObject
        {
            ["amount"] = ProviderSupport.Decimal(request.Amount),
            ["currency"] = request.Amount.Currency.Code,
            ["client_reference"] = request.PaymentId.Value.ToString("D"),
            ["success_url"] = back,
            ["error_url"] = back,
        };

        var session = await http.SendAsync("Wave", "create checkout session", api.Request(HttpMethod.Post, "/checkout/sessions", body), cancellationToken);
        var id = ProviderHttp.Text(session, "id");
        if (!WaveApi.IsSessionId(id))
        {
            throw new PaymentGatewayException("Wave create checkout session: no session id returned.");
        }

        var url = ProviderHttp.Text(session, "wave_launch_url");
        return new GatewayCheckout(ProviderReference.Create(id!), Uri.TryCreate(url, UriKind.Absolute, out var checkout) ? checkout : null);
    }

    /// <summary>Une session encore ouverte est close chez Wave : son lien ne sert plus.</summary>
    public async Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        try
        {
            _ = await http.SendAsync("Wave", "expire checkout session", api.Request(HttpMethod.Post, $"/checkout/sessions/{Uri.EscapeDataString(reference.Value)}/expire", new JsonObject()), cancellationToken);
        }
        catch (PaymentGatewayException exception) when (exception.StatusCode is 400 or 404 or 409)
        {
            // Déjà payée, expirée ou inconnue : rien à fermer.
        }
    }

    /// <summary>Wave ne rembourse que la totalité d'une session payée.</summary>
    public async Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await http.SendAsync(
            "Wave", "refund checkout session",
            api.Request(HttpMethod.Post, $"/checkout/sessions/{Uri.EscapeDataString(request.CapturedReference.Value)}/refund", new JsonObject()),
            cancellationToken);
    }

    /// <summary>Lecture d'une session inexistante : « introuvable » avec une bonne clé, 401 ou 403 sinon.</summary>
    public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default) =>
        ProviderSupport.CheckAsync(
            async () =>
            {
                try
                {
                    _ = await api.SessionAsync(http, "cos-" + Guid.NewGuid().ToString("N"), cancellationToken);
                }
                catch (PaymentGatewayException exception) when (exception.StatusCode is 404)
                {
                }
            },
            null,
            ProviderSupport.Warnings(options, needsReturnUrl: true, needsHttpsApi: true));

    public async Task<GatewayTransactionStatus?> FetchStatusAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var session = await api.SessionAsync(http, reference.Value, cancellationToken);
        return new GatewayTransactionStatus(WaveApi.State(session), Amount(session), ProviderHttp.Text(session, "currency"));
    }

    internal static decimal? Amount(JsonNode? session) =>
        decimal.TryParse(ProviderHttp.Text(session, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : ProviderHttp.Number(session, "amount");
}

internal sealed class WaveWebhookReader(ProviderHttp http, WaveApi api, string secret) : IPaymentWebhookReader
{
    public const string Header = "Wave-Signature";
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public string Provider => WaveIntegration.Code;

    public async Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (!Verify(ProviderSupport.Header(headers, Header), body, secret, nowUtc) || ProviderSupport.ParseJson(body) is not { } root)
        {
            return WebhookReadResult.Invalid;
        }

        if (ProviderHttp.Text(root, "type") is not ("checkout.session.completed" or "checkout.session.payment_failed")
            || ProviderHttp.Text(root["data"], "id") is not { } id || !WaveApi.IsSessionId(id))
        {
            return WebhookReadResult.Ignored;
        }

        var session = await api.SessionAsync(http, id, cancellationToken);
        ProviderResultKind? kind = WaveApi.State(session) switch
        {
            GatewayTransactionState.Captured => ProviderResultKind.Captured,
            GatewayTransactionState.Failed => ProviderResultKind.Failed,
            _ => null,
        };
        if (kind is not { } known)
        {
            return WebhookReadResult.Ignored;
        }

        var status = ProviderHttp.Text(session, "payment_status") ?? ProviderHttp.Text(session, "checkout_status");
        return WebhookReadResult.Verified(new VerifiedWebhook(
            $"{id}:{status}",
            known,
            id,
            WaveGateway.Amount(session),
            ProviderHttp.Text(session, "currency"),
            Reason: known == ProviderResultKind.Failed ? ProviderHttp.Text(session?["last_payment_error"], "code") ?? status : null));
    }

    /// <summary>En-tête <c>t=&lt;secondes&gt;,v1=&lt;hex&gt;</c> : HMAC-SHA256(secret, t + corps), 5 minutes de tolérance.</summary>
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
            else if (pair[0] == "v1")
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
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + body)));
}
