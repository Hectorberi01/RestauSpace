# Notifications : audit de sécurité (étape 33)

Chaque règle du cahier (§39) est suivie de l'endroit où elle est appliquée et du test qui la vérifie.
« Revue » signifie : vérifié à la lecture du code, sans test automatique.

## Secrets des prestataires

| Règle | Application | Vérification |
|---|---|---|
| Aucune API Key, mot de passe SMTP, token WhatsApp, clé Firebase stocké en clair | Les agrégats ne portent qu'une `SecretReference` ; la valeur est chiffrée (AES-GCM, clé `Notifications:CredentialsKey`, préfixe `v1:`) dans `notification_secrets` | `NotificationsPersistenceTests` (`Credentials_are_stored_encrypted...`) |
| Secret lu seulement au moment de l'envoi | `NotificationSenderResolver` lit le coffre puis construit l'expéditeur ; rien n'est mis en cache | Revue |
| Jamais dans les réponses API | DTO : `HasCredentials` et noms de champs seulement | `NotificationsEndpointsTests` (corps de réponse sans la valeur du secret) |
| Jamais dans les événements de domaine ni d'intégration | Événements : ids, codes, canal, statut | `NotificationTests`, `NotificationAdditionsTests` (sécurité des événements) |
| Jamais dans les logs ni les exceptions | Worker et bridges : id, opération, code d'erreur ou type d'exception. Messages d'erreur du domaine sans valeur | Revue |
| Jamais dans les traces | Tags : tenant, ids, prestataire, canal, type, statut | Revue (`NotificationsTelemetry`) |
| Une configuration ne référence que les secrets de son tenant | CHECK SQL sur le préfixe de la référence + vérification Application | `NotificationsPersistenceTests`, `NotificationUseCaseTests` |

## OTP

| Règle | Application | Vérification |
|---|---|---|
| Jamais enregistré en clair | Seul un HMAC-SHA256 (clé `Notifications:OtpKey`, distincte de la clé des secrets) est stocké ; `OtpHash.ToString()` renvoie `[otp-hash]` | `NotificationAdditionsTests`, `NotificationUseCaseTests` |
| Jamais dans les logs | Le code n'existe qu'en mémoire le temps du rendu ; le contenu rendu est effacé (`Redact`) avant la sauvegarde | `NotificationUseCaseTests` (contenu effacé) |
| Comparaison résistante au timing | `CryptographicOperations.FixedTimeEquals` | Revue |
| Expiration, tentatives, anti-spam | Durée de vie 10 min, 5 essais, 60 s entre deux envois, 5 par heure (429) | `NotificationUseCaseTests` |
| Mauvais codes comptés même en cas d'erreur | La tentative est enregistrée avant la réponse (code faux, expiré ou épuisé) | `NotificationUseCaseTests` |

## Multi-tenant

| Règle | Application | Vérification |
|---|---|---|
| TenantId toujours issu du contexte authentifié | `ITenantContext.RequireTenant()` ; aucune requête n'a de champ TenantId | `NotificationsEndpointsTests` |
| Lecture croisée impossible | Filtres EF (`ITenantOwned`, notifications et OTP du tenant courant) ; 404 sur l'id d'un autre tenant | `NotificationsPersistenceTests`, `NotificationsEndpointsTests` |
| `LocationId` du même tenant | `INotificationLocations` (bridge Locations) vérifié avant toute création | `NotificationUseCaseTests` |
| Workers multi-tenant | `NotificationDirectory` (sans filtre) ne renvoie que des localisateurs ; chaque traitement s'exécute dans un scope `BackgroundTenant.ActFor(tenant)` | Revue |
| Webhooks | Le tenant est déduit de la configuration visée, jamais du corps ; signature vérifiée avant toute écriture | `NotificationsEndpointsTests` (signature falsifiée : 401) |

## Données personnelles

| Règle | Application | Vérification |
|---|---|---|
| Adresses masquées dans les réponses | `Masking` (e-mail, téléphone, jeton Push) | Revue |
| Contenu effacé après la rétention | `RedactAfter` 90 jours (worker) ; OTP effacés immédiatement | `NotificationUseCaseTests` |
| Corps brut des webhooks non conservé | Seuls l'id d'événement, le message prestataire et le statut sont gardés | Revue |
| Consentement marketing | Notification marketing ignorée sans consentement (`NotificationSkipReason`) | `NotificationUseCaseTests` |

## Autorisations

| Règle | Application | Vérification |
|---|---|---|
| Restaurant : permissions dédiées | `notifications.view`, `.send`, `.configure`, `.manage_preferences` | `NotificationsEndpointsTests` (403) |
| Administration SaaS séparée | Groupe `/admin/...` sous la policy d'administration de la plateforme | `NotificationsEndpointsTests` |
| Prestataire `FAKE` | Activé seulement si `EnableFakeProvider` (développement) | Revue (`Program.cs`) |

## Points restant à surveiller

1. Le worker journalise l'exception complète d'une passe en échec (erreurs d'infrastructure : base, réseau). Npgsql
   ne met pas les valeurs des paramètres dans ses messages tant que `Include Error Detail` reste désactivé : ne pas
   l'activer en production.
2. `CredentialsKey` et `OtpKey` viennent de la configuration : en production, les fournir par un coffre (variables
   d'environnement injectées, Key Vault, etc.), jamais dans un fichier versionné. Les clés de `appsettings.Development.json`
   sont réservées au développement.
3. La rotation de `CredentialsKey` n'est pas outillée (préfixe `v1:` prévu pour une `v2`).
4. Les vrais prestataires (étape ultérieure) devront vérifier leur signature de webhook en temps constant et ne
   jamais remonter le message d'erreur brut du prestataire dans `NotificationFailure`.
