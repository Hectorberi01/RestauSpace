using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Gateways.Providers;

/// <summary>
/// Flutterwave (Afrique multi-pays : cartes, mobile money, virements locaux), API v3 « Standard », sur le compte
/// marchand du restaurant (ou de la plateforme). Clé secrète <c>FLWSECK-…</c> (<c>FLWSECK_TEST-…</c> en test), empreinte
/// secrète des webhooks (« secret hash » du tableau de bord) et e-mail du compte, que Flutterwave exige comme client :
/// on n'envoie jamais celui du client. Le client paie sur la page hébergée ; la référence est la nôtre (<c>tx_ref</c>).
/// Webhook authentifié par l'en-tête <c>verif-hash</c>, puis la transaction est RELUE chez Flutterwave, qui seule fait
/// foi. Remboursement depuis le tableau de bord Flutterwave.
/// </summary>
internal sealed class FlutterwaveIntegration(ProviderHttp http, PaymentsOptions options) : IPaymentIntegration
{
    public const string Code = "flutterwave";
    public const string SecretKey = "secret_key";
    public const string WebhookHash = "webhook_hash";
    public const string AccountEmail = "account_email";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(Code);

    public string Name => "Flutterwave";

    public IReadOnlyList<string> CredentialFields { get; } = [SecretKey, WebhookHash, AccountEmail];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) =>
        new FlutterwaveGateway(Provider, http, options, FlutterwaveApi.From(credentials));

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        new FlutterwaveWebhookReader(http, FlutterwaveApi.From(credentials), ProviderSupport.Required(credentials, Code, WebhookHash));
}

internal sealed record FlutterwaveApi(string Key, string AccountEmail)
{
    public const string BaseUrl = "https://api.flutterwave.com/v3";

    public bool IsSandbox => Key.Contains("_TEST", StringComparison.Ordinal);

    public static FlutterwaveApi From(IReadOnlyDictionary<string, string> credentials)
    {
        var email = ProviderSupport.Required(credentials, FlutterwaveIntegration.Code, FlutterwaveIntegration.AccountEmail);
        return email.Contains('@', StringComparison.Ordinal) && !email.Any(char.IsWhiteSpace)
            ? new FlutterwaveApi(ProviderSupport.Required(credentials, FlutterwaveIntegration.Code, FlutterwaveIntegration.SecretKey), email)
            : throw new PaymentGatewayException("Flutterwave: credential 'account_email' is invalid.", isPermanent: true);
    }

    public HttpRequestMessage Request(HttpMethod method, string path, JsonNode? body = null)
    {
        var request = body is null ? new HttpRequestMessage(method, BaseUrl + path) : ProviderHttp.Json(method, BaseUrl + path, body);
        ProviderHttp.Bearer(request, Key);
        return request;
    }

    /// <summary>La transaction de notre référence ; null si Flutterwave ne la connaît pas (le client n'a pas encore payé).</summary>
    public async Task<JsonNode?> VerifyAsync(ProviderHttp http, string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.SendAsync(
                "Flutterwave", "verify transaction", Request(HttpMethod.Get, $"/transactions/verify_by_reference?tx_ref={Uri.EscapeDataString(reference)}"), cancellationToken);
            return response["data"];
        }
        catch (PaymentGatewayException exception) when (exception.StatusCode is 400 or 404)
        {
            return null;
        }
    }

    public static GatewayTransactionState State(JsonNode? transaction) => ProviderHttp.Text(transaction, "status") switch
    {
        "successful" => GatewayTransactionState.Captured,
        "failed" or "cancelled" => GatewayTransactionState.Failed,
        _ => GatewayTransactionState.Pending,
    };

    public static bool IsReference(string? value) =>
        value is { Length: > 0 and <= 100 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

internal sealed class FlutterwaveGateway(PaymentProviderCode provider, ProviderHttp http, PaymentsOptions options, FlutterwaveApi api) : IPaymentGateway
{
    public PaymentProviderCode Provider => provider;

    public async Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var back = ProviderSupport.ReturnUrl(options, request)
            ?? throw new PaymentGatewayException("Flutterwave: a return URL is required.", isPermanent: true);

        // Une référence par tentative : Flutterwave refuse de réutiliser un tx_ref.
        var reference = $"{request.PaymentId.Value:N}-{RandomNumberGenerator.GetHexString(8, lowercase: true)}";
        var body = new JsonObject
        {
            ["tx_ref"] = reference,
            ["amount"] = ProviderSupport.Decimal(request.Amount),
            ["currency"] = request.Amount.Currency.Code,
            ["redirect_url"] = back,
            ["customer"] = new JsonObject { ["email"] = api.AccountEmail },
            ["customizations"] = new JsonObject { ["title"] = ProviderSupport.Label(request) },
            ["meta"] = new JsonObject { ["payment_id"] = request.PaymentId.Value.ToString("D") },
        };
        var created = await http.SendAsync("Flutterwave", "create payment", api.Request(HttpMethod.Post, "/payments", body), cancellationToken);
        var link = ProviderHttp.Text(created["data"], "link");
        return new GatewayCheckout(ProviderReference.Create(reference), Uri.TryCreate(link, UriKind.Absolute, out var url) ? url : null);
    }

    public Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default) =>
        throw new PaymentGatewayException("Flutterwave: refunds are made from the Flutterwave dashboard.", isPermanent: true);

    public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default) =>
        ProviderSupport.CheckAsync(
            async () => _ = await http.SendAsync("Flutterwave", "list balances", api.Request(HttpMethod.Get, "/balances"), cancellationToken),
            api.IsSandbox,
            ProviderSupport.Warnings(options, needsReturnUrl: true));

    public async Task<GatewayTransactionStatus?> FetchStatusAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var transaction = await api.VerifyAsync(http, reference.Value, cancellationToken);
        return transaction is null
            ? new GatewayTransactionStatus(GatewayTransactionState.Pending, null, null)
            : new GatewayTransactionStatus(FlutterwaveApi.State(transaction), ProviderHttp.Number(transaction, "amount"), ProviderHttp.Text(transaction, "currency"));
    }
}

internal sealed class FlutterwaveWebhookReader(ProviderHttp http, FlutterwaveApi api, string hash) : IPaymentWebhookReader
{
    public const string Header = "verif-hash";

    public string Provider => FlutterwaveIntegration.Code;

    public async Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (!ProviderSupport.SameSecret(ProviderSupport.Header(headers, Header), hash) || ProviderSupport.ParseJson(body) is not { } root)
        {
            return WebhookReadResult.Invalid;
        }

        var reference = ProviderHttp.Text(root["data"], "tx_ref") ?? ProviderHttp.Text(root, "txRef");
        if (!FlutterwaveApi.IsReference(reference))
        {
            return WebhookReadResult.Ignored;
        }

        var transaction = await api.VerifyAsync(http, reference!, cancellationToken);
        var status = ProviderHttp.Text(transaction, "status");
        ProviderResultKind? kind = FlutterwaveApi.State(transaction) switch
        {
            GatewayTransactionState.Captured => ProviderResultKind.Captured,
            GatewayTransactionState.Failed => ProviderResultKind.Failed,
            _ => null,
        };
        return kind is { } known
            ? WebhookReadResult.Verified(new VerifiedWebhook(
                $"{reference}:{status}", known, reference!, ProviderHttp.Number(transaction, "amount"), ProviderHttp.Text(transaction, "currency"),
                Reason: known == ProviderResultKind.Failed ? status : null))
            : WebhookReadResult.Ignored;
    }
}
