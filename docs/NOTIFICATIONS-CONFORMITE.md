# Module Notifications — conformité au cahier des charges

Cahier des charges : `docs/NOTIFICATIONS-CAHIER.md`. État au 09/10/2026, après l'étape 10. Légende : **OK**
conforme ; **PARTIEL** existe mais incomplet ; **MANQUE** absent ; **ÉCART** fait autrement, volontairement (à valider).

## Étapes 1 à 3 livrées : canaux et catalogue des prestataires

| § | Exigence | État | Commentaire |
|---|---|---|---|
| 2 | `NotificationChannel` (Email, Sms, WhatsApp, Push, InApp), sans chaînes magiques, extensible | OK | Enum à valeurs explicites et définitives (1 à 5) ; Email..Push = mêmes valeurs que `CommunicationChannel` du noyau (consentements Customers) ; `NotificationChannels` : `All`, `UsesExternalProvider` (faux pour InApp), `ToConsentChannel` (null pour InApp), `FromCommunicationChannel`, `Require` |
| 3 | `NotificationProvider` : Id, Code, Name, Channel, IsEnabled, ConfigurationType, dates ; `Create`, `Enable`, `Disable`, `UpdateCapabilities` | OK | + `Rename`, `ChangeConfigurationType`, `CanDeliverTo(pays?)`. Agrégat de la plateforme (aucun tenant). Créé NON proposé : la plateforme l'active une fois ses identifiants rangés |
| 3 | Codes `RESEND`, `TWILIO`, `WHATSAPP_META`, `IN_APP`... | OK | `NotificationProviderCode` : majuscules, lettre puis lettres/chiffres/`_`, 2 à 32. Seul code connu du domaine : `IN_APP` |
| 3 | `ConfigurationType` | OK (interprétation) | `Internal` (InApp seulement, et InApp toujours), `PlatformOnly`, `TenantOnly`, `PlatformOrTenant` : quels modes un restaurant pourra choisir (§6, étape 4) |
| 4 | `NotificationProviderCapability`, pas de `if (provider == "RESEND")` | OK | Canal + `NotificationFeature` (Text, Html, Attachments, ProviderTemplates, DeliveryStatus, Image, Document, Title, DeepLink, Topic, DeviceTargeting) + pays (vide = tous) + longueur max. Chaque fonctionnalité n'existe que sur certains canaux ; au moins Text ou Html. BODY (Push) = Text, TEMPLATE(S) = ProviderTemplates, DEVICE = DeviceTargeting |
| 39 | Domain Events | OK (autre nommage) | `NotificationProviderCreated`, `Renamed`, `CapabilitiesUpdated`, `ConfigurationTypeChanged`, `Enabled`, `Disabled` : sans suffixe `DomainEvent` (convention de la solution) ; aucun secret |
| 36 | Arborescence `RestaurantSaaS.Notifications.*` | ÉCART | Projets `Notifications.<Couche>`, comme les 20 autres modules (même choix que Payments) |

Tests : `NotificationChannelTests` (4), `NotificationProviderCapabilityTests` (21 cas), `NotificationProviderTests` (30 cas),
`NotificationProviderGuardTests` (3). Test d'architecture : `NotificationProvider` ajouté aux agrégats de plateforme.

## Étape 4 livrée : configuration par restaurant

| § | Exigence | État | Commentaire |
|---|---|---|---|
| 5 | `RestaurantNotificationConfiguration` : Id, TenantId, NotificationProviderId, Channel, Mode, Status, IsEnabled, SecretReference?, dates | OK | + `Provider` (code recopié), `Assignments`, `IsGlobal`. UNE configuration par (restaurant, prestataire) ; plusieurs prestataires d'un même canal = repli par priorité |
| 5 | `Create`, `Enable`, `Disable`, `Configure`, `ChangeProvider`, `ChangeMode`, `AssignToLocation`, `RemoveFromLocation` | OK | + `CanServe` / `EnsureCanServe(locationId?)`, `PriorityAt`. Créée INACTIVE (prête en mode plateforme, en attente d'identifiants en mode restaurant). `Enable` revérifie que le prestataire est proposé ET que le mode est encore permis par son contrat |
| 5 | `ChangeProvider` | OK (interprétation) | Vers un prestataire du MÊME canal, proposé, que le restaurant n'utilise pas déjà ; établissements gardés ; référence du coffre oubliée (les identifiants d'un prestataire ne valent pas pour un autre) ; désactivée si plus prête |
| 6 | `NotificationProviderMode` PlatformManaged / TenantManaged | OK | Borné par `ConfigurationType` du prestataire (`IsAllowedBy`) ; InApp (`Internal`) : mode plateforme seulement. Quotas : étape 27 |
| 7 | `NotificationCredentialReference`, jamais de secret | OK | `notifications/tenant/{tenantId N}/{code minuscule}` ou `notifications/platform/{code}` ; porte son tenant ET son prestataire : `Configure` refuse une référence d'un autre restaurant (`common.tenant.mismatch`) ou d'un autre prestataire (`credentials_for_another_provider`) ; forme stockée unique (`Parse` refuse majuscules et variantes). `INotificationSecretStore` : Application |
| 8 | `NotificationChannelAssignment` : LocationId, Channel, ProviderId, IsEnabled, Priority ; `LocationId` du même tenant | OK | Entité enfant ; canal et prestataire recopiés de la configuration (suivent `ChangeProvider`) ; priorité 0..1000 ; 500 affectations au plus ; `LocationReference` construit par l'Application (même tenant vérifié par l'agrégat) |
| 39 | `NotificationConfigurationEnabled/DisabledDomainEvent` | OK (autre nommage) | `NotificationConfigurationCreated`, `Configured`, `ModeChanged`, `ProviderChanged`, `Enabled`, `Disabled`, `NotificationChannelAssignedToLocation`, `RemovedFromLocation` ; aucun secret |

Tests : `RestaurantNotificationConfigurationTests` (16 cas), `NotificationCredentialReferenceTests` (11 cas).

## Étape 5 livrée : templates

| § | Exigence | État | Commentaire |
|---|---|---|---|
| 9 | Template par Tenant + NotificationType + Channel + Language ; `Create`, `UpdateContent`, `Enable`, `Disable` | OK / ÉCART validé | VERSIONS GARDÉES (décision du 09/10) : Draft modifiable (`UpdateContent`, ex-`EditDraft`), Published figé, Archived ; `Enable` / `Disable` ajoutés (actif à la création, copié sur la version suivante, impossible une fois archivé) ; `Channel` passe à `NotificationChannel` (InApp) |
| 9 | Subject? | OK | Email : sujet obligatoire ; Push et InApp : titre facultatif (≤ 200) ; Sms et WhatsApp : aucun |
| 9 | Types ORDER_CONFIRMED... | OK / ÉCART | Catalogue `NotificationTypes` (24 types : client, comptes et sécurité, équipe en in-app, marketing). Clés en minuscules pointées (`order.confirmed` pour ORDER_CONFIRMED), format existant de `TemplateKey`. Un template n'existe que pour un type connu (`type_unknown`) |
| 10 | Templates plateforme et résolution restaurant puis plateforme | OK | `NotificationTemplateResolution.Resolve` (règle du domaine, pure) : plus haute version PUBLIÉE du restaurant si active ; désactivée = repli plateforme (une ancienne version n'est jamais ressuscitée) ; sinon plateforme si active ; sinon aucun. `INotificationTemplateResolver` (Application) chargera les candidats et appliquera cette règle (étape 16) |
| 11 | Variables contrôlées, autorisées par type, validées à la création / modification, aucun code exécuté | OK | `{{ nom.pointé }}` seulement (`TemplateVariables`) ; toute autre accolade double refusée (`variable_syntax_invalid`) ; variable hors de la liste du type refusée (`variable_not_allowed`), sujet compris ; 100 variables au plus ; un email MARKETING doit contenir `{{unsubscribe.url}}` |
| 20, 21 | Transactionnel / marketing distingués | OK (catalogue) | Finalité portée par le type ; contrôles d'envoi (consentement, opt-out, fréquence) aux étapes 6 et 17 |
| 31 | `NotificationPriority` Critical / High / Normal / Low | OK (catalogue) | Priorité par défaut du type : OTP et mot de passe Critical, paiement échoué et commande prête High, marketing Low. Utilisée par le worker à l'étape 18 |

Tests : `NotificationTemplateTests` (adaptés + 13 cas), `NotificationTypesTests` (20 cas), `TemplateVariablesTests` (6 cas),
`NotificationTemplateResolutionTests` (7 cas).

## Étape 6 livrée : préférences du destinataire

| § | Exigence | État | Commentaire |
|---|---|---|---|
| 19 | `NotificationPreference` par finalité (transactionnel / marketing) et par canal | OK | Agrégat (tenant) d'UNE personne : `RecipientId` = client (Customers) ou utilisateur (Identity), par id seulement. Tout accepté par défaut, sauf le marketing pour un utilisateur. `OptOut`, `OptIn`, `OptOutOfAllMarketing`, `Allows`. Choix datés avec leur source (`PreferenceSource`, code court) ; rejouer ne change rien (lien de désinscription cliqué deux fois) |
| 19, 21 | Transactionnel n'implique jamais le marketing ; consentement et opt-out | OK | Les deux finalités sont indépendantes. `NotificationPermission.Decide` : essentiel = toujours ; marketing = client seulement, non désinscrit, et (hors in-app) preuve de consentement ACCORDÉE pour CE client et CE canal (`ConsentProof`, Customers) ; une préférence ne remplace jamais le consentement ; transactionnel = non désinscrit |
| 20, 22 | Messages de sécurité | OK (choix) | `IsEssential` sur `account.verify_email`, `account.password_reset`, `security.otp` : envoyés même à une personne désinscrite du canal (sinon elle perdrait l'accès à son compte) ; jamais marketing |
| 21 | Règles de fréquence | MANQUE | Réglage du restaurant et compteur d'envois : policy de l'Application (étape 17) |

Tests : `NotificationPreferenceTests` (14 cas), `NotificationPermissionTests` (7 cas).

## Étape 7 livrée : agrégat Notification

| § | Exigence | État | Commentaire |
|---|---|---|---|
| 12 | Statuts Pending, Scheduled, Processing, Sent, PartiallyDelivered, Delivered, Failed, Cancelled | OK | `Processing` remplace l'ancienne « tentative en cours sous Pending » : une tentative InProgress existe si et seulement si l'état est Processing |
| 12 | TenantId, LocationId, TemplateKey, Channel, ProviderCode, RecipientId, Subject/Content rendus, Priority, ScheduledAtUtc | OK | `LocationReference` exigée (vérifiée du même tenant, `common.tenant.mismatch`) ; Purpose et Priority recopiées du type ; `NotificationContent` = sujet + corps RENDUS (une variable `{{...}}` restante est refusée) ; `TemplateId` facultatif (preuve du modèle utilisé) |
| 12 | Méthodes Create, Schedule, StartProcessing, MarkAsSent, MarkAsPartiallyDelivered, MarkAsDelivered, MarkAsFailed, ScheduleRetry, Cancel | OK | Un rapport de remise arrivé avant l'enregistrement de l'envoi (Processing) est accepté et clôt la tentative ; MarkAsSent / MarkAsDelivered rejoués = sans effet (worker « au moins une fois ») |
| 2, 13 | Destinataire par canal, InApp compris | OK | `NotificationRecipient` (`NotificationChannel`) ; InApp = id de l'utilisateur, et le `RecipientId` est cet utilisateur (`recipient_mismatch` sinon) |
| 3 | IN_APP, prestataire interne | OK | IN_APP si et seulement si canal InApp (`provider_channel_mismatch`) |
| 19, 21 | Marketing : client + consentement accordé, revérifié à chaque tentative | OK | Jamais en plateforme (`platform_marketing`) ni en in-app (le destinataire in-app est un utilisateur, le marketing exige un client) |
| 31 | Rien de sensible dans les événements ni dans `ToString` | OK | Événements : ids, codes, canal, dates. Contenu et adresse effacés par `Redact` ; `NotificationContent.ToString()` ne montre que la longueur |
| 12 | Choix du prestataire, rendu, préférences, fréquence | Application | Étapes 15 à 17 ; l'agrégat reçoit le résultat |

Tests : `NotificationTests` (61 cas), `NotificationValueObjectsTests` (destinataire InApp, `RecipientId`, contenu).

## Étape 8 livrée : NotificationDelivery

| § | Exigence | État | Commentaire |
|---|---|---|---|
| 14 | Entité `NotificationDelivery` : Id, ProviderCode, ProviderMessageId?, Status, AttemptNumber, StartedAtUtc, CompletedAtUtc?, FailureCode?, NextRetryAtUtc? | OK | Ajouts : `DeliveredAtUtc`, `Failure` (code + nature + message brut effaçable). Remplace `NotificationAttempt` |
| 14 | Méthodes Create, MarkAsSent, MarkAsDelivered, MarkAsFailed, ScheduleRetry | OK | `internal`, appelées par `Notification` seule (cohérence statut notification / dernière tentative) |
| 25 | Distinguer TemporaryFailure et PermanentFailure ; ne pas relancer une erreur permanente | OK | `NotificationFailureKind` ; `ScheduleRetry` refuse un échec permanent. Backoff configurable : `NotificationRetryPolicy` (existant) |
| 26 | Dead letter | PARTIEL | `IsDeliveryAbandoned` + `NotificationDeliveryAbandoned` (échec permanent ou tentatives épuisées) ; inspection, retry manuel et abandon : étape 19 |
| 29 | Retrouver la tentative par `ProviderMessageId` et appliquer la transition | OK (domaine) | Rapports avec id vérifié ; rebond après acceptation (Sent -> Failed). Endpoint et signature : étape 23 |
| 16 | Prestataire de secours | OK (domaine) | `StartProcessing(..., provider)` ; le choix reste au resolver (étape 14) |

Tests : `NotificationTests` (68 cas), `NotificationValueObjectsTests` (`NotificationFailure`, `ProviderMessageId`).

## Étapes 9 et 10 livrées : tests Domain, NotificationsDbContext

| § | Exigence | État | Commentaire |
|---|---|---|---|
| 42 | Tests Domain | OK | Couverts au fil des étapes 1 à 8 (agrégats, value objects, transitions, sécurité des événements) |
| 37 | `NotificationsDbContext`, schéma `notifications` | OK | DbSets : prestataires, configurations, templates, préférences, notifications, outbox ; historique des migrations dans le schéma ; fabrique de conception (`NOTIFICATIONS_DB`) |
| 38 | Isolation par tenant à la lecture | OK (filtres) | `ITenantOwned` : filtre du kernel. Notifications : celles du tenant ; sans tenant, la plateforme seule. Templates : ceux du tenant + ceux de la plateforme. Prestataires : catalogue sans filtre. Testé sur PostgreSQL à l'étape 11 |
| 37 | Tables, `tenant_id` sur les enfants, contraintes, index | OK | Étape 11 (configurations EF Core) ; migration `InitialCreate` à générer sur le poste |
| 28 | Outbox | OK | Dispatcher et migration au démarrage branchés à l'étape 21 |

Tests : `NotificationsDbContextTests` (6 cas, sans base de données).

## Écarts du domaine existant, à traiter aux étapes suivantes

Le domaine construit plus tôt (`Notification`, `NotificationAttempt`, `NotificationTemplate`, `Recipient`, `ConsentProof`,
`NotificationRetryPolicy`) précède ce cahier des charges :

| § | Écart | Étape prévue |
|---|---|---|
| 2, 13 | `Recipient` -> `NotificationRecipient` (InApp compris) | Fait (étape 7) |
| 12 | Statuts, `ProviderCode`, `LocationId`, contenu rendu | Fait (étape 7) |
| 12 | `NotificationPayload` n'est plus utilisé par `Notification` ; ses noms de variables (sans point) ne suivent pas `TemplateVariables` | Fait (étape 16) : clés pointées, rendu par `TemplateRenderer` |
| 14 | `NotificationAttempt` -> `NotificationDelivery` | Fait (étape 8) |
| 25 | Erreur temporaire / permanente | Fait (étape 8) ; politique de relance par l'Application : 19 |
| 9-11 | Template versionné, gardé (décision) ; Enable/Disable, variables autorisées et résolution ajoutés | Fait (étape 5) |
| 23, 24, 22 | Appareils, in-app, OTP | Fait (étapes 24 à 26) |

## Étapes 11 à 33 livrées

| Étape | § | Exigence | État | Où |
|---|---|---|---|---|
| 11 | 37 | Configurations EF Core | OK | `Persistence/Configurations/*` : tables, CHECK générés depuis les enums, `tenant_id` sur chaque enfant, index uniques (NULLS NOT DISTINCT pour les templates), `xmin` en version de ligne |
| 12 | 37 | Repositories | OK | `NotificationsPersistence.cs` ; `NotificationDirectory` (tous tenants) réservé aux workers |
| 13 | 18 | `INotificationSender` | OK | `Application/Sending/Senders.cs` ; résultat Accepted / Delivered / Failed (temporaire ou permanent) |
| 14 | 19 | `INotificationSenderResolver` | OK | `NotificationSenderResolver` : fabriques par code prestataire, identifiants lus au coffre au moment de l'envoi |
| 15 | 20 | `FakeNotificationSender` | OK | `FakeSenderFactory` (code `FAKE`), activé en développement seulement ; `FakeNotificationOutbox` pour les tests |
| 16 | 9-11 | Résolution des templates | OK | Tenant puis plateforme, langue demandée puis langue par défaut ; `TemplateRenderer` (variable manquante refusée) |
| 17 | 15 | Use case SendNotification | OK | Politique (`NotificationPolicy`) : tenant actif, canal configuré, préférences, consentement marketing, quotas, idempotence |
| 18 | 16 | Planification | OK | `ScheduledAtUtc` ; le worker n'envoie que les notifications échues |
| 19 | 25 | Relances | OK | `NotificationRetryPolicy` ; tentatives bloquées récupérées après `ProcessingTimeout` ; dead letter : relance / abandon manuels (admin) |
| 20 | 29 | Inbox | OK | Table `inbox_messages` (clé : consommateur + message) ; un événement rejoué ne crée rien |
| 21 | 28 | Outbox | OK | Dispatcher de la solution ; événements de domaine sans contenu ni secret |
| 22 | 30 | Consumers Kafka | ÉCART | Pas de Kafka dans la solution : `IOutboxConsumer` en processus (`NotificationsBridges.cs`) pour commandes, paiements, réservations. Le contrat `NotificationTrigger` reste transposable à un consumer Kafka |
| 23 | 26 | Webhooks | OK | `/notifications/webhooks/{provider}[/{configurationId}]` : signature vérifiée par le lecteur du prestataire (401 sinon), événement dédupliqué, corps brut non conservé, appliqué par le worker |
| 24 | 22 | OTP | OK | Code jamais stocké (HMAC avec clé dédiée), comparaison en temps constant, durée de vie, tentatives, délai entre deux envois, plafond horaire ; contenu effacé avant la sauvegarde |
| 25 | 23 | Push | PARTIEL | Enregistrement des appareils (jeton masqué dans les réponses et absent des événements) ; aucun prestataire Push implémenté (Firebase / APNs exclus par le cahier) |
| 26 | 24 | In-App | OK | Prestataire `IN_APP` intégré ; non lu / lu / archivé ; `/me/notifications` |
| 27 | 31 | Quotas | PARTIEL | Compteur mensuel par tenant et canal (messages de sécurité exemptés) (`notification_usage`) ; limites par défaut en configuration en attendant le module Billing |
| 28 | 33 | Endpoints administrateur SaaS | OK | `/admin/notification-providers`, `/admin/notification-templates`, `/admin/notification-dead-letters` |
| 29 | 34 | Endpoints restaurant | OK | `/notification-configurations`, `/notification-templates`, `/notifications`, `/me/...` |
| 30 | 42 | Tests d'intégration | OK | `Notifications.Infrastructure.Tests` (PostgreSQL Testcontainers), `Notifications.Api.Tests` |
| 31 | 38 | Tests multi-tenant | OK | Lecture croisée impossible (404), secret d'un autre tenant refusé, TenantId jamais lu dans le corps |
| 32 | 40 | Observabilité | OK | `ActivitySource` et `Meter` « SaasRestaurant.Notifications » : ids, canal, code prestataire, statut ; jamais de destinataire ni de contenu |
| 33 | 39 | Audit sécurité | OK | `docs/NOTIFICATIONS-SECURITE.md` |

Écarts assumés : routes sans préfixe `/api/v1` (convention de la solution) ; Kafka remplacé par l'outbox ; quotas en
configuration tant que Billing n'existe pas ; aucun prestataire réel (Resend, Twilio, WhatsApp, Firebase, APNs).
