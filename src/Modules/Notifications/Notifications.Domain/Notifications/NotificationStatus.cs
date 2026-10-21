namespace Notifications.Domain;

/// <summary>
/// Cycle de vie d'une notification. Valeurs explicites et définitives (stockées).
/// Pending -> Processing -> Sent -> (PartiallyDelivered ->) Delivered ; Scheduled -> Processing à l'échéance ;
/// Processing -> Failed -> Pending (relance) ; Pending / Scheduled / Failed -> Cancelled. Delivered et Cancelled sont
/// terminaux ; Sent et PartiallyDelivered n'attendent plus que le statut de remise du prestataire.
/// </summary>
public enum NotificationStatus
{
    /// <summary>À envoyer dès que possible (ou à l'échéance d'une relance).</summary>
    Pending = 1,

    /// <summary>À envoyer à <see cref="Notification.ScheduledAtUtc"/>, pas avant.</summary>
    Scheduled = 2,

    /// <summary>Une tentative est en cours chez le prestataire.</summary>
    Processing = 3,

    /// <summary>Accepté par le prestataire.</summary>
    Sent = 4,

    /// <summary>Remis en partie (par exemple sur une partie des appareils du destinataire).</summary>
    PartiallyDelivered = 5,

    /// <summary>Remise confirmée par le prestataire.</summary>
    Delivered = 6,

    /// <summary>La dernière tentative a échoué ; une relance est possible tant qu'il reste des tentatives.</summary>
    Failed = 7,

    Cancelled = 8,
}

public static class NotificationStatuses
{
    /// <summary>Le prestataire a accepté le message : plus aucun envoi possible, seulement des nouvelles de la remise.</summary>
    public static bool IsHandedOver(this NotificationStatus status) =>
        status is NotificationStatus.Sent or NotificationStatus.PartiallyDelivered or NotificationStatus.Delivered;
}

/// <summary>Affichage d'une notification in-app (§ 24 du cahier) : non lue, lue, rangée.</summary>
public enum InAppState
{
    Unread = 1,
    Read = 2,
    Archived = 3,
}
