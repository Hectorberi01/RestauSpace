namespace Notifications.Domain;

/// <summary>Pourquoi un message ne part pas vers cette personne sur ce canal.</summary>
public enum PermissionRefusal
{
    None = 0,

    /// <summary>La personne a refusé cette finalité sur ce canal (opt-out).</summary>
    OptedOut = 1,

    /// <summary>Marketing vers quelqu'un qui n'est pas un client.</summary>
    NotACustomer = 2,

    /// <summary>Marketing sans preuve de consentement fournie par Customers.</summary>
    ConsentRequired = 3,

    /// <summary>La preuve dit non (jamais accordé, ou retiré).</summary>
    ConsentNotGranted = 4,

    /// <summary>La preuve concerne un autre client ou un autre canal.</summary>
    ConsentMismatch = 5,
}

public sealed record PermissionDecision(PermissionRefusal Refusal)
{
    public static PermissionDecision Allowed { get; } = new(PermissionRefusal.None);

    public bool IsAllowed => Refusal == PermissionRefusal.None;
}

/// <summary>
/// La part de la policy d'envoi qui concerne le DESTINATAIRE : sa préférence et, pour le marketing, son
/// consentement. Règles :
/// 1. un message essentiel (sécurité du compte) passe toujours ; il n'est jamais marketing ;
/// 2. marketing : seulement vers un client, non désinscrit du canal, et, hors in-app, avec une preuve de consentement
///    ACCORDÉE pour CE client et CE canal (une préférence ne remplace jamais le consentement) ;
/// 3. transactionnel : non désinscrit du canal.
/// Configuration du restaurant, modèle, prestataire, quotas et fréquence sont vérifiés par la policy de l'Application.
/// </summary>
public static class NotificationPermission
{
    /// <param name="preferences">Null : la personne n'a rien choisi (valeurs par défaut).</param>
    /// <param name="consent">Pour le marketing : la preuve lue dans Customers juste avant.</param>
    public static PermissionDecision Decide(
        NotificationTypeDefinition type,
        NotificationChannel channel,
        RecipientId recipient,
        NotificationPreference? preferences,
        ConsentProof? consent = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(recipient);
        NotificationChannels.Require(channel);
        if (preferences is not null && preferences.Owner != recipient)
        {
            throw new ArgumentException("The preferences belong to another person.", nameof(preferences));
        }

        if (type.IsEssential)
        {
            return PermissionDecision.Allowed;
        }

        if (preferences?.IsMuted(type.Key) == true)
        {
            return new PermissionDecision(PermissionRefusal.OptedOut);
        }

        var allowedByPreference = preferences?.Allows(type.Purpose, channel) ?? NotificationPreference.AllowedByDefault(recipient, type.Purpose);
        if (!type.IsMarketing)
        {
            return allowedByPreference ? PermissionDecision.Allowed : new PermissionDecision(PermissionRefusal.OptedOut);
        }

        if (!recipient.IsCustomer)
        {
            return new PermissionDecision(PermissionRefusal.NotACustomer);
        }

        if (!allowedByPreference)
        {
            return new PermissionDecision(PermissionRefusal.OptedOut);
        }

        // In-app : affiché dans l'application que la personne utilise, sans canal de consentement externe.
        if (channel.ToConsentChannel() is not { } consentChannel)
        {
            return PermissionDecision.Allowed;
        }

        if (consent is null)
        {
            return new PermissionDecision(PermissionRefusal.ConsentRequired);
        }

        if (consent.CustomerId.Value != recipient.Id || consent.Channel != consentChannel)
        {
            return new PermissionDecision(PermissionRefusal.ConsentMismatch);
        }

        return consent.IsGranted ? PermissionDecision.Allowed : new PermissionDecision(PermissionRefusal.ConsentNotGranted);
    }
}
