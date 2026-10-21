namespace Notifications.Domain;

/// <summary>Valeurs explicites et définitives (stockées).</summary>
public enum NotificationDeliveryStatus
{
    /// <summary>Appel au prestataire en cours.</summary>
    InProgress = 1,

    /// <summary>Accepté par le prestataire.</summary>
    Sent = 2,

    /// <summary>Remis en partie (une partie des appareils, par exemple).</summary>
    PartiallyDelivered = 3,

    /// <summary>Remise confirmée par le prestataire.</summary>
    Delivered = 4,

    /// <summary>Refusé par le prestataire, ou échec signalé après coup (rebond, par exemple).</summary>
    Failed = 5,
}

/// <summary>
/// Une tentative réelle auprès d'un prestataire (§ 14 du cahier) : quel prestataire, l'id qu'il a donné au message,
/// l'issue, le code d'échec et, pour un échec relancé, la date de la relance. Seule <see cref="Notification"/> crée et
/// fait évoluer ses tentatives (méthodes <c>internal</c>) : l'agrégat garde la cohérence entre son statut et celui de
/// sa dernière tentative.
/// </summary>
public sealed class NotificationDelivery : Entity<NotificationDeliveryId>
{
    private NotificationDelivery(
        NotificationDeliveryId id, NotificationProviderCode providerCode, int attemptNumber, DateTimeOffset startedAtUtc)
        : base(id)
    {
        ProviderCode = providerCode;
        AttemptNumber = attemptNumber;
        StartedAtUtc = startedAtUtc;
        Status = NotificationDeliveryStatus.InProgress;
    }

    // Réservé à l'ORM.
    private NotificationDelivery()
    {
        ProviderCode = null!;
    }

    /// <summary>Le prestataire de CETTE tentative (une relance peut passer par un prestataire de secours).</summary>
    public NotificationProviderCode ProviderCode { get; private set; }

    /// <summary>Connu une fois le message accepté (ou dès le premier rapport de remise, s'il arrive avant).</summary>
    public ProviderMessageId? ProviderMessageId { get; private set; }

    public NotificationDeliveryStatus Status { get; private set; }

    /// <summary>Rang de la tentative dans sa notification, à partir de 1.</summary>
    public int AttemptNumber { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    /// <summary>Fin de l'appel au prestataire (acceptation ou refus) ; null tant qu'il est en cours.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Remise confirmée par le prestataire.</summary>
    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    public NotificationFailure? Failure { get; private set; }

    /// <summary>Code de l'échec, journalisable ; null si la tentative n'a pas échoué.</summary>
    public string? FailureCode => Failure?.Code;

    /// <summary>Date de la tentative suivante, quand cet échec a été relancé.</summary>
    public DateTimeOffset? NextRetryAtUtc { get; private set; }

    public bool IsInProgress => Status == NotificationDeliveryStatus.InProgress;

    /// <summary>Le prestataire détient le message : Sent, PartiallyDelivered ou Delivered.</summary>
    public bool IsAccepted =>
        Status is NotificationDeliveryStatus.Sent or NotificationDeliveryStatus.PartiallyDelivered or NotificationDeliveryStatus.Delivered;

    internal static NotificationDelivery Create(NotificationProviderCode providerCode, int attemptNumber, DateTimeOffset startedAtUtc) =>
        new(NotificationDeliveryId.New(), providerCode, attemptNumber, startedAtUtc);

    /// <summary>Vrai si cette tentative porte cet id, ou n'en a pas encore (le rapport arrive avant l'acceptation).</summary>
    internal bool Matches(ProviderMessageId? providerMessageId) =>
        providerMessageId is null || ProviderMessageId is null || ProviderMessageId == providerMessageId;

    internal void RecordProviderMessageId(ProviderMessageId? providerMessageId) => ProviderMessageId ??= providerMessageId;

    internal void MarkAsSent(ProviderMessageId? providerMessageId, DateTimeOffset at)
    {
        Status = NotificationDeliveryStatus.Sent;
        CompletedAtUtc = at;
        ProviderMessageId ??= providerMessageId;
    }

    internal void MarkAsPartiallyDelivered(ProviderMessageId? providerMessageId, DateTimeOffset at)
    {
        CompletedAtUtc ??= at;
        ProviderMessageId ??= providerMessageId;
        Status = NotificationDeliveryStatus.PartiallyDelivered;
    }

    internal void MarkAsDelivered(ProviderMessageId? providerMessageId, DateTimeOffset at)
    {
        CompletedAtUtc ??= at;
        ProviderMessageId ??= providerMessageId;
        Status = NotificationDeliveryStatus.Delivered;
        DeliveredAtUtc = at;
    }

    internal void MarkAsFailed(NotificationFailure failure, ProviderMessageId? providerMessageId, DateTimeOffset at)
    {
        CompletedAtUtc ??= at;
        ProviderMessageId ??= providerMessageId;
        Status = NotificationDeliveryStatus.Failed;
        Failure = failure;
    }

    internal void ScheduleRetry(DateTimeOffset nextRetryAtUtc) => NextRetryAtUtc = nextRetryAtUtc;

    // Le message du prestataire peut citer l'adresse du destinataire.
    internal void EraseFailureReason() => Failure = Failure?.WithoutReason();
}
