using System.Security.Cryptography;
using System.Text;
using Identity.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.Security;

internal sealed class NoCompromisedPasswordCheck : ICompromisedPasswords
{
    public Task<bool> IsCompromisedAsync(string password, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

/// <summary>
/// API « range » de Have I Been Pwned. Seuls les 5 premiers caractères du SHA-1 du mot de passe quittent le serveur
/// (k-anonymat) ; le mot de passe lui-même jamais. Si le service est injoignable, le mot de passe est accepté :
/// la panne d'un tiers ne doit pas bloquer les inscriptions.
/// </summary>
internal sealed class PwnedPasswordsClient(HttpClient http, ILogger<PwnedPasswordsClient> logger) : ICompromisedPasswords
{
    public static readonly Uri DefaultBaseAddress = new("https://api.pwnedpasswords.com/");

    public async Task<bool> IsCompromisedAsync(string password, CancellationToken cancellationToken = default)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{hash[..5]}");
            // Remplit la réponse pour que sa taille ne trahisse pas la plage demandée.
            request.Headers.Add("Add-Padding", "true");
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var suffix = hash[5..];
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = line.IndexOf(':');
                if (separator > 0
                    && line.AsSpan(0, separator).Equals(suffix, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line.AsSpan(separator + 1), out var count)
                    && count > 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The compromised-password check could not be done; the password is accepted.");
            return false;
        }
    }
}
