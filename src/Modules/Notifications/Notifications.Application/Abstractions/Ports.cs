using Notifications.Domain;

namespace Notifications.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte authentifié de la requête, ou du message traité
// (jamais du corps d'une requête). Une ressource d'un autre tenant n'est tout simplement pas trouvée.

/// <summary>Le catalogue des prestataires de la plateforme (pas d'un tenant).</summary>
public interface INotificationProviderRepository
{
    Task<NotificationProvider?> FindAsync(NotificationProviderId id, CancellationToken cancellationToken = default);

    Task<NotificationProvider?> FindByCodeAsync(NotificationProviderCode code, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par canal puis nom.</summary>
    Task<IReadOnlyList<NotificationProvider>> ListAsync(CancellationToken cancellationToken = default);

    void Add(NotificationProvider provider);
}

public interface IRestaurantNotificationConfigurationRepository
{
    Task<RestaurantNotificationConfiguration?> FindAsync(
        TenantId tenantId, RestaurantNotificationConfigurationId id, CancellationToken cancellationToken = default);

    Task<RestaurantNotificationConfiguration?> FindByProviderAsync(
        TenantId tenantId, NotificationProviderCode provider, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule.</summary>
    Task<IReadOnlyList<RestaurantNotificationConfiguration>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule : les configurations d'un canal (choix du prestataire).</summary>
    Task<IReadOnlyList<RestaurantNotificationConfiguration>> ListForChannelAsync(
        TenantId tenantId, NotificationChannel channel, CancellationToken cancellationToken = default);

    void Add(RestaurantNotificationConfiguration configuration);
}

/// <summary>Templates d'un tenant ou de la plateforme (<c>owner</c> null). Un tenant ne trouve JAMAIS ceux d'un autre.</summary>
public interface INotificationTemplateRepository : ITemplateUniquenessChecker
{
    /// <summary>Le template de CE propriétaire exactement : un restaurant ne trouve pas un template de la plateforme ici.</summary>
    Task<NotificationTemplate?> FindAsync(TenantId? owner, TemplateId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule : les versions du tenant ET de la plateforme pour (type, canal, langue), pour la résolution.</summary>
    Task<IReadOnlyList<NotificationTemplate>> ListCandidatesAsync(
        TenantId? tenantId, TemplateKey key, NotificationChannel channel, NotificationLanguage language, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule : les templates de ce propriétaire.</summary>
    Task<IReadOnlyList<NotificationTemplate>> ListAsync(TenantId? owner, CancellationToken cancellationToken = default);

    void Add(NotificationTemplate template);
}

public interface INotificationPreferenceRepository
{
    Task<NotificationPreference?> FindAsync(TenantId tenantId, RecipientId owner, CancellationToken cancellationToken = default);

    /// <summary>
    /// La même lecture HORS du tenant de la requête : réservée au lien de désinscription, dont le tenant vient d'un jeton
    /// signé par la plateforme (<see cref="IUnsubscribeLinks"/>), jamais d'une valeur libre.
    /// </summary>
    Task<NotificationPreference?> FindForSignedLinkAsync(TenantId tenantId, RecipientId owner, CancellationToken cancellationToken = default);

    void Add(NotificationPreference preference);
}

/// <summary>Ce que porte un lien de désinscription : le restaurant, son client et le canal du message reçu.</summary>
public sealed record UnsubscribeClaim(TenantId TenantId, CustomerId CustomerId, NotificationChannel Channel);

/// <summary>
/// Jetons des liens de désinscription marketing, signés (HMAC) par la plateforme : ils ne se devinent pas et ne se
/// modifient pas. Sans durée de vie (un e-mail ancien doit toujours permettre de se désinscrire). Aucune donnée
/// personnelle en clair : des identifiants opaques et le canal.
/// </summary>
public interface IUnsubscribeLinks
{
    string Issue(UnsubscribeClaim claim);

    /// <summary>Null pour un jeton absent, mal formé ou dont la signature ne correspond pas.</summary>
    UnsubscribeClaim? Read(string? token);
}

/// <param name="From">Créées à partir de (inclus).</param>
/// <param name="To">Créées avant (exclu).</param>
public sealed record NotificationFilter(
    NotificationStatus? Status = null,
    NotificationChannel? Channel = null,
    TemplateKey? Type = null,
    RecipientId? Recipient = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public interface INotificationRepository
{
    /// <summary>La notification de CE propriétaire (tenant, ou plateforme quand <paramref name="owner"/> est null).</summary>
    Task<Notification?> FindAsync(TenantId? owner, NotificationId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, les plus récentes d'abord.</summary>
    Task<IReadOnlyList<Notification>> ListAsync(
        TenantId tenantId, NotificationFilter filter, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule : la boîte in-app d'un utilisateur dans un restaurant, les plus récentes d'abord.</summary>
    Task<IReadOnlyList<Notification>> ListInAppAsync(
        TenantId tenantId, UserId userId, bool includeArchived, int skip, int take, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(TenantId? owner, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<Notification?> FindByIdempotencyKeyAsync(TenantId? owner, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <param name="idempotencyKey">Une seule notification par clé et par propriétaire (index unique en dernier recours).</param>
    void Add(Notification notification, string? idempotencyKey = null);

    /// <summary>Notifications du tenant créées après <paramref name="since"/> sur ces canaux et de ces types.</summary>
    Task<NotificationWindowCount> CountCreatedSinceAsync(
        TenantId tenantId, IReadOnlyCollection<NotificationChannel> channels, IReadOnlyCollection<TemplateKey> types, DateTimeOffset since,
        CancellationToken cancellationToken = default);
}

/// <param name="OldestAtUtc">Création de la plus ancienne comptée ; null si aucune.</param>
public sealed record NotificationWindowCount(int Count, DateTimeOffset? OldestAtUtc);

/// <summary>Une notification vue d'un traitement de la PLATEFORME (worker, webhook) : son id et son propriétaire.</summary>
public sealed record NotificationLocator(NotificationId Id, TenantId? TenantId);

/// <summary>
/// Lectures TOUS TENANTS pour les traitements d'arrière-plan. Le résultat sert à agir ensuite POUR ce tenant
/// (<see cref="BackgroundTenant"/>), jamais à modifier directement.
/// </summary>
public sealed record NotificationCount(NotificationChannel Channel, NotificationStatus Status, int Count);

public interface INotificationDirectory
{
    /// <summary>À envoyer (Pending ou Scheduled arrivées à échéance), les plus urgentes et les plus anciennes d'abord.</summary>
    Task<IReadOnlyList<NotificationLocator>> ListDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default);

    /// <summary>En cours depuis avant <paramref name="startedBefore"/> : le worker qui les traitait s'est arrêté.</summary>
    Task<IReadOnlyList<NotificationLocator>> ListStuckAsync(DateTimeOffset startedBefore, int take, CancellationToken cancellationToken = default);

    /// <summary>Terminées depuis avant <paramref name="before"/> et pas encore expurgées.</summary>
    Task<IReadOnlyList<NotificationLocator>> ListToRedactAsync(DateTimeOffset before, int take, CancellationToken cancellationToken = default);

    /// <param name="signedBy">Tenant dont le compte a signé le rapport : seules ses notifications ; null = compte de la plateforme.</param>
    Task<NotificationLocator?> FindByProviderMessageAsync(
        NotificationProviderCode provider, ProviderMessageId messageId, TenantId? signedBy, CancellationToken cancellationToken = default);

    /// <summary>« Dead letter » : en échec et jamais plus envoyées, les plus récentes d'abord.</summary>
    Task<IReadOnlyList<Notification>> ListAbandonedAsync(int skip, int take, CancellationToken cancellationToken = default);

    Task<NotificationLocator?> LocateAsync(NotificationId id, CancellationToken cancellationToken = default);

    /// <summary>Administration (journal d'envoi) : toutes les notifications, les plus récentes d'abord.</summary>
    Task<IReadOnlyList<Notification>> ListRecentAsync(
        NotificationChannel? channel, NotificationStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Administration (indicateurs) : notifications créées depuis <paramref name="since"/>, par canal et statut.</summary>
    Task<IReadOnlyList<NotificationCount>> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Administration (indicateurs) : pour les <paramref name="take"/> derniers envois acceptés depuis <paramref name="since"/>,
    /// le délai entre la création (ou l'échéance d'un envoi programmé) et l'acceptation par le prestataire.
    /// </summary>
    Task<IReadOnlyList<TimeSpan>> SendDelaysSinceAsync(DateTimeOffset since, int take, CancellationToken cancellationToken = default);

    /// <summary>Administration de la plateforme (dead letter) : la notification, suivie pour modification, quel que soit son tenant.</summary>
    Task<Notification?> LoadAnyAsync(NotificationId id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Le coffre des identifiants des comptes chez les prestataires (clé API, mot de passe SMTP, jeton WhatsApp, clé privée
/// Firebase, secret de webhook). Seule l'Infrastructure voit les valeurs ; les agrégats ne gardent que la
/// <see cref="NotificationCredentialReference"/>. Les valeurs ne doivent JAMAIS apparaître dans un log, un événement,
/// une exception ou une réponse.
/// </summary>
public interface INotificationSecretStore
{
    Task StoreAsync(NotificationCredentialReference reference, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken = default);

    /// <summary>Null si rien n'est rangé à cette référence.</summary>
    Task<IReadOnlyDictionary<string, string>?> ReadAsync(NotificationCredentialReference reference, CancellationToken cancellationToken = default);
}

/// <summary>Transactional Inbox : l'événement entrant est noté traité dans la transaction de ce qu'il produit.</summary>
public interface INotificationInbox
{
    Task<bool> HasProcessedAsync(string consumer, string eventId, CancellationToken cancellationToken = default);

    /// <summary>Écrit à la prochaine sauvegarde de l'unit of work ; un doublon simultané y lève <c>notifications.inbox.duplicate</c>.</summary>
    void MarkProcessed(string consumer, string eventId, string eventType, DateTimeOffset processedAtUtc);
}

/// <summary>Consommation par restaurant (§ 33), écrite quand une notification est remise au prestataire.</summary>
public interface INotificationUsage
{
    Task RecordAsync(TenantId tenantId, NotificationChannel channel, NotificationProviderCode provider, DateTimeOffset at, CancellationToken cancellationToken = default);

    Task<long> CountAsync(TenantId tenantId, NotificationChannel channel, DateTimeOffset monthOf, CancellationToken cancellationToken = default);
}

/// <summary>
/// Quotas du restaurant (§ 32) : contrat avec Billing / Entitlements, fourni par l'hôte. Les plans SaaS ne sont jamais
/// dans ce module.
/// </summary>
public interface INotificationEntitlementChecker
{
    /// <summary>Nombre de notifications permises par mois sur ce canal ; null = sans limite.</summary>
    Task<long?> MonthlyLimitAsync(TenantId tenantId, NotificationChannel channel, CancellationToken cancellationToken = default);
}

/// <summary>Ce que Notifications doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface INotificationTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Le nom affiché du restaurant (enseigne), null s'il est inconnu.</summary>
    Task<string?> DisplayNameAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Un établissement du tenant, fourni par l'hôte depuis Locations ; null s'il n'existe pas dans CE tenant.</summary>
public interface INotificationLocations
{
    Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    /// <summary>Pays des établissements du tenant ; vide s'il n'en a aucun ou si c'est un autre tenant que celui du contexte.</summary>
    Task<IReadOnlyList<CountryCode>> CountriesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Les coordonnées minimales d'un client, fournies par l'hôte depuis Customers.</summary>
/// <param name="FirstName">Pour la variable <c>customer.firstName</c> ; null si inconnu.</param>
/// <param name="Language">Langue préférée (sous-étiquette primaire) ; null si inconnue.</param>
public sealed record CustomerContact(CustomerId CustomerId, Email? Email, PhoneNumber? Phone, string? FirstName, string? Language, bool IsAnonymized);

public interface INotificationContacts
{
    /// <summary>Null pour un client inconnu ou d'un autre tenant.</summary>
    Task<CustomerContact?> FindCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);
}

/// <summary>Le consentement marketing d'un client, lu dans Customers par l'hôte (juste avant la création et avant chaque envoi).</summary>
public interface INotificationConsents
{
    /// <summary>Null si le client est inconnu de ce tenant.</summary>
    Task<ConsentProof?> FindAsync(TenantId tenantId, CustomerId customerId, CommunicationChannel channel, CancellationToken cancellationToken = default);
}

/// <summary>L'utilisateur authentifié de la requête (claim <c>sub</c>), fourni par l'Api.</summary>
public interface INotificationsCurrentUser
{
    UserId? UserId { get; }
}

public interface IDeviceRegistrationRepository
{
    Task<DeviceRegistration?> FindAsync(UserId userId, string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Les appareils actifs d'un utilisateur (destinataires d'un push).</summary>
    Task<IReadOnlyList<DeviceRegistration>> ListActiveAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Les enregistrements actifs portant ce jeton (tous utilisateurs) : un jeton refusé ou repris ailleurs.</summary>
    Task<IReadOnlyList<DeviceRegistration>> ListByTokenAsync(string pushToken, CancellationToken cancellationToken = default);

    void Add(DeviceRegistration registration);
}

public interface IOtpChallengeRepository
{
    Task<OtpChallenge?> FindAsync(TenantId? owner, OtpChallengeId id, CancellationToken cancellationToken = default);

    /// <summary>Les codes demandés pour ce destinataire et cet usage depuis <paramref name="since"/>, les plus récents d'abord.</summary>
    Task<IReadOnlyList<OtpChallenge>> ListRecentAsync(
        OtpHash subjectKey, OtpPurpose purpose, DateTimeOffset since, CancellationToken cancellationToken = default);

    void Add(OtpChallenge challenge);
}

/// <summary>
/// Génère et empreinte les codes à usage unique (HMAC avec une clé de l'hôte). Le code en clair n'existe qu'en mémoire,
/// le temps d'être envoyé ; il n'est jamais stocké, journalisé ni renvoyé.
/// </summary>
public interface IOtpHasher
{
    /// <summary>Un code de <paramref name="digits"/> chiffres tiré d'un générateur cryptographique.</summary>
    string NewCode(int digits);

    OtpHash HashSubject(NotificationChannel channel, string address);

    OtpHash HashCode(OtpHash subjectKey, OtpPurpose purpose, string code);
}

public interface ICampaignRepository
{
    Task<Campaign?> FindAsync(TenantId tenantId, CampaignId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, les plus récentes d'abord.</summary>
    Task<IReadOnlyList<Campaign>> ListAsync(TenantId tenantId, int take, CancellationToken cancellationToken = default);

    void Add(Campaign campaign);
}

public sealed record CampaignLocator(CampaignId Id, TenantId TenantId);

/// <summary>Lecture TOUS TENANTS pour le worker : les campagnes à faire avancer.</summary>
public interface ICampaignDirectory
{
    Task<IReadOnlyList<CampaignLocator>> ListDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// Audience d'une campagne, fournie par l'hôte depuis Customers : clients actifs du tenant qui ont un consentement
/// marketing ACCORDÉ sur ce canal et l'adresse correspondante, éventuellement filtrés par tag, triés par id.
/// </summary>
public interface ICampaignAudience
{
    /// <param name="customersOf">Seulement les clients de cet établissement (au moins une commande) ; null : tous.</param>
    Task<int> CountAsync(
        TenantId tenantId, CommunicationChannel channel, string? tag, LocationId? customersOf = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerId>> ListAsync(
        TenantId tenantId, CommunicationChannel channel, string? tag, int skip, int take, LocationId? customersOf = null,
        CancellationToken cancellationToken = default);
}

public interface IMarketingRulesRepository
{
    Task<MarketingRules?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(MarketingRules rules);
}

/// <summary>Messages marketing déjà adressés à une personne (règle de fréquence).</summary>
public interface IMarketingHistory
{
    /// <summary>Vrai si un message marketing lui a été créé après <paramref name="since"/> (annulés et échecs exclus).</summary>
    Task<bool> ReceivedSinceAsync(TenantId tenantId, RecipientId recipient, DateTimeOffset since, CancellationToken cancellationToken = default);
}
