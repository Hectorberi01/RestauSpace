using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fiscal.Application;
using Fiscal.Domain;

namespace Fiscal.Infrastructure.Emecf;

/// <summary>
/// Client de l'API e-MECeF (DGI du Bénin). Le jeton part dans l'en-tête Authorization et nulle part ailleurs. Une erreur
/// ne contient JAMAIS le jeton, un en-tête ni le corps envoyé : seulement l'opération, le statut HTTP et le code DGI.
/// 401/403 : jeton refusé (définitif) ; autre 4xx : définitif ; 5xx, délai ou réseau : passager. Codes DGI passagers :
/// 1 (trop de factures en attente), 10 (référence invérifiable pour l'instant), 99 (erreur de traitement).
/// </summary>
internal sealed class EmecfClient(FiscalOptions options) : IFiscalAuthority
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
    {
        Timeout = TimeSpan.FromSeconds(25),
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> TransientCodes = new(StringComparer.Ordinal) { "1", "10", "99" };

    public async Task<EmecfStatus> StatusAsync(FiscalMode mode, string token, CancellationToken cancellationToken = default)
    {
        var body = await SendAsync(mode, token, HttpMethod.Get, "info/status", null, "status", cancellationToken);
        var validUntil = DateTimeOffset.TryParse(Text(body, "tokenValid"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : (DateTimeOffset?)null;
        return new EmecfStatus(Text(body, "ifu"), Text(body, "nim"), validUntil);
    }

    public async Task<string> SubmitAsync(FiscalMode mode, string token, EmecfInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payload = new
        {
            ifu = request.Ifu,
            aib = request.Aib,
            type = request.Type,
            items = request.Items.Select(item => new { name = item.Name, price = item.Price, quantity = item.Quantity, taxGroup = item.TaxGroup }).ToList(),
            @operator = new { id = "1", name = request.OperatorName },
            payment = request.Payments.Select(payment => new { name = payment.Name, amount = payment.Amount }).ToList(),
            reference = request.Reference,
        };
        var body = await SendAsync(mode, token, HttpMethod.Post, "invoice", payload, "submit", cancellationToken);
        return Text(body, "uid") is { Length: > 0 } uid
            ? uid
            : throw new FiscalAuthorityException("invalid_response", "e-MECeF submit: no uid in the answer.", permanent: false);
    }

    public async Task<FiscalSecurity> ConfirmAsync(FiscalMode mode, string token, string uid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(uid);
        var body = await SendAsync(mode, token, HttpMethod.Put, $"invoice/{Uri.EscapeDataString(uid)}/confirm", null, "confirm", cancellationToken);
        var code = Text(body, "codeMECeFDGI");
        var qr = Text(body, "qrCode");
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(qr))
        {
            throw new FiscalAuthorityException("invalid_response", "e-MECeF confirm: security elements missing.", permanent: false);
        }

        return new FiscalSecurity(code, qr, Text(body, "counters") ?? string.Empty, Text(body, "nim") ?? string.Empty, Text(body, "dateTime") ?? string.Empty);
    }

    private Uri BaseOf(FiscalMode mode) => mode switch
    {
        FiscalMode.Production => options.ProductionBaseUrl,
        FiscalMode.Sandbox => options.SandboxBaseUrl,
        _ => throw new FiscalAuthorityException("disabled", "e-MECeF is disabled.", permanent: true),
    };

    private async Task<JsonNode> SendAsync(FiscalMode mode, string token, HttpMethod method, string path, object? payload, string operation, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        using var request = new HttpRequestMessage(method, new Uri(BaseOf(mode), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (payload is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new FiscalAuthorityException("unreachable", $"e-MECeF {operation}: service unreachable.", permanent: false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FiscalAuthorityException("timeout", $"e-MECeF {operation}: service timed out.", permanent: false);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new FiscalAuthorityException("token_rejected", $"e-MECeF {operation}: token rejected (HTTP {status}).", permanent: true);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonNode? body = null;
            try
            {
                body = string.IsNullOrWhiteSpace(content) ? new JsonObject() : JsonNode.Parse(content);
            }
            catch (JsonException)
            {
            }

            // Erreur DGI : parfois avec un statut 200, parfois avec un 4xx.
            if (body is not null && Text(body, "errorCode") is { Length: > 0 } errorCode && errorCode != "0")
            {
                var description = Text(body, "errorDesc");
                throw new FiscalAuthorityException(
                    "dgi_" + Clip(errorCode, 10),
                    $"e-MECeF {operation}: {Clip(description ?? "refused", 160)}",
                    permanent: !TransientCodes.Contains(errorCode));
            }

            if (status >= 500 || status is 408 or 429)
            {
                throw new FiscalAuthorityException("unavailable", $"e-MECeF {operation}: HTTP {status}.", permanent: false);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new FiscalAuthorityException("http_" + status.ToString(CultureInfo.InvariantCulture), $"e-MECeF {operation}: HTTP {status}.", permanent: true);
            }

            return body ?? throw new FiscalAuthorityException("invalid_response", $"e-MECeF {operation}: unreadable answer.", permanent: false);
        }
    }

    private static string? Text(JsonNode node, string name) =>
        node is JsonObject json && json.TryGetPropertyValue(name, out var value) && value is JsonValue scalar
            ? scalar.TryGetValue<string>(out var text) ? text : scalar.ToJsonString().Trim('"')
            : null;

    private static string Clip(string text, int length)
    {
        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > length ? clean[..length] : clean;
    }
}
