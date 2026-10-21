using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Integrations.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Integrations.Infrastructure.Webhooks;

/// <summary>
/// Adresses joignables par un webhook : uniquement des adresses publiques routables. Plus strict que le contrôle lexical
/// de <c>WebhookUrl</c> : appliqué à CHAQUE adresse obtenue par la résolution DNS, juste avant la connexion.
/// </summary>
public static class PublicAddresses
{
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicV6(address.GetAddressBytes()),
            _ => false,
        };
    }

    private static bool IsPublicV4(byte[] b) => b[0] switch
    {
        0 or 10 or 127 => false,                     // « ce réseau », privé, loopback
        100 => b[1] is < 64 or > 127,                // CGNAT 100.64.0.0/10
        169 => b[1] != 254,                          // link-local (métadonnées des clouds : 169.254.169.254)
        172 => b[1] is < 16 or > 31,                 // privé 172.16.0.0/12
        192 => !(b[1] == 168 || (b[1] == 0 && b[2] is 0 or 2)), // privé, IETF 192.0.0.0/24, documentation
        198 => b[1] is not (18 or 19) && !(b[1] == 51 && b[2] == 100), // bancs d'essai, documentation
        203 => !(b[1] == 0 && b[2] == 113),          // documentation
        >= 224 => false,                             // multicast, réservé, diffusion
        _ => true,
    };

    private static bool IsPublicV6(byte[] b)
    {
        // Seul l'espace unicast global 2000::/3 est routable sur Internet.
        if ((b[0] & 0xE0) != 0x20)
        {
            return false;
        }

        // Documentation 2001:db8::/32, Teredo 2001::/32 (tunnel vers une IPv4 quelconque).
        if (b[0] == 0x20 && b[1] == 0x01 && ((b[2] == 0x0D && b[3] == 0xB8) || (b[2] == 0 && b[3] == 0)))
        {
            return false;
        }

        // 6to4 2002::/16 : l'adresse IPv4 embarquée décide.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublicV4([b[2], b[3], b[4], b[5]]);
        }

        return true;
    }
}

/// <summary>La connexion a été refusée parce que le nom se résout vers une adresse non publique.</summary>
internal sealed class BlockedAddressException() : Exception("The webhook host resolves to a non-public address.");

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
            if (current is BlockedAddressException)
            {
                return true;
            }
        }

        return false;
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);
        // Une seule adresse non publique suffit à refuser : un nom « mixte » est le signe d'un rebinding.
        if (addresses.Length == 0 || !addresses.All(PublicAddresses.IsPublic))
        {
            throw new BlockedAddressException();
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
