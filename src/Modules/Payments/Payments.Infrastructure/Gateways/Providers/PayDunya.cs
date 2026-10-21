using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Gateways.Providers;

/// <summary>
/// PayDunya (Afrique de l'Ouest : Orange Money, Wave, Moov Money, MTN, Free Money, cartes ; XOF), sur le compte du
/// restaurant. Clé principale, clé privée, jeton et environnement (<c>sandbox</c> ou <c>live</c>). Le client paie sur la
/// facture en ligne PayDunya ; la référence est le jeton de la facture. La notification (IPN) arrive en formulaire avec
/// <c>data[hash]</c> = SHA-512 de la clé principale, puis la facture est RELUE chez PayDunya, qui seule fait foi.
/// Remboursement depuis le tableau de bord PayDunya.
/// </summary>
internal sealed class PayDunyaIntegration(ProviderHttp http, PaymentsOptions options) : IPaymentIntegration
{
    public const string Code = "paydunya";
    public const string MasterKey = "master_key";
    public const string PrivateKey = "private_key";
    public const string Token = "token";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(Code);

    public string Name => "PayDunya";

    public IReadOnlyList<string> CredentialFields { get; } = [MasterKey, PrivateKey, Token, ProviderSupport.Environment];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) =>
        new PayDunyaGateway(Provider, http, options, PayDunyaApi.From(credentials));

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        new PayDunyaWebhookReader(http, PayDunyaApi.From(credentials));
}

internal sealed record PayDunyaApi(string MasterKey, string PrivateKey, string Token, bool IsSandbox)
{
    public string BaseUrl => IsSandbox ? "https://app.paydunya.com/sandbox-api/v1" : "https://app.paydunya.com/api/v1";

    public static PayDunyaApi From(IReadOnlyDictionary<string, string> credentials) => new(
        ProviderSupport.Required(credentials, PayDunyaIntegration.Code, PayDunyaIntegration.MasterKey),
        ProviderSupport.Required(credentials, PayDunyaIntegration.Code, PayDunyaIntegration.PrivateKey),
        ProviderSupport.Required(credentials, PayDunyaIntegration.Code, PayDunyaIntegration.Token),
        ProviderSupport.IsSandbox(credentials));

    public HttpRequestMessage Request(HttpMethod method, string path, JsonNode? body = null)
    {
        var request = body is null ? new HttpRequestMessage(method, BaseUrl + path) : ProviderHttp.Json(method, BaseUrl + path, body);
        request.Headers.Add("PAYDUNYA-MASTER-KEY", MasterKey);
        request.Headers.Add("PAYDUNYA-PRIVATE-KEY", PrivateKey);
        request.Headers.Add("PAYDUNYA-TOKEN", Token);
        return request;
    }

    /// <summary>L'empreinte attendue dans les notifications : SHA-512 hexadécimal de la clé principale.</summary>
    public string Hash => Convert.ToHexStringLower(SHA512.HashData(Encoding.UTF8.GetBytes(MasterKey)));

    public Task<JsonNode> ConfirmAsync(ProviderHttp http, string token, CancellationToken cancellationToken) =>
        http.SendAsync("PayDunya", "confirm invoice", Request(HttpMethod.Get, $"/checkout-invoice/confirm/{Uri.EscapeDataString(token)}"), cancellationToken);

    public static GatewayTransactionState State(JsonNode? invoice) => ProviderHttp.Text(invoice, "status") switch
    {
        "completed" => GatewayTransactionState.Captured,
        "cancelled" or "failed" or "expired" => GatewayTransactionState.Failed,
        _ => GatewayTransactionState.Pending,
    };

    public static bool IsToken(string? value) =>
        value is { Length: > 0 and <= 100 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

internal sealed class PayDunyaGateway(PaymentProviderCode provider, ProviderHttp http, PaymentsOptions options, PayDunyaApi api) : IPaymentGateway
{
    public PaymentProviderCode Provider => provider;

    public async Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actions = new JsonObject();
        if (ProviderSupport.ReturnUrl(options, request) is { } back)
        {
            actions["return_url"] = back;
            actions["cancel_url"] = back;
        }

        if (ProviderSupport.WebhookUrl(options, PayDunyaIntegration.Code, request.ConfigurationId) is { } notify)
        {
            actions["callback_url"] = notify;
        }

        var body = new JsonObject
        {
            ["invoice"] = new JsonObject { ["total_amount"] = ProviderSupport.Decimal(request.Amount), ["description"] = ProviderSupport.Label(request) },
            ["store"] = new JsonObject { ["name"] = ProviderSupport.Label(request) },
            ["actions"] = actions,
            ["custom_data"] = new JsonObject { ["payment_id"] = request.PaymentId.Value.ToString("D") },
        };
        var created = await http.SendAsync("PayDunya", "create invoice", api.Request(HttpMethod.Post, "/checkout-invoice/create", body), cancellationToken);
        if (ProviderHttp.Text(created, "response_code") != "00" || ProviderHttp.Text(created, "token") is not { } token)
        {
            throw new PaymentGatewayException($"PayDunya create invoice: refused ({ProviderHttp.Text(created, "response_code") ?? "no code"}).", isPermanent: true);
        }

        var url = ProviderHttp.Text(created, "response_text");
        return new GatewayCheckout(ProviderReference.Create(token), Uri.TryCreate(url, UriKind.Absolute, out var checkout) ? checkout : null);
    }

    public Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default) =>
        throw new PaymentGatewayException("PayDunya: refunds are made from the PayDunya dashboard.", isPermanent: true);

    public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new GatewayCredentialCheck(GatewayCredentialStatus.NotSupported, api.IsSandbox, ProviderSupport.Warnings(options, needsPublicApi: true)));

    public async Task<GatewayTransactionStatus?> FetchStatusAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var invoice = await api.ConfirmAsync(http, reference.Value, cancellationToken);
        return new GatewayTransactionStatus(PayDunyaApi.State(invoice), ProviderHttp.Number(invoice["invoice"], "total_amount"), "XOF");
    }
}

internal sealed class PayDunyaWebhookReader(ProviderHttp http, PayDunyaApi api) : IPaymentWebhookReader
{
    public string Provider => PayDunyaIntegration.Code;

    public async Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var form = Form(body);
        if (!ProviderSupport.SameSecret(form.GetValueOrDefault("data[hash]")?.Trim().ToLowerInvariant(), api.Hash))
        {
            return WebhookReadResult.Invalid;
        }

        var token = form.GetValueOrDefault("data[invoice][token]");
        if (!PayDunyaApi.IsToken(token))
        {
            return WebhookReadResult.Ignored;
        }

        var invoice = await api.ConfirmAsync(http, token!, cancellationToken);
        var status = ProviderHttp.Text(invoice, "status");
        ProviderResultKind? kind = PayDunyaApi.State(invoice) switch
        {
            GatewayTransactionState.Captured => ProviderResultKind.Captured,
            GatewayTransactionState.Failed => ProviderResultKind.Failed,
            _ => null,
        };
        return kind is { } known
            ? WebhookReadResult.Verified(new VerifiedWebhook(
                $"{token}:{status}", known, token!, ProviderHttp.Number(invoice["invoice"], "total_amount"), "XOF",
                Reason: known == ProviderResultKind.Failed ? status : null))
            : WebhookReadResult.Ignored;
    }

    private static Dictionary<string, string> Form(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equal = pair.IndexOf('=', StringComparison.Ordinal);
            result[WebUtility.UrlDecode(equal < 0 ? pair : pair[..equal])] = equal < 0 ? string.Empty : WebUtility.UrlDecode(pair[(equal + 1)..]);
        }

        return result;
    }
}
