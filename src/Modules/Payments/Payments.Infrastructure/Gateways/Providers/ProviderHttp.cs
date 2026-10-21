using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Infrastructure.Gateways.Providers;

/// <summary>
/// Le client HTTP partagé par les intégrations des prestataires. Une erreur ne contient JAMAIS d'identifiant, d'en-tête ni
/// de corps envoyé : seulement le prestataire, l'opération et le statut HTTP (ou le code d'erreur renvoyé). 4xx : refus
/// définitif (sauf 408, 409 et 429) ; 5xx, délai dépassé ou réseau : passager.
/// </summary>
internal sealed class ProviderHttp(HttpClient client)
{
    public static ProviderHttp Create() =>
        new(new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) }) { Timeout = TimeSpan.FromSeconds(30) });

    /// <summary>Envoie la requête et renvoie le corps JSON (vide : objet vide).</summary>
    public async Task<JsonNode> SendAsync(string provider, string operation, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new PaymentGatewayException($"{provider} {operation}: provider unreachable.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PaymentGatewayException($"{provider} {operation}: provider timed out.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                var permanent = status is >= 400 and < 500 and not (408 or 409 or 429);
                var code = ErrorCode(text);
                throw new PaymentGatewayException(
                    $"{provider} {operation}: HTTP {status.ToString(CultureInfo.InvariantCulture)}{(code is null ? string.Empty : $" ({code})")}.", permanent)
                {
                    StatusCode = status,
                    ProviderErrorCode = code,
                };
            }

            try
            {
                return string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) ?? new JsonObject();
            }
            catch (JsonException)
            {
                throw new PaymentGatewayException($"{provider} {operation}: unreadable response.");
            }
        }
    }

    public static HttpRequestMessage Json(HttpMethod method, string url, JsonNode body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new HttpRequestMessage(method, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    public static HttpRequestMessage Form(string url, IEnumerable<KeyValuePair<string, string>> fields) =>
        new(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };

    public static void Bearer(HttpRequestMessage request, string token) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static void Basic(HttpRequestMessage request, string user, string password) =>
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    /// <summary>Code ou type d'erreur renvoyé par le prestataire (court, sans donnée), pour l'enquête.</summary>
    private static string? ErrorCode(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            var code = Text(node, "code") ?? Text(node, "error_code") ?? Text(node?["error"], "code") ?? Text(node, "name") ?? Text(node?["error"], "type")
                ?? Text(node, "error");
            return code is { Length: > 0 and <= 60 } && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? code : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? Text(JsonNode? node, string name) =>
        node is JsonObject json && json.TryGetPropertyValue(name, out var value) && value is JsonValue scalar
            ? scalar.TryGetValue<string>(out var text) ? text
                : scalar.TryGetValue<long>(out var number) ? number.ToString(CultureInfo.InvariantCulture)
                : scalar.TryGetValue<decimal>(out var amount) ? amount.ToString(CultureInfo.InvariantCulture)
                : null
            : null;

    public static decimal? Number(JsonNode? node, string name)
    {
        if (node is not JsonObject json || !json.TryGetPropertyValue(name, out var value) || value is not JsonValue scalar)
        {
            return null;
        }

        if (scalar.TryGetValue<decimal>(out var number))
        {
            return number;
        }

        return scalar.TryGetValue<string>(out var text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}

/// <summary>Ce qui est commun aux intégrations : identifiants exigés, environnement, montants, adresses de retour.</summary>
internal static class ProviderSupport
{
    public const string Environment = "environment";
    public const string Sandbox = "sandbox";
    public const string Live = "live";

    /// <summary>La valeur non vide d'un champ d'identifiants ; sinon une erreur définitive (sans la valeur).</summary>
    public static string Required(IReadOnlyDictionary<string, string> credentials, string provider, string field) =>
        credentials.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new PaymentGatewayException($"{provider}: credential '{field}' is missing.", isPermanent: true);

    public static string? Optional(IReadOnlyDictionary<string, string> credentials, string field) =>
        credentials.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    /// <summary>Vrai pour l'environnement de test (<c>sandbox</c>, <c>test</c>) ; faux pour <c>live</c> ou <c>production</c>.</summary>
    public static bool IsSandbox(IReadOnlyDictionary<string, string> credentials) =>
        Optional(credentials, Environment)?.ToLowerInvariant() is not ("live" or "production" or "prod");

    /// <summary>Le montant en plus petite unité de la devise (centimes ; francs CFA tels quels).</summary>
    public static long MinorUnits(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        var factor = (decimal)Math.Pow(10, amount.Currency.DecimalPlaces);
        return (long)decimal.Round(amount.Amount * factor, 0, MidpointRounding.AwayFromZero);
    }

    public static decimal FromMinorUnits(long value, string currency)
    {
        var places = Currency.IsKnown(currency) ? Currency.FromCode(currency).DecimalPlaces : 2;
        return value / (decimal)Math.Pow(10, places);
    }

    /// <summary>Le montant tel qu'écrit par les API qui le veulent en texte décimal (« 12.50 », « 1500 »).</summary>
    public static string Decimal(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        return decimal.Round(amount.Amount, amount.Currency.DecimalPlaces, MidpointRounding.AwayFromZero)
            .ToString("F" + amount.Currency.DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>L'adresse de notification de la configuration, ou null sans adresse publique de l'API.</summary>
    public static string? WebhookUrl(PaymentsOptions options, string provider, Guid configurationId) =>
        options.PublicApiBaseUrl is { } api && configurationId != Guid.Empty
            ? $"{api.AbsoluteUri.TrimEnd('/')}/v1/payments/webhooks/{provider}/{configurationId:D}"
            : null;

    /// <summary>
    /// La page de retour du client, avec la référence du paiement : celle de l'encaissement (application de vente en
    /// ligne) si elle est donnée, sinon celle de la plateforme.
    /// </summary>
    public static string? ReturnUrl(PaymentsOptions options, GatewayPaymentRequest request) =>
        (request.ReturnUrl ?? options.CheckoutReturnUrl) is { } back
            ? $"{back.AbsoluteUri}{(back.Query.Length > 0 ? "&" : "?")}paiement={request.PaymentId.Value:D}"
            : null;

    /// <summary>Libellé envoyé au prestataire : aucune donnée personnelle.</summary>
    public static string Description(OrderId order) => $"Commande {order.Value.ToString("N")[..8].ToUpperInvariant()}";

    /// <summary>Le libellé de la demande, sinon celui de la commande.</summary>
    public static string Label(GatewayPaymentRequest request) => request.Label ?? Description(request.OrderId);

    /// <summary>En-têtes reçus, noms insensibles à la casse.</summary>
    public static string? Header(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var value) ? value : headers.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    public static bool SameSecret(string? received, string? expected) =>
        received is { Length: > 0 } && expected is { Length: > 0 }
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(received), Encoding.UTF8.GetBytes(expected));

    public const string CheckoutReturnUrlMissing = "checkout_return_url_missing";
    public const string PublicApiUrlMissing = "public_api_url_missing";
    public const string PublicApiUrlNotHttps = "public_api_url_not_https";

    /// <summary>Ce qui manque à la plateforme pour encaisser avec ce prestataire, même avec des identifiants valides.</summary>
    public static List<string> Warnings(PaymentsOptions options, bool needsReturnUrl = false, bool needsPublicApi = false, bool needsHttpsApi = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        var warnings = new List<string>();
        if (needsReturnUrl && options.CheckoutReturnUrl is null)
        {
            warnings.Add(CheckoutReturnUrlMissing);
        }

        if ((needsPublicApi || needsHttpsApi) && options.PublicApiBaseUrl is null)
        {
            warnings.Add(PublicApiUrlMissing);
        }
        else if (needsHttpsApi && options.PublicApiBaseUrl is { } api && api.Scheme != Uri.UriSchemeHttps)
        {
            warnings.Add(PublicApiUrlNotHttps);
        }

        return warnings;
    }

    /// <summary>
    /// Lance l'appel de vérification : sans erreur, les identifiants sont acceptés ; 401 ou 403 (ou une erreur que
    /// <paramref name="rejects"/> reconnaît), ils sont refusés. Toute autre erreur remonte.
    /// </summary>
    public static async Task<GatewayCredentialCheck> CheckAsync(
        Func<Task> call, bool? isSandbox, IReadOnlyList<string> warnings, Func<PaymentGatewayException, bool>? rejects = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        try
        {
            await call();
            return new GatewayCredentialCheck(GatewayCredentialStatus.Verified, isSandbox, warnings);
        }
        catch (PaymentGatewayException exception) when (exception.StatusCode is 401 or 403 || rejects?.Invoke(exception) == true)
        {
            return new GatewayCredentialCheck(GatewayCredentialStatus.Rejected, isSandbox, warnings);
        }
    }

    /// <summary>Le corps JSON d'un webhook, ou null s'il est illisible.</summary>
    public static JsonNode? ParseJson(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
