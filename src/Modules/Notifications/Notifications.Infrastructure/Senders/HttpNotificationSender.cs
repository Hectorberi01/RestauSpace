using System.Net;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Infrastructure.Senders;

/// <summary>
/// Un envoi = une requête HTTP au prestataire puis la lecture de sa réponse. Les sous-classes construisent la requête
/// et traduisent la réponse ; réseau coupé et délai dépassé sont traités ici (échec temporaire, relance possible).
/// </summary>
internal abstract class HttpNotificationSender(NotificationProviderCode provider, NotificationChannel channel, HttpClient http) : INotificationSender
{
    public NotificationProviderCode Provider => provider;

    public NotificationChannel Channel => channel;

    protected abstract Task<HttpRequestMessage> BuildAsync(NotificationSendRequest request, CancellationToken cancellationToken);

    protected abstract NotificationSendResult Read(HttpStatusCode status, string body);

    public async Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = await BuildAsync(request, cancellationToken);
        var (status, body, transport) = await ProviderSupport.CallAsync(http, message, cancellationToken);
        return transport is not null ? NotificationSendResult.Failed(transport) : Read(status, body);
    }

    /// <summary>Accepté avec l'identifiant du message, ou accepté sans identifiant si le prestataire n'en donne pas.</summary>
    protected static NotificationSendResult Accepted(string? messageId) =>
        NotificationSendResult.Accepted(
            messageId is { Length: > 0 } id && id.Length <= ProviderMessageId.MaxLength && !id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
                ? ProviderMessageId.Create(id)
                : null);
}
