using System.Globalization;
using System.Text.Json.Nodes;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Gateways.Providers;

/// <summary>
/// KKiaPay (Bénin : mobile money et cartes, XOF), sur le compte du restaurant : clés publique, privée et secrète, et le
/// secret de webhook choisi dans le tableau de bord KKiaPay (envoyé dans <c>x-kkiapay-secret</c>). Le client paie dans le
/// widget hébergé de KKiaPay ; notre référence voyage en <c>partnerId</c>. À la notification, la transaction est relue par
/// l'API de statut de KKiaPay, qui seule fait foi. Le remboursement se fait depuis le tableau de bord KKiaPay (son API
/// demande l'id de transaction KKiaPay, que nous ne conservons pas).
/// </summary>
internal sealed class KkiaPayIntegration(ProviderHttp http, PaymentsOptions options) : IPaymentIntegration
{
    public const string Code = "kkiapay";
    public const string PublicKey = "public_key";
    public const string PrivateKey = "private_key";
    public const string SecretKey = "secret_key";
    public const string WebhookSecret = "webhook_secret";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(Code);

    public string Name => "KKiaPay";

    public IReadOnlyList<string> CredentialFields { get; } = [PublicKey, PrivateKey, SecretKey, WebhookSecret, ProviderSupport.Environment];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) =>
        new KkiaPayGateway(Provider, http, options, KkiaPayApi.From(credentials));

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        new KkiaPayWebhookReader(http, KkiaPayApi.From(credentials), ProviderSupport.Required(credentials, Code, WebhookSecret));
}

internal sealed record KkiaPayApi(string PublicKey, string PrivateKey, string SecretKey, bool Sandbox)
{
    public const string WidgetUrl = "https://widget-v3.kkiapay.me/";

    public string BaseUrl => Sandbox ? "https://api-sandbox.kkiapay.me" : "https://api.kkiapay.me";

    public static KkiaPayApi From(IReadOnlyDictionary<string, string> credentials) =>
        new(ProviderSupport.Required(credentials, KkiaPayIntegration.Code, KkiaPayIntegration.PublicKey),
            ProviderSupport.Required(credentials, KkiaPayIntegration.Code, KkiaPayIntegration.PrivateKey),
            ProviderSupport.Required(credentials, KkiaPayIntegration.Code, KkiaPayIntegration.SecretKey),
            ProviderSupport.IsSandbox(credentials));

    public async Task<JsonNode> StatusAsync(ProviderHttp http, string transactionId, CancellationToken cancellationToken)
    {
        var request = ProviderHttp.Json(HttpMethod.Post, BaseUrl + "/api/v1/transactions/status", new JsonObject { ["transactionId"] = transactionId });
        request.Headers.Add("x-api-key", PublicKey);
        request.Headers.Add("x-private-key", PrivateKey);
        request.Headers.Add("x-secret-key", SecretKey);
        return await http.SendAsync("KKiaPay", "transaction status", request, cancellationToken);
    }
}

internal sealed class KkiaPayGateway(PaymentProviderCode provider, ProviderHttp http, PaymentsOptions options, KkiaPayApi api) : IPaymentGateway
{
    public PaymentProviderCode Provider => provider;

    public Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reference = "kkp" + Guid.NewGuid().ToString("N");
        var query = new List<string>
        {
            "amount=" + ProviderSupport.MinorUnits(request.Amount).ToString(CultureInfo.InvariantCulture),
            "key=" + Uri.EscapeDataString(api.PublicKey),
            "sandbox=" + (api.Sandbox ? "true" : "false"),
            "partnerId=" + reference,
            "data=" + reference,
            "reason=" + Uri.EscapeDataString(ProviderSupport.Label(request)),
        };
        if (ProviderSupport.ReturnUrl(options, request) is { } back)
        {
            query.Add("callback=" + Uri.EscapeDataString(back));
        }

        return Task.FromResult(new GatewayCheckout(ProviderReference.Create(reference), new Uri(KkiaPayApi.WidgetUrl + "?" + string.Join('&', query))));
    }

    public Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default) =>
        throw new PaymentGatewayException("KKiaPay: refunds are made from the KKiaPay dashboard.", isPermanent: true);

    /// <summary>Statut d'une transaction qui n'existe pas : « introuvable » avec de bonnes clés, 401 ou 403 sinon.</summary>
    public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default) =>
        ProviderSupport.CheckAsync(
            async () =>
            {
                try
                {
                    _ = await api.StatusAsync(http, "chk" + Guid.NewGuid().ToString("N"), cancellationToken);
                }
                catch (PaymentGatewayException exception) when (exception.StatusCode is 400 or 404)
                {
                    return;
                }
            },
            api.Sandbox,
            ProviderSupport.Warnings(options));
}

internal sealed class KkiaPayWebhookReader(ProviderHttp http, KkiaPayApi api, string secret) : IPaymentWebhookReader
{
    public const string Header = "x-kkiapay-secret";

    public string Provider => KkiaPayIntegration.Code;

    public async Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (!ProviderSupport.SameSecret(ProviderSupport.Header(headers, Header), secret) || ProviderSupport.ParseJson(body) is not { } root)
        {
            return WebhookReadResult.Invalid;
        }

        if (ProviderHttp.Text(root, "transactionId") is not { Length: > 0 and <= 100 } transactionId || !transactionId.All(char.IsAsciiLetterOrDigit))
        {
            return WebhookReadResult.Invalid;
        }

        var transaction = await api.StatusAsync(http, transactionId, cancellationToken);
        var reference = Reference(ProviderHttp.Text(transaction, "partnerId")) ?? Reference(ProviderHttp.Text(transaction, "state"))
            ?? Reference(ProviderHttp.Text(root, "partnerId"));
        if (reference is null)
        {
            return WebhookReadResult.Ignored;
        }

        var status = ProviderHttp.Text(transaction, "status");
        ProviderResultKind? kind = status switch
        {
            "SUCCESS" => ProviderResultKind.Captured,
            "FAILED" or "CANCELLED" or "CANCELED" or "EXPIRED" => ProviderResultKind.Failed,
            _ => null,
        };
        if (kind is not { } known)
        {
            return WebhookReadResult.Ignored;
        }

        return WebhookReadResult.Verified(new VerifiedWebhook(
            $"{transactionId}:{status}",
            known,
            reference,
            ProviderHttp.Number(transaction, "amount"),
            "XOF",
            Reason: known == ProviderResultKind.Failed ? ProviderHttp.Text(transaction, "failureCode") ?? status!.ToLowerInvariant() : null));
    }

    private static string? Reference(string? value) =>
        value is { Length: 35 } && value.StartsWith("kkp", StringComparison.Ordinal) && value.All(char.IsAsciiLetterOrDigit) ? value : null;
}
