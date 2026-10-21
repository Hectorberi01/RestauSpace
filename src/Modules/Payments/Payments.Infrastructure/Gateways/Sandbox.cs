using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Gateways;

/// <summary>
/// Intégration simulée <c>sandbox</c>, pour le développement, la recette et les tests : elle ouvre des intentions
/// (« sbx_... ») sans rien appeler, et ses résultats arrivent comme ceux d'un vrai prestataire, par des webhooks signés
/// avec le <c>webhook_secret</c> que le RESTAURANT a saisi (voir <see cref="SandboxSignature"/>). Modèle pour brancher un
/// vrai prestataire.
/// </summary>
internal sealed class SandboxIntegration(PaymentsOptions options) : IPaymentIntegration
{
    public const string WebhookSecretField = "webhook_secret";

    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create(SandboxSignature.Provider);

    public string Name => "Sandbox (simulation)";

    public IReadOnlyList<string> CredentialFields { get; } = [WebhookSecretField];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials) => new SandboxPaymentGateway(Provider, options);

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return new SandboxWebhookReader(credentials.TryGetValue(WebhookSecretField, out var secret) ? secret : string.Empty);
    }
}

internal sealed class SandboxPaymentGateway(PaymentProviderCode provider, PaymentsOptions options) : IPaymentGateway
{
    public PaymentProviderCode Provider => provider;

    public Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reference = ProviderReference.Create("sbx_" + Guid.NewGuid().ToString("N"));
        var checkout = options.SandboxCheckoutBaseUrl is { } baseUrl ? new Uri(baseUrl, Uri.EscapeDataString(reference.Value)) : null;
        return Task.FromResult(new GatewayCheckout(reference, checkout));
    }

    public Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new GatewayCredentialCheck(GatewayCredentialStatus.Verified, true, []));

    /// <summary>Simulation : une intention du sandbox est tenue pour payée dès qu'on la relit.</summary>
    public Task<GatewayTransactionStatus?> FetchStatusAsync(ProviderReference reference, CancellationToken cancellationToken = default) =>
        Task.FromResult<GatewayTransactionStatus?>(new GatewayTransactionStatus(GatewayTransactionState.Captured, null, null));
}

/// <summary>
/// Webhooks du sandbox. En-tête <c>Sandbox-Signature: t=&lt;secondes unix&gt;,v1=&lt;hex&gt;</c>, où v1 =
/// HMAC-SHA256(secret, "&lt;t&gt;.&lt;corps&gt;") ; refusé au-delà de 5 minutes d'écart. Corps JSON :
/// <c>{ "id", "type": "payment.authorized|payment.captured|payment.failed|refund.succeeded|refund.failed",
/// "reference", "amount", "currency", "refundId", "reason" }</c>.
/// </summary>
internal sealed class SandboxWebhookReader(string secret) : IPaymentWebhookReader
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public string Provider => SandboxSignature.Provider;

    public Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(headers, body, nowUtc));

    internal WebhookReadResult Read(IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrEmpty(secret)
            || !headers.TryGetValue(SandboxSignature.Header, out var header)
            || !SandboxSignature.TryParse(header, out var timestamp, out var signature)
            || Math.Abs(nowUtc.ToUnixTimeSeconds() - timestamp) > (long)Tolerance.TotalSeconds
            || !CryptographicOperations.FixedTimeEquals(signature, SandboxSignature.Compute(secret, timestamp, body)))
        {
            return WebhookReadResult.Invalid;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var kind = Text(root, "type") switch
            {
                "payment.authorized" => ProviderResultKind.Authorized,
                "payment.captured" => ProviderResultKind.Captured,
                "payment.failed" => ProviderResultKind.Failed,
                "refund.succeeded" => ProviderResultKind.RefundSucceeded,
                "refund.failed" => ProviderResultKind.RefundFailed,
                _ => (ProviderResultKind?)null,
            };
            if (kind is not { } known)
            {
                return WebhookReadResult.Ignored;
            }

            if (Text(root, "id") is not { Length: > 0 and <= PaymentWebhook.MaxEventIdLength } id || Text(root, "reference") is not { Length: > 0 } reference)
            {
                return WebhookReadResult.Invalid;
            }

            return WebhookReadResult.Verified(new VerifiedWebhook(
                id,
                known,
                reference,
                root.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number ? amount.GetDecimal() : null,
                Text(root, "currency"),
                Guid.TryParse(Text(root, "refundId"), out var refundId) ? refundId : null,
                Text(root, "reason")));
        }
        catch (JsonException)
        {
            return WebhookReadResult.Invalid;
        }
        catch (FormatException)
        {
            return WebhookReadResult.Invalid;
        }
        catch (InvalidOperationException)
        {
            return WebhookReadResult.Invalid;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>Signature des webhooks du sandbox : publique pour que les tests et les outils de recette signent les leurs.</summary>
public static class SandboxSignature
{
    public const string Provider = "sandbox";
    public const string Header = "Sandbox-Signature";

    /// <summary>Valeur de l'en-tête <see cref="Header"/> pour ce corps, signée maintenant (ou à <paramref name="at"/>).</summary>
    public static string Sign(string secret, string body, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentNullException.ThrowIfNull(body);
        var timestamp = at.ToUnixTimeSeconds();
        return $"t={timestamp.ToString(CultureInfo.InvariantCulture)},v1={Convert.ToHexStringLower(Compute(secret, timestamp, body))}";
    }

    internal static byte[] Compute(string secret, long timestamp, string body) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp.ToString(CultureInfo.InvariantCulture)}.{body}"));

    internal static bool TryParse(string header, out long timestamp, out byte[] signature)
    {
        timestamp = 0;
        signature = [];
        string? t = null;
        string? v1 = null;
        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("t=", StringComparison.Ordinal))
            {
                t = part[2..];
            }
            else if (part.StartsWith("v1=", StringComparison.Ordinal))
            {
                v1 = part[3..];
            }
        }

        if (!long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out timestamp) || v1 is not { Length: 64 })
        {
            return false;
        }

        try
        {
            signature = Convert.FromHexString(v1);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
