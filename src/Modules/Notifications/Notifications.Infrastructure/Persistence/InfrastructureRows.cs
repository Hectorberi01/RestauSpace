namespace Notifications.Infrastructure.Persistence;

// Tables propres à l'Infrastructure : jamais des agrégats, jamais exposées hors du module.

/// <summary>
/// Ligne du coffre (<c>notification_secrets</c>) : identifiants d'un compte chez un prestataire (clé API, mot de passe
/// SMTP, jeton WhatsApp, clé privée Firebase, secret de webhook), CHIFFRÉS. Jamais chargée avec un agrégat.
/// </summary>
internal sealed class NotificationSecret
{
    public const int MaxCipherTextLength = 16384;

    public string Reference { get; set; } = null!;

    /// <summary>Le restaurant propriétaire (tiré de la référence) ; null pour un compte de la plateforme.</summary>
    public Guid? TenantId { get; set; }

    public string CipherText { get; set; } = null!;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// Événement entrant déjà traité par un consommateur (<c>inbox_messages</c>, Transactional Inbox) : écrit dans la même
/// transaction que ce qu'il a produit. Le même événement reçu deux fois ne produit rien la seconde fois.
/// </summary>
internal sealed class InboxMessage
{
    public const int MaxConsumerLength = 100;
    public const int MaxEventIdLength = 200;

    public string Consumer { get; set; } = null!;

    /// <summary>« {module}:{id} » pour un événement de l'outbox d'un module ; l'id d'un message Kafka plus tard.</summary>
    public string EventId { get; set; } = null!;

    public string EventType { get; set; } = null!;

    public DateTimeOffset ProcessedAtUtc { get; set; }
}

/// <summary>
/// Consommation d'un restaurant (<c>notification_usage</c>, § 33) : nombre de notifications remises au prestataire, par
/// mois, canal et prestataire. Lue par les quotas, Billing et Reporting.
/// </summary>
internal sealed class NotificationUsage
{
    public Guid TenantId { get; set; }

    /// <summary>Premier jour du mois (UTC).</summary>
    public DateOnly Period { get; set; }

    public int Channel { get; set; }

    public string Provider { get; set; } = null!;

    public long Count { get; set; }
}

/// <summary>Valeurs explicites et définitives (stockées).</summary>
public enum WebhookEventKind
{
    Delivered = 1,
    PartiallyDelivered = 2,
    Failed = 3,

    /// <summary>Ouvert, cliqué, désabonné... : gardé pour l'audit, sans effet sur le statut.</summary>
    Ignored = 4,
}

/// <summary>
/// Webhook d'un prestataire (<c>provider_webhook_events</c>), signature vérifiée, conservé avec sa lecture normalisée et
/// appliqué en arrière-plan. Unique par (prestataire, id d'événement qualifié par le compte signataire) : un webhook
/// renvoyé n'est enregistré qu'une fois.
/// Le corps brut n'est PAS gardé (il peut contenir l'adresse et le contenu du message) : seulement ce qui sert.
/// </summary>
internal sealed class NotificationWebhookEvent
{
    public const int MaxEventIdLength = 255;
    public const int MaxErrorLength = 500;

    public long Id { get; set; }

    public string Provider { get; set; } = null!;

    /// <summary>« p:{id} » (compte de la plateforme) ou « t:{tenant}:{id} » (compte du restaurant), voir <c>WebhookEventScope</c>.</summary>
    public string EventId { get; set; } = null!;

    public WebhookEventKind Kind { get; set; }

    public string? ProviderMessageId { get; set; }

    public string? FailureCode { get; set; }

    public bool IsPermanentFailure { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAtUtc { get; set; }

    /// <summary>Code de l'erreur de traitement, jamais un message du prestataire.</summary>
    public string? LastError { get; set; }
}
