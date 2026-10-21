using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Storefront.Application.Abstractions;

namespace Storefront.Infrastructure;

/// <summary>
/// Cloudflare Turnstile : le jeton du widget est vérifié une fois auprès de <c>siteverify</c> avec la clé secrète de la
/// plateforme. La clé secrète et le jeton ne sont jamais journalisés ; un service injoignable fait échouer le contrôle.
/// </summary>
internal sealed class TurnstileHumanCheck(HttpClient http, StorefrontOptions options, ILogger<TurnstileHumanCheck> logger) : IHumanCheck
{
    public const int MaxTokenLength = 2048;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Turnstile.SiteKey) && !string.IsNullOrWhiteSpace(options.Turnstile.SecretKey);

    public string? SiteKey => IsAvailable ? options.Turnstile.SiteKey : null;

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        var fields = new Dictionary<string, string> { ["secret"] = options.Turnstile.SecretKey!, ["response"] = token };
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            fields["remoteip"] = remoteIp;
        }

        try
        {
            using var response = await http.PostAsync(options.Turnstile.VerifyUrl, new FormUrlEncodedContent(fields), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Turnstile verification answered {Status}.", (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResult>(cancellationToken);
            return result?.Success == true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Turnstile verification unavailable: {Error}.", exception.GetType().Name);
            return false;
        }
    }

    private sealed record SiteVerifyResult([property: JsonPropertyName("success")] bool Success);
}
