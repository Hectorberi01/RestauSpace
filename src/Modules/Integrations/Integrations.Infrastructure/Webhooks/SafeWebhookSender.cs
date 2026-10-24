using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Integrations.Application.Abstractions;
using Microsoft.Extensions.Logging;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Integrations.Infrastructure.Webhooks;

/// <summary>
/// Envoi des webhooks, protégé contre la SSRF : le nom est résolu ICI, chaque adresse obtenue doit être publique, et la
/// connexion se fait à ces adresses validées (pas de seconde résolution : pas de DNS rebinding). Ni proxy, ni
/// redirection, ni cookies ; temps borné ; la réponse n'est pas lue (seul le code compte). Le journal ne contient ni
/// l'URL complète, ni le corps, ni les en-têtes (la signature en fait partie).
/// </summary>
internal sealed class SafeWebhookSender : IWebhookSender, IDisposable
{
    public const string BlockedAddress = "blocked_address";
    public const string Timeout = "timeout";
    public const string NetworkError = "network_error";

    private readonly HttpClient _client;
    private readonly ILogger<SafeWebhookSender> _logger;

    public SafeWebhookSender(IntegrationsOptions options, ILogger<SafeWebhookSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxResponseHeadersLength = 16,
            ConnectCallback = ConnectToPublicAddressAsync,
        };
        _client = new HttpClient(handler) { Timeout = options.SendTimeout };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RestauSpace-Webhooks", "1.0"));
    }

    public async Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        try
        {
            // En-têtes seulement : le corps de la réponse n'est jamais lu (ni taille, ni contenu à craindre).
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;
            return status is >= 200 and < 300 ? WebhookSendResult.Success : WebhookSendResult.Failure("http_" + status.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (HttpRequestException exception) when (IsBlocked(exception))
        {
            _logger.LogWarning("Webhook to host {Host} refused: non-public address.", request.Url.Host);
            return WebhookSendResult.Failure(BlockedAddress);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WebhookSendResult.Failure(Timeout);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogInformation("Webhook to host {Host} failed: {Error}.", request.Url.Host, exception.HttpRequestError);
            return WebhookSendResult.Failure(NetworkError);
        }
    }

    public void Dispose() => _client.Dispose();

    private static bool IsBlocked(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is NonPublicAddressException)
            {
                return true;
            }
        }

        return false;
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var socket = await PublicAddresses.ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, cancellationToken);
        return new NetworkStream(socket, ownsSocket: true);
    }
}
