namespace Notifications.Application;

/// <summary>Erreurs levées par les cas d'usage Notifications (les erreurs des règles métier sont dans
/// Notifications.Domain).</summary>
public static class NotificationsApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("notifications.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    public static readonly DomainError TenantNotWritable =
        new("notifications.tenant.not_writable", "The restaurant account is suspended or closed: its notifications can be read, not changed.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError UnsubscribeLinkInvalid = new("notifications.unsubscribe.link_invalid", "This unsubscribe link is not valid.");
    public static readonly DomainError ProviderNotFound = new("notifications.provider.not_found", "The notification provider does not exist.");
    public static readonly DomainError ConfigurationNotFound = new("notifications.configuration.not_found", "The notification configuration does not exist.");
    public static readonly DomainError TemplateNotFound = new("notifications.template.not_found", "The template does not exist.");
    public static readonly DomainError NotificationNotFound = new("notifications.notification.not_found", "The notification does not exist.");
    public static readonly DomainError LocationNotFound = new("notifications.location.not_found", "The location does not exist.");
    public static readonly DomainError CustomerNotFound = new("notifications.customer.not_found", "The customer does not exist or has no contact for this channel.");
    public static readonly DomainError OtpNotFound = new("notifications.otp.not_found", "The code does not exist.");
    public static readonly DomainError CampaignNotFound = new("notifications.campaign.not_found", "The campaign does not exist.");
    public static readonly DomainError DeviceNotFound = new("notifications.device.not_found", "The device is not registered.");

    public static readonly DomainError IntegrationMissing =
        new("notifications.provider.integration_missing", "No integration exists in the platform for this provider code.");
    public static readonly DomainError CredentialsIncomplete =
        new("notifications.configuration.credentials_incomplete", "Every credential field of the provider is required, and nothing else.");
    public static readonly DomainError TestNotSupported =
        new("notifications.configuration.test_not_supported", "Only an email, SMS or WhatsApp configuration can send a test message.");
    public static readonly DomainError RecipientRequired =
        new("notifications.send.recipient_required", "A notification needs a customer, a user or an address.");
    public static readonly DomainError TemplateActionUnknown = new("notifications.template.action_unknown", "Unknown template action.");
    public static readonly DomainError PageInvalid = new("notifications.page_invalid", "Skip must be zero or more and take between 1 and 100.");
    public static readonly DomainError DuplicateRequest =
        new("notifications.send.duplicate", "A notification was already created for this idempotency key.", DomainErrorKind.Conflict);
    public static readonly DomainError DuplicateEvent = new("notifications.inbox.duplicate", "This event was already processed.", DomainErrorKind.Conflict);
    public static readonly DomainError UserRequired = new("notifications.user.required", "Sign in as a user to use this feature.");

    public static readonly DomainError OtpInvalid = new("notifications.otp.invalid", "The code is not valid.");
    public static readonly DomainError OtpExpired = new("notifications.otp.expired", "The code has expired; ask for a new one.");
    public static readonly DomainError OtpAlreadyUsed = new("notifications.otp.already_used", "The code was already used.");
    public static readonly DomainError OtpExhausted = new("notifications.otp.too_many_attempts", "Too many wrong codes; ask for a new one.");
    public static readonly DomainError OtpTooManyRequests = new("notifications.otp.too_many_requests", "Too many codes were asked for; wait before asking again.");
    public static readonly DomainError OtpNotSent = new("notifications.otp.not_sent", "The code could not be sent; try again or use another channel.");
}

/// <summary>Codes de permission déclarés par le module Notifications (accordés via les rôles Staff).</summary>
public static class NotificationsPermissions
{
    /// <summary>Historique des notifications du restaurant (métadonnées, adresses masquées).</summary>
    public const string View = "notifications.view";

    /// <summary>Envoyer une notification à la main, annuler un envoi en attente.</summary>
    public const string Send = "notifications.send";

    /// <summary>Prestataires choisis, identifiants, établissements, templates du restaurant.</summary>
    public const string Configure = "notifications.configure";

    /// <summary>Préférences de notification des clients (opt-in, opt-out).</summary>
    public const string ManagePreferences = "notifications.manage_preferences";

    public static IReadOnlyList<string> All { get; } = [View, Send, Configure, ManagePreferences];
}

/// <summary>Réglages des cas d'usage, fournis par l'hôte (valeurs par défaut raisonnables).</summary>
public sealed class NotificationsSettings
{
    /// <summary>Langue d'un message quand ni la demande ni le destinataire n'en donnent une.</summary>
    public string DefaultLanguage { get; set; } = "fr";

    public TimeSpan OtpLifetime { get; set; } = TimeSpan.FromMinutes(10);

    public int OtpMaxAttempts { get; set; } = 5;

    public int OtpDigits { get; set; } = 6;

    /// <summary>Délai minimal entre deux codes pour le même destinataire et le même usage.</summary>
    public TimeSpan OtpCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Codes au plus par destinataire et par usage sur une heure glissante.</summary>
    public int OtpMaxPerHour { get; set; } = 5;

    /// <summary>Une tentative restée « en cours » plus longtemps (worker arrêté) est comptée en échec temporaire.</summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Âge à partir duquel une notification terminée est expurgée (adresse, contenu, messages des prestataires).</summary>
    public TimeSpan RedactAfter { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Page publique de désinscription (site), à laquelle le jeton est ajouté en <c>?jeton=</c>. Vide : aucun lien n'est
    /// produit et un e-mail marketing, qui l'exige, n'est pas envoyé (modèle incomplet).
    /// </summary>
    public string? UnsubscribePageUrl { get; set; }

    /// <summary>Clients traités par lot d'envoi d'une campagne (un lot = une transaction).</summary>
    public int CampaignBatchSize { get; set; } = 100;
}
