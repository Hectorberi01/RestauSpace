using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Gateways.Providers;

/// <summary>
/// Paystack (Nigeria, Ghana, Afrique du Sud, Kenya, Côte d'Ivoire : cartes, mobile money, virements selon le pays), sur
/// le compte du restaurant. Clé secrète <c>sk_test_…</c> ou <c>sk_live_…</c> et e-mail du compte, que Paystack exige comme
/// client : on n'envoie jamais celui du client. Le client paie sur la page Paystack (<c>authorization_url</c>). Les
/// webhooks sont signés par la clé secrète (<c>x-paystack-signature</c>, HMAC-SHA512 du corps) puis la transaction est
/// RELUE chez Paystack. Remboursement depuis le tableau de bord Paystack.
/// </summary>
internal sealed class PaystackIntegration(ProviderHttp http, PaymentsOptions options) : IPaymentIntegration
{
    public const string Code = "paystack";
    public const string SecretKey = "secret_key";
    public const string AccountEmail = "account_email";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(Code);

    public string Name => "Paystack";

    public IReadOnlyList<string> CredentialFields { get; } = [SecretKey, AccountEmail];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) =>
        new PaystackGateway(Provider, http, options, PaystackApi.From(credentials));

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        new PaystackWebhookReader(http, PaystackApi.From(credentials));
}

internal sealed record PaystackApi(string Key, string AccountEmail)
{
    public const string BaseUrl = "https://api.paystack.co";

    public bool IsSandbox => Key.StartsWith("sk_test_", StringComparison.Ordinal);

    public static PaystackApi From(IReadOnlyDictionary<string, string> credentials)
    {
        var email = ProviderSupport.Required(credentials, PaystackIntegration.Code, PaystackIntegration.AccountEmail);
        return email.Contains('@', StringComparison.Ordinal) && !email.Any(char.IsWhiteSpace)
            ? new PaystackApi(ProviderSupport.Required(credentials, PaystackIntegration.Code, PaystackIntegration.SecretKey), email)
            : throw new PaymentGatewayException("Paystack: credential 'account_email' is invalid.", isPermanent: true);
    }

    public HttpRequestMessage Request(HttpMethod method, string path, JsonNode? body = null)
    {
        var request = body is null ? new HttpRequestMessage(method, BaseUrl + path) : ProviderHttp.Json(method, BaseUrl + path, body);
        ProviderHttp.Bearer(request, Key);
        return request;
    }

    public async Task<JsonNode?> VerifyAsync(ProviderHttp http, string reference, CancellationToken cancellationToken) =>
        (await http.SendAsync("Paystack", "verify transaction", Request(HttpMethod.Get, $"/transaction/verify/{Uri.EscapeDataString(reference)}"), cancellationToken))["data"];

    public static GatewayTransactionState State(JsonNode? transaction) => ProviderHttp.Text(transaction, "status") switch
    {
        "success" => GatewayTransactionState.Captured,
        "failed" or "abandoned" or "reversed" => GatewayTransactionState.Failed,
        _ => GatewayTransactionState.Pending,
    };

    public static decimal? Amount(JsonNode? transaction) =>
        ProviderHttp.Number(transaction, "amount") is { } minor && ProviderHttp.Text(transaction, "currency") is { } currency
            ? ProviderSupport.FromMinorUnits((long)minor, currency)
            : null;

    public static bool IsReference(string? value) =>
        value is { Length: > 0 and <= 100 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '=');
}

internal sealed class PaystackGateway(PaymentProviderCode provider, ProviderHttp http, PaymentsOptions options, PaystackApi api) : IPaymentGateway
{
    public PaymentProviderCode Provider => provider;

    public async Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reference = $"{request.PaymentId.Value:N}-{RandomNumberGenerator.GetHexString(8, lowercase: true)}";
        var body = new JsonObject
        {
            ["email"] = api.AccountEmail,
            ["amount"] = ProviderSupport.MinorUnits(request.Amount),
            ["currency"] = request.Amount.Currency.Code,
            ["reference"] = reference,
            ["metadata"] = new JsonObject { ["payment_id"] = request.PaymentId.Value.ToString("D"), ["label"] = ProviderSupport.Label(request) },
        };
        if (ProviderSupport.ReturnUrl(options, request) is { } back)
        {
            body["callback_url"] = back;
        }

        var created = await http.SendAsync("Paystack", "initialize transaction", api.Request(HttpMethod.Post, "/transaction/initialize", body), cancellationToken);
        var url = ProviderHttp.Text(created["data"], "authorization_url");
        return new GatewayCheckout(ProviderReference.Create(ProviderHttp.Text(created["data"], "reference") ?? reference), Uri.TryCreate(url, UriKind.Absolute, out var checkout) ? checkout : null);
    }

    public Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default) =>
        throw new PaymentGatewayException("Paystack: refunds are made from the Paystack dashboard.", isPermanent: true);

    public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default) =>
        ProviderSupport.CheckAsync(
            async () => _ = await http.SendAsync("Paystack", "read balance", api.Request(HttpMethod.Get, "/balance"), cancellationToken),
            api.IsSandbox,
            ProviderSupport.Warnings(options));

    public async Task<GatewayTransactionStatus?> FetchStatusAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var transaction = await api.VerifyAsync(http, reference.Value, cancellationToken);
        return new GatewayTransactionStatus(PaystackApi.State(transaction), PaystackApi.Amount(transaction), ProviderHttp.Text(transaction, "currency"));
    }
}

internal sealed class PaystackWebhookReader(ProviderHttp http, PaystackApi api) : IPaymentWebhookReader
{
    public const string Header = "x-paystack-signature";

    public string Provider => PaystackIntegration.Code;

    public static string Sign(string secretKey, string body) =>
        Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(body)));

    public async Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (!ProviderSupport.SameSecret(ProviderSupport.Header(headers, Header)?.Trim().ToLowerInvariant(), Sign(api.Key, body))
            || ProviderSupport.ParseJson(body) is not { } root)
        {
            return WebhookReadResult.Invalid;
        }

        var reference = ProviderHttp.Text(root["data"], "reference");
        if (ProviderHttp.Text(root, "event")?.StartsWith("charge.", StringComparison.Ordinal) != true || !PaystackApi.IsReference(reference))
        {
            return WebhookReadResult.Ignored;
        }

        var transaction = await api.VerifyAsync(http, reference!, cancellationToken);
        var status = ProviderHttp.Text(transaction, "status");
        ProviderResultKind? kind = PaystackApi.State(transaction) switch
        {
            GatewayTransactionState.Captured => ProviderResultKind.Captured,
            GatewayTransactionState.Failed => ProviderResultKind.Failed,
            _ => null,
        };
        return kind is { } known
            ? WebhookReadResult.Verified(new VerifiedWebhook(
                $"{reference}:{status}", known, reference!, PaystackApi.Amount(transaction), ProviderHttp.Text(transaction, "currency"),
                Reason: known == ProviderResultKind.Failed ? status : null))
            : WebhookReadResult.Ignored;
    }
}
