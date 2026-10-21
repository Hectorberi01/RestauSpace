using System.Globalization;
using System.Text.Json.Nodes;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Gateways.Providers;

/// <summary>
/// CinetPay (Afrique de l'Ouest et centrale : mobile money et cartes, XOF/XAF), sur le compte du restaurant (clé API et
/// identifiant de site). Le client paie sur la page de CinetPay. L'adresse de notification est donnée à chaque paiement
/// (<c>Payments:PublicApiBaseUrl</c> requise) ; la notification n'est qu'un signal : la transaction est relue par
/// l'API de vérification de CinetPay, qui seule fait foi. En XOF/XAF, le montant doit être un multiple de 5. Pas d'API de
/// remboursement : il se fait depuis le tableau de bord CinetPay.
/// </summary>
internal sealed class CinetPayIntegration(ProviderHttp http, PaymentsOptions options) : IPaymentIntegration
{
    public const string Code = "cinetpay";
    public const string ApiKey = "api_key";
    public const string SiteId = "site_id";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(Code);

    public string Name => "CinetPay";

    public IReadOnlyList<string> CredentialFields { get; } = [ApiKey, SiteId];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) =>
        new CinetPayGateway(Provider, http, options, CinetPayApi.From(credentials));

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        new CinetPayWebhookReader(http, CinetPayApi.From(credentials));
}

internal sealed record CinetPayApi(string ApiKey, string SiteId)
{
    public const string BaseUrl = "https://api-checkout.cinetpay.com/v2";

    /// <summary>Codes de CinetPay pour une clé API inconnue ou un site invalide.</summary>
    public static readonly IReadOnlySet<string> RejectedCodes = new HashSet<string>(StringComparer.Ordinal) { "606", "609", "613" };

    public static CinetPayApi From(IReadOnlyDictionary<string, string> credentials) =>
        new(ProviderSupport.Required(credentials, CinetPayIntegration.Code, CinetPayIntegration.ApiKey),
            ProviderSupport.Required(credentials, CinetPayIntegration.Code, CinetPayIntegration.SiteId));

    public async Task<JsonNode> CheckAsync(ProviderHttp http, string transactionId, CancellationToken cancellationToken) =>
        await http.SendAsync(
            "CinetPay",
            "check payment",
            ProviderHttp.Json(HttpMethod.Post, BaseUrl + "/payment/check", new JsonObject
            {
                ["apikey"] = ApiKey,
                ["site_id"] = SiteId,
                ["transaction_id"] = transactionId,
            }),
            cancellationToken);
}

internal sealed class CinetPayGateway(PaymentProviderCode provider, ProviderHttp http, PaymentsOptions options, CinetPayApi api) : IPaymentGateway
{
    public PaymentProviderCode Provider => provider;

    public async Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var amount = ProviderSupport.MinorUnits(request.Amount);
        if (request.Amount.Currency.DecimalPlaces == 0 && amount % 5 != 0)
        {
            throw new PaymentGatewayException("CinetPay: the amount must be a multiple of 5.", isPermanent: true);
        }

        var notify = ProviderSupport.WebhookUrl(options, CinetPayIntegration.Code, request.ConfigurationId)
            ?? throw new PaymentGatewayException("CinetPay: 'Payments:PublicApiBaseUrl' is not configured.", isPermanent: true);
        var transactionId = "cp" + Guid.NewGuid().ToString("N");
        var body = new JsonObject
        {
            ["apikey"] = api.ApiKey,
            ["site_id"] = api.SiteId,
            ["transaction_id"] = transactionId,
            ["amount"] = amount,
            ["currency"] = request.Amount.Currency.Code,
            ["description"] = ProviderSupport.Label(request),
            ["notify_url"] = notify,
            ["return_url"] = ProviderSupport.ReturnUrl(options, request) ?? notify,
            ["channels"] = "ALL",
            ["lang"] = "fr",
            ["metadata"] = request.PaymentId.Value.ToString("D"),
        };
        var response = await http.SendAsync("CinetPay", "create payment", ProviderHttp.Json(HttpMethod.Post, CinetPayApi.BaseUrl + "/payment", body), cancellationToken);
        if (ProviderHttp.Text(response, "code") != "201" || ProviderHttp.Text(response["data"], "payment_url") is not { } url)
        {
            throw new PaymentGatewayException($"CinetPay create payment: refused ({ProviderHttp.Text(response, "code") ?? "no code"}).", isPermanent: true);
        }

        return new GatewayCheckout(ProviderReference.Create(transactionId), Uri.TryCreate(url, UriKind.Absolute, out var checkout) ? checkout : null);
    }

    public Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default) =>
        throw new PaymentGatewayException("CinetPay: refunds are made from the CinetPay dashboard.", isPermanent: true);

    /// <summary>
    /// Vérifie une transaction qui n'existe pas : CinetPay répond « introuvable » si la clé et le site sont bons, et
    /// <c>609</c> (clé inconnue) ou <c>613</c> (site invalide) sinon.
    /// </summary>
    public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default) =>
        ProviderSupport.CheckAsync(
            async () =>
            {
                var check = await api.CheckAsync(http, "cp" + Guid.NewGuid().ToString("N"), cancellationToken);
                if (ProviderHttp.Text(check, "code") is { } code && CinetPayApi.RejectedCodes.Contains(code))
                {
                    throw new PaymentGatewayException($"CinetPay check: credentials refused ({code}).", isPermanent: true) { ProviderErrorCode = code };
                }
            },
            null,
            ProviderSupport.Warnings(options, needsPublicApi: true),
            exception => exception.ProviderErrorCode is { } code && CinetPayApi.RejectedCodes.Contains(code));

    public async Task<GatewayTransactionStatus?> FetchStatusAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var check = await api.CheckAsync(http, reference.Value, cancellationToken);
        var data = check["data"];
        var state = (ProviderHttp.Text(check, "code"), ProviderHttp.Text(data, "status")) switch
        {
            ("00", "ACCEPTED") => GatewayTransactionState.Captured,
            (_, "REFUSED" or "CANCELED" or "CANCELLED" or "EXPIRED") => GatewayTransactionState.Failed,
            _ => GatewayTransactionState.Pending,
        };
        return new GatewayTransactionStatus(state, ProviderHttp.Number(data, "amount"), ProviderHttp.Text(data, "currency"));
    }
}

internal sealed class CinetPayWebhookReader(ProviderHttp http, CinetPayApi api) : IPaymentWebhookReader
{
    public string Provider => CinetPayIntegration.Code;

    public async Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var fields = Fields(body);
        if (!fields.TryGetValue("cpm_trans_id", out var transactionId) || transactionId is not { Length: > 2 and <= 100 }
            || !transactionId.StartsWith("cp", StringComparison.Ordinal) || !transactionId.All(char.IsAsciiLetterOrDigit))
        {
            return WebhookReadResult.Invalid;
        }

        if (fields.TryGetValue("cpm_site_id", out var site) && !string.Equals(site, api.SiteId, StringComparison.Ordinal))
        {
            return WebhookReadResult.Invalid;
        }

        var check = await api.CheckAsync(http, transactionId, cancellationToken);
        var data = check["data"];
        var status = ProviderHttp.Text(data, "status");
        ProviderResultKind? kind = (ProviderHttp.Text(check, "code"), status) switch
        {
            ("00", "ACCEPTED") => ProviderResultKind.Captured,
            (_, "REFUSED" or "CANCELED" or "CANCELLED" or "EXPIRED") => ProviderResultKind.Failed,
            _ => null,
        };
        if (kind is not { } known)
        {
            return WebhookReadResult.Ignored;
        }

        return WebhookReadResult.Verified(new VerifiedWebhook(
            $"{transactionId}:{status}",
            known,
            transactionId,
            ProviderHttp.Number(data, "amount"),
            ProviderHttp.Text(data, "currency"),
            Reason: known == ProviderResultKind.Failed ? status!.ToLowerInvariant() : null));
    }

    /// <summary>Notification en formulaire (cas habituel) ou en JSON.</summary>
    internal static Dictionary<string, string> Fields(string body)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ProviderSupport.ParseJson(body) is JsonObject json)
        {
            foreach (var (key, value) in json)
            {
                if (value is JsonValue scalar)
                {
                    fields[key] = scalar.ToString();
                }
            }

            return fields;
        }

        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            fields[Uri.UnescapeDataString(parts[0].Replace('+', ' '))] = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
        }

        return fields;
    }
}
