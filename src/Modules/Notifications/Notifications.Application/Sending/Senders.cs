using Notifications.Domain;

namespace Notifications.Application.Sending;

/// <summary>
/// Ce qu'un prestataire reçoit pour UNE tentative. Le contenu est déjà rendu ; aucun identifiant de compte ici (le sender
/// les a reçus à sa création). SÉCURITÉ : <see cref="ToString"/> ne montre ni l'adresse ni le contenu.
/// </summary>
public sealed record NotificationSendRequest(
    NotificationId NotificationId,
    NotificationDeliveryId DeliveryId,
    TenantId? TenantId,
    string Type,
    NotificationPriority Priority,
    NotificationRecipient Recipient,
    NotificationContent Content)
{
    public NotificationChannel Channel => Recipient.Channel;

    public override string ToString() => $"{Type} {Channel} notification {NotificationId} (delivery {DeliveryId})";
}

/// <summary>Réponse d'un prestataire : accepté (avec l'id de son message, et parfois déjà remis) ou refusé.</summary>
public sealed record NotificationSendResult(bool IsAccepted, ProviderMessageId? MessageId, bool IsDelivered, NotificationFailure? Failure)
{
    public static NotificationSendResult Accepted(ProviderMessageId? messageId) => new(true, messageId, false, null);

    /// <summary>Remis immédiatement (in-app : la notification enregistrée EST affichée).</summary>
    public static NotificationSendResult Delivered(ProviderMessageId? messageId = null) => new(true, messageId, true, null);

    public static NotificationSendResult Failed(NotificationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new NotificationSendResult(false, null, false, failure);
    }
}

/// <summary>
/// Un prestataire, branché sur UN compte (celui de la plateforme ou d'un restaurant), pour UN canal. L'Application ne
/// connaît aucun SDK : Resend, Twilio, WhatsApp, Firebase, APNs, SMTP... sont des implémentations de l'Infrastructure.
/// Une erreur réseau ou de protocole est RENVOYÉE comme échec temporaire, jamais levée avec la réponse brute (elle peut
/// citer l'adresse ou un secret).
/// </summary>
public interface INotificationSender
{
    NotificationProviderCode Provider { get; }

    NotificationChannel Channel { get; }

    Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Une intégration de prestataire présente dans le code de la plateforme.</summary>
/// <param name="CredentialFields">Champs que le compte doit fournir (ex. <c>api_key</c>, <c>webhook_secret</c>).</param>
public sealed record NotificationIntegration(
    NotificationProviderCode Provider, IReadOnlyList<NotificationChannel> Channels, IReadOnlyList<string> CredentialFields);

/// <summary>
/// (canal + prestataire + compte) -> le sender. Choisit les identifiants selon le mode de la configuration du restaurant
/// (compte de la plateforme ou du restaurant, lus au coffre au moment de l'envoi, jamais gardés) ; aucun aiguillage
/// géant : une intégration par prestataire, enregistrée dans l'Infrastructure (ajouter un prestataire ne touche pas
/// au Domain).
/// </summary>
public interface INotificationSenderResolver
{
    IReadOnlyList<NotificationIntegration> Integrations { get; }

    NotificationIntegration? FindIntegration(NotificationProviderCode provider);

    /// <summary>Null si aucune intégration n'existe pour ce prestataire et ce canal, ou si le compte n'a pas d'identifiants.</summary>
    /// <param name="configuration">La configuration du restaurant ; null pour un envoi de la plateforme ou in-app.</param>
    Task<INotificationSender?> ResolveAsync(
        NotificationProviderCode provider,
        NotificationChannel channel,
        RestaurantNotificationConfiguration? configuration,
        CancellationToken cancellationToken = default);
}
