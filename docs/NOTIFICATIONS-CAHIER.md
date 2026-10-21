# Module Notifications — cahier des charges (texte fourni le 09/10/2026)

# Mission

Tu es un **Software Architect / Tech Lead senior .NET spécialisé en DDD, Clean Architecture, architectures événementielles et systèmes SaaS multi-tenant**.

Tu dois concevoir et implémenter le module `Notifications` d'un SaaS de gestion de restaurants en **C#/.NET**, utilisant une architecture **monolithe modulaire**.

Le module doit permettre :

- à la plateforme SaaS de configurer les providers disponibles ;
- à chaque restaurant d'activer les canaux qu'il souhaite ;
- à chaque restaurant de configurer ses propres providers lorsque nécessaire ;
- de fournir des providers gérés directement par la plateforme ;
- de personnaliser les templates ;
- d'envoyer des emails ;
- d'envoyer des SMS ;
- d'envoyer des messages WhatsApp ;
- d'envoyer des notifications Push ;
- de gérer les notifications In-App ;
- de traiter les notifications de manière asynchrone ;
- de recevoir des événements provenant des autres modules ;
- de gérer les retries ;
- de gérer les erreurs ;
- de suivre les statuts d'envoi ;
- de gérer les préférences des destinataires ;
- de garantir l'isolation multi-tenant.

Le module doit être conçu pour permettre l'ajout futur d'un provider sans modifier le cœur métier.

---

# 1. Principe général

Séparer clairement :

1. `NotificationChannel`
2. `NotificationProvider`
3. `RestaurantNotificationConfiguration`
4. `NotificationTemplate`
5. `Notification`
6. `NotificationDelivery`
7. `NotificationPreference`

Exemple :

```text
PLATEFORME SaaS
       │
       ├── EMAIL
       │    ├── Resend
       │    ├── SendGrid
       │    └── SMTP
       │
       ├── SMS
       │    ├── Twilio
       │    ├── fournisseur local
       │    └── autres
       │
       ├── WHATSAPP
       │    └── WhatsApp Business
       │
       ├── PUSH
       │    ├── Firebase
       │    └── APNs
       │
       └── IN_APP
              │
              ▼
       Providers supportés
              │
              ▼
         RESTAURANT
              │
       ┌──────┼────────┐
       ▼      ▼        ▼
     Email   SMS    WhatsApp
```

La plateforme définit ce qui est techniquement supporté.

Le restaurant choisit ce qu'il souhaite utiliser.

---

# 2. NotificationChannel

Créer :

`NotificationChannel`

Valeurs minimales :

```text
Email
Sms
WhatsApp
Push
InApp
```

Ne jamais utiliser des chaînes magiques dispersées dans le code.

Prévoir l'extension future vers d'autres canaux.

---

# 3. NotificationProvider

Créer l'Aggregate Root :

`NotificationProvider`

Propriétés minimales :

- `NotificationProviderId`
- `Code`
- `Name`
- `Channel`
- `IsEnabled`
- `ConfigurationType`
- `CreatedAtUtc`
- `UpdatedAtUtc`

Exemples de codes :

```text
RESEND
SENDGRID
SMTP
TWILIO
WHATSAPP_META
FIREBASE
APNS
IN_APP
```

La plateforme SaaS contrôle cette liste.

Méthodes :

- `Create()`
- `Enable()`
- `Disable()`
- `UpdateCapabilities()`

Un restaurant ne doit pas pouvoir créer arbitrairement un provider technique.

---

# 4. Provider Capabilities

Créer :

`NotificationProviderCapability`

Il doit permettre de définir ce que supporte chaque provider.

Exemples :

```text
RESEND

Channel:
EMAIL

Capabilities:
HTML
TEXT
ATTACHMENTS
TEMPLATES
DELIVERY_STATUS
```

ou :

```text
WHATSAPP_META

Channel:
WHATSAPP

Capabilities:
TEXT
TEMPLATE
IMAGE
DOCUMENT
DELIVERY_STATUS
```

ou :

```text
FIREBASE

Channel:
PUSH

Capabilities:
TITLE
BODY
IMAGE
DEEPLINK
TOPIC
DEVICE
```

Ne jamais créer partout :

```csharp
if (provider == "RESEND")
```

ou :

```csharp
if (provider == "TWILIO")
```

Les comportements spécifiques doivent être encapsulés dans Infrastructure.

---

# 5. Configuration par restaurant

Créer l'Aggregate Root :

`RestaurantNotificationConfiguration`

Propriétés minimales :

- `RestaurantNotificationConfigurationId`
- `TenantId`
- `NotificationProviderId`
- `Channel`
- `NotificationProviderMode`
- `Status`
- `IsEnabled`
- `SecretReference?`
- `CreatedAtUtc`
- `UpdatedAtUtc`

Méthodes :

- `Create()`
- `Enable()`
- `Disable()`
- `Configure()`
- `ChangeProvider()`
- `ChangeMode()`
- `AssignToLocation()`
- `RemoveFromLocation()`

Garantir l'isolation :

```text
Tenant A
   X
Tenant B
```

Un tenant ne doit jamais pouvoir accéder aux configurations d'un autre tenant.

---

# 6. PlatformManaged et TenantManaged

Créer :

`NotificationProviderMode`

avec :

```text
PlatformManaged
TenantManaged
```

## PlatformManaged

La plateforme possède les credentials du provider.

Exemple :

```text
Restaurant
    │
    ▼
SaaS Restaurant
    │
    ▼
Resend plateforme
```

Plusieurs restaurants peuvent utiliser l'infrastructure commune.

Prévoir quotas et limitations.

---

## TenantManaged

Le restaurant utilise son propre compte.

Exemple :

```text
Restaurant A
    │
    ▼
Compte Resend A
```

Restaurant B :

```text
Restaurant B
    │
    ▼
Compte SendGrid B
```

Le SaaS orchestre l'envoi mais utilise les credentials du tenant.

---

# 7. Secrets

INTERDICTION de stocker directement :

- API Key ;
- SMTP Password ;
- WhatsApp Access Token ;
- Firebase Private Key ;
- provider secret.

Créer :

`INotificationSecretStore`

et :

`NotificationCredentialReference`

Exemple :

```text
notifications/tenant/{tenantId}/resend
```

PostgreSQL conserve uniquement :

```text
secret_reference
```

Les secrets réels doivent pouvoir être stockés dans :

- Vault ;
- Secret Manager ;
- solution équivalente.

Ils ne doivent jamais apparaître dans :

- logs ;
- exceptions ;
- Kafka ;
- Domain Events ;
- Integration Events ;
- réponses API.

---

# 8. Configuration par établissement

Un tenant peut avoir plusieurs établissements.

Exemple :

```text
Restaurant Group
│
├── Paris
│   ├── Email
│   ├── SMS
│   └── Push
│
├── Lyon
│   ├── Email
│   └── Push
│
└── Cotonou
    ├── Email
    ├── SMS
    ├── WhatsApp
    └── Push
```

Créer si nécessaire :

`NotificationChannelAssignment`

avec :

- `LocationId`
- `Channel`
- `ProviderId`
- `IsEnabled`
- `Priority`

Toujours vérifier que `LocationId` appartient au même `TenantId`.

---

# 9. NotificationTemplate

Créer l'Aggregate Root :

`NotificationTemplate`

Un template appartient à :

```text
Tenant
+
NotificationType
+
Channel
+
Language
```

Exemples de types :

```text
ORDER_CONFIRMED
ORDER_READY
ORDER_CANCELLED

RESERVATION_CONFIRMED
RESERVATION_REMINDER
RESERVATION_CANCELLED

PAYMENT_SUCCEEDED
PAYMENT_FAILED
REFUND_SUCCEEDED

WELCOME
PASSWORD_RESET
OTP

LOYALTY_REWARD_EARNED

PROMOTIONAL_CAMPAIGN
```

Propriétés :

- `NotificationTemplateId`
- `TenantId`
- `Code`
- `Channel`
- `Language`
- `Subject?`
- `Content`
- `IsEnabled`
- `CreatedAtUtc`
- `UpdatedAtUtc`

Méthodes :

- `Create()`
- `UpdateContent()`
- `Enable()`
- `Disable()`

---

# 10. Templates plateforme

Prévoir également des templates par défaut fournis par la plateforme.

Exemple :

```text
Platform Template

ORDER_CONFIRMED
       │
       ▼
Restaurant sans template personnalisé
       │
       ▼
Utilisation du template plateforme
```

Mais :

```text
Restaurant possède son template
       │
       ▼
Template Restaurant prioritaire
```

Résolution :

```text
Restaurant Template
        │
        │ existe ?
        ▼
       OUI
        │
        ▼
     utiliser
```

Sinon :

```text
Platform Default Template
```

Créer :

`INotificationTemplateResolver`

---

# 11. Variables de template

Supporter des variables contrôlées.

Exemple :

```text
Bonjour {{customer.firstName}},

Votre réservation du {{reservation.date}}
à {{reservation.time}} est confirmée.

Restaurant : {{restaurant.name}}
```

Ne pas permettre l'exécution de code arbitraire.

Créer un système de variables autorisées par type de notification.

Exemple :

```text
RESERVATION_CONFIRMED

customer.firstName
restaurant.name
reservation.date
reservation.time
reservation.guestCount
```

Valider les variables à la création/modification du template.

---

# 12. Notification Aggregate

Créer :

`Notification`

comme Aggregate Root.

Propriétés :

- `NotificationId`
- `TenantId`
- `LocationId?`
- `NotificationType`
- `RecipientId?`
- `Channel`
- `ProviderCode`
- `Status`
- `Subject?`
- `Content`
- `ScheduledAtUtc?`
- `CreatedAtUtc`
- `SentAtUtc?`

Statuts minimum :

```text
Pending
Scheduled
Processing
Sent
PartiallyDelivered
Delivered
Failed
Cancelled
```

Méthodes :

- `Create()`
- `Schedule()`
- `StartProcessing()`
- `MarkAsSent()`
- `MarkAsDelivered()`
- `MarkAsFailed()`
- `Cancel()`

Les transitions d'état doivent être protégées.

---

# 13. Recipient

Ne pas supposer que toutes les notifications sont destinées à un `Customer`.

Une notification peut concerner :

- client ;
- employé ;
- manager ;
- propriétaire du restaurant ;
- administrateur SaaS.

Créer un Value Object approprié :

`NotificationRecipient`

Il peut contenir selon le canal :

- Email ;
- PhoneNumber ;
- UserId ;
- DeviceToken.

Éviter de mettre toutes ces informations directement dans `Notification`.

---

# 14. NotificationDelivery

Créer l'Entity :

`NotificationDelivery`

Elle représente une tentative réelle auprès d'un provider.

Propriétés :

- `NotificationDeliveryId`
- `ProviderCode`
- `ProviderMessageId?`
- `Status`
- `AttemptNumber`
- `StartedAtUtc`
- `CompletedAtUtc?`
- `FailureCode?`
- `NextRetryAtUtc?`

Méthodes :

- `Create()`
- `MarkAsSent()`
- `MarkAsDelivered()`
- `MarkAsFailed()`
- `ScheduleRetry()`

---

# 15. Abstraction des providers

Application ne doit connaître aucun SDK concret.

Créer :

`INotificationSender`

Exemple conceptuel :

```csharp
public interface INotificationSender
{
    NotificationProviderCode Provider { get; }

    NotificationChannel Channel { get; }

    Task<NotificationSendResult> SendAsync(
        NotificationSendRequest request,
        CancellationToken cancellationToken);
}
```

Infrastructure fournit :

```text
INotificationSender
       ▲
       │
 ┌─────┼─────────┬────────────┬─────────┐
 ▼     ▼         ▼            ▼         ▼
Resend Twilio WhatsApp     Firebase   SMTP
```

---

# 16. Provider Resolver

Créer :

`INotificationSenderResolver`

Responsabilité :

```text
Channel
+
Provider
+
Tenant Configuration
        │
        ▼
INotificationSender
```

Exemple :

```text
EMAIL
+
RESEND
        │
        ▼
ResendNotificationSender
```

Ne pas utiliser un énorme `switch`.

---

# 17. Événements provenant des autres modules

Le module Notifications doit être découplé des autres modules.

INTERDICTION :

```text
Notifications.Domain
        ↓
Orders.Domain
```

ou :

```text
Notifications.Domain
        ↓
Reservations.Domain
```

Utiliser des Integration Events.

Exemples :

```text
Orders
   │
   ▼
OrderPlacedIntegrationEvent
   │
   ▼
Kafka
   │
   ▼
Notifications
```

Même principe :

```text
Reservations
       │
ReservationConfirmed
       │
       ▼
Notifications
```

```text
Payments
       │
PaymentCaptured
       │
       ▼
Notifications
```

Le module Notifications traduit ensuite ces événements en intentions de notification.

---

# 18. Notification Policies

Ne pas envoyer automatiquement une notification uniquement parce qu'un événement existe.

Créer une couche de Policy/Application appropriée.

Exemple :

```text
OrderPlaced
     │
     ▼
NotificationPolicy
     │
     ├── Email activé ?
     ├── SMS activé ?
     ├── préférence client ?
     ├── template disponible ?
     ├── provider disponible ?
     └── consentement requis ?
```

Puis seulement :

```text
Create Notification
```

---

# 19. Préférences utilisateur

Créer :

`NotificationPreference`

Permettre par exemple :

```text
Client A

Transactional Email      ON
Transactional SMS        ON

Marketing Email          ON
Marketing SMS            OFF
Marketing WhatsApp       OFF

Push Promotions          ON
```

Distinguer impérativement :

```text
Transactional
```

et :

```text
Marketing
```

Ne jamais considérer qu'une autorisation transactionnelle implique automatiquement une autorisation marketing.

Prévoir les mécanismes de consentement et d'opt-out appropriés.

---

# 20. Notifications transactionnelles

Exemples :

```text
ORDER_CONFIRMED
ORDER_READY
ORDER_CANCELLED

RESERVATION_CONFIRMED
RESERVATION_REMINDER

PAYMENT_SUCCEEDED
PAYMENT_FAILED

PASSWORD_RESET
OTP
```

Elles doivent être distinguées des campagnes marketing.

---

# 21. Notifications marketing

Exemples :

```text
PROMOTION
NEW_MENU
LOYALTY_CAMPAIGN
CUSTOMER_REACTIVATION
SPECIAL_EVENT
```

Avant envoi :

- vérifier consentement ;
- vérifier canal autorisé ;
- vérifier opt-out ;
- vérifier tenant ;
- appliquer les règles de fréquence si configurées.

---

# 22. OTP

Prévoir un cas spécifique pour :

`OTP`

Un OTP ne doit jamais être enregistré en clair.

Le système doit pouvoir :

1. générérer un OTP sécurisé ;
2. stocker uniquement son hash lorsque nécessaire ;
3. définir une expiration ;
4. limiter les tentatives ;
5. limiter les demandes successives ;
6. empêcher la réutilisation ;
7. envoyer via SMS, Email ou WhatsApp selon configuration.

Ne jamais mettre l'OTP dans les logs.

---

# 23. Push Notifications

Supporter :

```text
Firebase Cloud Messaging
APNs
```

Créer une abstraction de device :

`DeviceRegistration`

avec :

- `DeviceId`
- `UserId`
- `TenantId`
- `Platform`
- `PushToken`
- `IsActive`
- `LastSeenAtUtc`

Prévoir :

```text
Android
iOS
Web
```

Les tokens invalides doivent pouvoir être désactivés.

---

# 24. In-App Notifications

Créer la possibilité d'afficher des notifications directement dans le dashboard/application.

Exemple :

```text
Nouvelle réservation
Commande annulée
Paiement échoué
Stock faible
Nouvel avis client
```

Prévoir :

`InAppNotification`

ou une représentation adaptée.

Statuts :

```text
Unread
Read
Archived
```

Méthodes :

- `MarkAsRead()`
- `Archive()`

---

# 25. Retries

Les erreurs temporaires ne doivent pas entraîner immédiatement un échec définitif.

Implémenter :

```text
Tentative 1
   ↓
échec temporaire
   ↓
Retry
   ↓
Tentative 2
   ↓
Retry
   ↓
Tentative 3
```

Utiliser une stratégie de :

`Exponential Backoff`

Exemple :

```text
1 min
5 min
15 min
1 h
```

La configuration exacte doit rester configurable.

Distinguer :

```text
TemporaryFailure
PermanentFailure
```

Exemple permanent :

```text
INVALID_PHONE_NUMBER
```

Ne pas retry indéfiniment une erreur permanente.

---

# 26. Dead Letter

Après le nombre maximal de tentatives :

```text
Notification
      │
      ▼
MaxRetriesReached
      │
      ▼
Dead Letter
```

Créer un mécanisme permettant :

- inspection ;
- diagnostic ;
- retry manuel ;
- abandon définitif.

---

# 27. Idempotence

Le même Integration Event peut être reçu plusieurs fois.

Exemple :

```text
OrderPlaced
OrderPlaced
OrderPlaced
```

Cela ne doit pas envoyer trois emails.

Créer :

`ProcessedIntegrationEvent`

ou mécanisme Inbox.

Utiliser :

```text
EventId
+
Consumer
```

comme protection d'idempotence.

Mettre en place le pattern :

```text
Kafka
  │
  ▼
Inbox
  │
  ▼
Handler
  │
  ▼
Notification
```

---

# 28. Inbox / Outbox

Utiliser :

`Transactional Inbox`

pour les événements entrants.

Utiliser :

`Transactional Outbox`

si Notifications doit publier des événements.

Exemple :

```text
Kafka
   │
   ▼
Inbox
   │
   ▼
Notification créée
   │
   ▼
PostgreSQL
```

Puis traitement asynchrone :

```text
Pending Notification
       │
       ▼
Background Worker
       │
       ▼
Provider
```

---

# 29. Webhooks providers

Certains providers peuvent envoyer :

```text
Delivered
Opened
Clicked
Bounced
Failed
Unsubscribed
```

Créer une architecture générique :

```text
/api/v1/notifications/webhooks/{provider}
```

Chaque webhook doit :

1. vérifier l'origine/signature ;
2. vérifier l'idempotence ;
3. récupérer `ProviderMessageId` ;
4. retrouver `NotificationDelivery` ;
5. appliquer la transition ;
6. persister ;
7. éventuellement publier un événement.

---

# 30. Scheduling

Supporter les notifications programmées.

Exemple :

```text
Réservation
12/10 à 20h

Reminder
12/10 à 17h
```

Créer :

`ScheduledAtUtc`

Un worker récupère uniquement les notifications arrivées à échéance.

Toujours travailler en UTC dans le backend.

La timezone du restaurant est utilisée uniquement pour calculer le moment UTC approprié.

---

# 31. Priorités

Prévoir éventuellement :

`NotificationPriority`

avec :

```text
Critical
High
Normal
Low
```

Exemple :

```text
OTP              Critical
PaymentFailed    High
OrderReady       High
Reservation      Normal
Marketing        Low
```

Une campagne marketing ne doit pas bloquer l'envoi d'un OTP.

---

# 32. Quotas

Prévoir les quotas par tenant.

Exemple :

```text
Starter

Email: 2 000 / mois
SMS: 100 / mois
WhatsApp: 100 / mois
```

```text
Pro

Email: 20 000 / mois
SMS: 1 000 / mois
WhatsApp: 2 000 / mois
```

Ne pas mettre les plans SaaS directement dans `Notifications.Domain`.

Utiliser un contrat avec le module `Billing/Entitlements`.

Créer une abstraction telle que :

`INotificationEntitlementChecker`

---

# 33. Usage

Enregistrer la consommation.

Exemple :

```text
Tenant
Channel
Provider
Count
Period
```

Ces informations pourront être utilisées par :

- Billing ;
- Reporting ;
- administration SaaS.

---

# 34. Endpoints administrateur SaaS

Prévoir :

```text
GET    /api/v1/admin/notification-providers

POST   /api/v1/admin/notification-providers

PUT    /api/v1/admin/notification-providers/{id}

POST   /api/v1/admin/notification-providers/{id}/enable

POST   /api/v1/admin/notification-providers/{id}/disable
```

Permettre également la gestion des capabilities et configurations plateforme.

---

# 35. Endpoints restaurant

Prévoir notamment :

```text
GET /api/v1/notification-configurations

POST /api/v1/notification-configurations/{providerId}/configure

POST /api/v1/notification-configurations/{id}/enable

POST /api/v1/notification-configurations/{id}/disable
```

Templates :

```text
GET    /api/v1/notification-templates

POST   /api/v1/notification-templates

PUT    /api/v1/notification-templates/{id}

DELETE /api/v1/notification-templates/{id}
```

Historique :

```text
GET /api/v1/notifications

GET /api/v1/notifications/{id}
```

Le `TenantId` doit toujours provenir du contexte authentifié.

---

# 36. Structure du module

Utiliser :

```text
Notifications/
│
├── RestaurantSaaS.Notifications.Domain/
│   │
│   ├── Notifications/
│   │   ├── Notification.cs
│   │   └── NotificationDelivery.cs
│   │
│   ├── Providers/
│   │   ├── NotificationProvider.cs
│   │   └── NotificationProviderCapability.cs
│   │
│   ├── Configurations/
│   │   ├── RestaurantNotificationConfiguration.cs
│   │   └── NotificationChannelAssignment.cs
│   │
│   ├── Templates/
│   │   └── NotificationTemplate.cs
│   │
│   ├── Preferences/
│   │   └── NotificationPreference.cs
│   │
│   ├── ValueObjects/
│   ├── Enums/
│   ├── Events/
│   ├── Errors/
│   └── Policies/
│
├── RestaurantSaaS.Notifications.Application/
│   │
│   ├── Abstractions/
│   │   ├── INotificationSender.cs
│   │   ├── INotificationSenderResolver.cs
│   │   ├── INotificationSecretStore.cs
│   │   └── INotificationTemplateResolver.cs
│   │
│   ├── Notifications/
│   │   ├── SendNotification/
│   │   ├── ScheduleNotification/
│   │   └── RetryNotification/
│   │
│   ├── Configurations/
│   ├── Templates/
│   ├── Preferences/
│   ├── EventHandlers/
│   └── Webhooks/
│
├── RestaurantSaaS.Notifications.Infrastructure/
│   │
│   ├── Persistence/
│   │   ├── NotificationsDbContext.cs
│   │   ├── Configurations/
│   │   ├── Repositories/
│   │   └── Migrations/
│   │
│   ├── Providers/
│   │   ├── Email/
│   │   │   ├── Resend/
│   │   │   ├── SendGrid/
│   │   │   └── Smtp/
│   │   │
│   │   ├── Sms/
│   │   │   └── Twilio/
│   │   │
│   │   ├── WhatsApp/
│   │   │   └── Meta/
│   │   │
│   │   └── Push/
│   │       ├── Firebase/
│   │       └── Apns/
│   │
│   ├── Secrets/
│   ├── Messaging/
│   │   ├── Inbox/
│   │   └── Outbox/
│   │
│   ├── Workers/
│   └── Webhooks/
│
├── RestaurantSaaS.Notifications.Contracts/
│   └── Events/
│
└── RestaurantSaaS.Notifications.Api/
    ├── Endpoints/
    │   ├── Configurations/
    │   ├── Templates/
    │   ├── Notifications/
    │   └── Webhooks/
    │
    └── DependencyInjection.cs
```

---

# 37. PostgreSQL

Utiliser :

`NotificationsDbContext`

Schema :

```text
notifications
```

Tables minimales :

```text
notification_providers

notification_provider_capabilities

restaurant_notification_configurations

notification_channel_assignments

notification_templates

notification_preferences

notifications

notification_deliveries

device_registrations

provider_webhook_events

processed_integration_events

inbox_messages

outbox_messages
```

Ajouter `tenant_id` sur toutes les données appartenant à un restaurant.

Créer les contraintes et index nécessaires.

---

# 38. Multi-tenancy

Le client HTTP ne doit pas pouvoir choisir librement :

```json
{
    "tenantId": "tenant-concurrent"
}
```

Le `TenantId` doit provenir de :

```text
JWT
 │
 ▼
TenantResolutionMiddleware
 │
 ▼
ITenantContext
 │
 ▼
Notifications.Application
```

Chaque requête repository doit être protégée par Tenant.

---

# 39. Domain Events

Créer selon les besoins :

```text
NotificationCreatedDomainEvent

NotificationScheduledDomainEvent

NotificationSentDomainEvent

NotificationDeliveredDomainEvent

NotificationFailedDomainEvent

NotificationCancelledDomainEvent

NotificationConfigurationEnabledDomainEvent

NotificationConfigurationDisabledDomainEvent
```

Les événements doivent représenter des faits passés.

---

# 40. Integration Events sortants

Créer uniquement lorsque d'autres modules ont réellement besoin d'être informés.

Exemples :

```text
NotificationDeliveredIntegrationEvent

NotificationFailedIntegrationEvent
```

Ne pas publier inutilement chaque détail interne.

---

# 41. Observabilité

Chaque envoi doit être traçable avec :

```text
CorrelationId
TenantId
NotificationId
DeliveryId
Provider
Channel
NotificationType
```

Mais ne jamais logger :

- OTP ;
- API Key ;
- Access Token ;
- contenu sensible ;
- credentials ;
- données personnelles inutiles.

Créer des métriques :

```text
notifications.sent

notifications.failed

notifications.delivered

notifications.retry

notifications.duration

notifications.provider.errors
```

Tags :

```text
provider
channel
status
```

Éviter d'utiliser `TenantId` comme label de métrique si cela provoque une cardinalité excessive.

---

# 42. Tests obligatoires

Écrire notamment :

- Tenant A ne peut pas consulter les notifications de Tenant B ;
- Tenant A ne peut pas modifier les templates de Tenant B ;
- provider désactivé par la plateforme inutilisable ;
- provider désactivé par le restaurant inutilisable ;
- canal désactivé non utilisé ;
- template restaurant prioritaire sur template plateforme ;
- fallback vers template plateforme ;
- variable template inconnue rejetée ;
- notification avec adresse invalide rejetée ;
- notification programmée non envoyée avant l'heure ;
- notification programmée envoyée après échéance ;
- retry sur erreur temporaire ;
- aucun retry sur erreur permanente ;
- nombre maximal de retries respecté ;
- Integration Event dupliqué ne crée pas deux notifications ;
- webhook dupliqué idempotent ;
- secret jamais retourné ;
- OTP jamais loggé ;
- OTP expiré rejeté ;
- OTP déjà utilisé rejeté ;
- préférence marketing respectée ;
- opt-out respecté ;
- quota respecté ;
- Location d'un autre tenant rejetée ;
- ajout d'un nouveau provider sans modification du Domain.

---

# 43. Contraintes architecturales

Respecter :

```text
Notifications.Domain
        ↑
Notifications.Application
        ↑
Notifications.Infrastructure
```

API utilise Application.

Infrastructure implémente les abstractions définies par Application.

INTERDICTION :

```text
Notifications.Domain
        ↓
Resend SDK
```

INTERDICTION :

```text
Notifications.Domain
        ↓
Twilio SDK
```

INTERDICTION :

```text
Notifications.Domain
        ↓
Orders.Domain
```

INTERDICTION :

```text
Notifications.Domain
        ↓
Payments.Domain
```

La communication inter-module passe par des contrats explicites et Integration Events.

---

# 44. Flux cible

Exemple avec une commande :

```text
Orders
  │
  │ OrderPlacedIntegrationEvent
  ▼
Kafka
  │
  ▼
Notifications Inbox
  │
  ▼
OrderPlacedNotificationHandler
  │
  ├── vérification tenant
  ├── préférences
  ├── configuration
  ├── entitlement/quota
  ├── sélection canal
  ├── résolution template
  └── création Notification
          │
          ▼
      PostgreSQL
          │
          ▼
   Notification Worker
          │
          ▼
NotificationSenderResolver
          │
     ┌────┼───────┐
     ▼    ▼       ▼
   Email SMS   WhatsApp
     │    │       │
     ▼    ▼       ▼
 Resend Twilio   Meta
```

Le traitement HTTP d'une commande ne doit donc pas attendre qu'un email ou SMS soit envoyé.

---

# 45. Ordre d'implémentation

Ne génère pas l'intégralité du module immédiatement.

Procède dans cet ordre :

1. `NotificationChannel`.
2. `NotificationProvider`.
3. `NotificationProviderCapability`.
4. `RestaurantNotificationConfiguration`.
5. `NotificationTemplate`.
6. `NotificationPreference`.
7. `Notification`.
8. `NotificationDelivery`.
9. tests Domain.
10. `NotificationsDbContext`.
11. configurations EF Core.
12. repositories.
13. `INotificationSender`.
14. `INotificationSenderResolver`.
15. `FakeNotificationSender`.
16. résolution des templates.
17. use case `SendNotification`.
18. scheduling.
19. retries.
20. Inbox.
21. Outbox.
22. consumers Kafka.
23. webhooks.
24. OTP.
25. Push.
26. In-App.
27. quotas.
28. endpoints administrateur SaaS.
29. endpoints restaurant.
30. tests d'intégration.
31. tests multi-tenant.
32. observabilité.
33. audit sécurité.

Pour chaque étape :

- explique les choix ;
- affiche l'arborescence concernée ;
- génère le code complet ;
- génère les tests unitaires ;
- compile ;
- corrige les erreurs ;
- vérifie les dépendances architecturales ;
- n'ajoute aucune dépendance inutile ;
- attends validation avant de passer à l'étape suivante.

Commence uniquement par :

**Étape 1 à 3 : `NotificationChannel`, `NotificationProvider`, `NotificationProviderCapability`, leurs Value Objects, erreurs métier, Domain Events et tests unitaires.**

Ne génère pas encore les implémentations Resend, Twilio, WhatsApp, Firebase ou APNs.
