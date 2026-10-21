# Module Notifications : point final (09/10/2026)

## État

| Élément | État |
|---|---|
| Étapes du cahier (§45) | 1 à 33 livrées |
| Compilation | OK (4 projets du module + 4 projets de test) |
| Migration `InitialCreate` | Générée le 09/10/2026 : schéma `notifications`, 15 tables, 22 index, 43 contraintes CHECK |
| Tests | Écrits, exécution à confirmer (`dotnet test`) |

## Contenu du module

| Couche | Contenu |
|---|---|
| Domain | Prestataires et capacités, configurations restaurant (canaux, affectations par établissement, référence de secret), templates versionnés et rendu, préférences et consentement, notifications et tentatives, appareils Push, OTP, état in-app |
| Application | Envoi (politique, routage, idempotence, quotas), planification, relances, dead letter, effacement, OTP, préférences, appareils, boîte in-app, requêtes avec adresses masquées, télémétrie |
| Infrastructure | EF Core / PostgreSQL, filtres par tenant, outbox, inbox, coffre AES-GCM, HMAC des OTP, prestataires `IN_APP` et `FAKE`, réception des webhooks, worker |
| Api | Routes restaurant, administration SaaS, `/me`, webhooks |
| Host | Déclencheurs depuis Orders, Payments, Reservations (rappel 3 h avant), ponts Tenants, Locations, Customers |

## Tables

`notification_providers`, `restaurant_notification_configurations`, `notification_channel_assignments`,
`notification_templates`, `notification_preferences`, `notification_preference_choices`, `notifications`,
`notification_deliveries`, `device_registrations`, `otp_challenges`, `notification_secrets`, `inbox_messages`,
`notification_usage`, `provider_webhook_events`, `outbox_messages`.

## Tests

| Projet | Méthodes de test | Docker |
|---|---|---|
| Notifications.Domain.Tests | 252 | Non |
| Notifications.Application.Tests | 32 | Non |
| Notifications.Infrastructure.Tests | 24 (dont 13 sans base) | Oui pour 11 |
| Notifications.Api.Tests | 8 | Oui |

## Écarts assumés

1. Pas de Kafka : outbox et consumers en processus (`NotificationsBridges.cs`).
2. Routes sans préfixe `/api/v1` (convention de la solution).
3. Quotas en configuration en attendant Billing.
4. Push : enregistrement des appareils seulement, aucun prestataire.
5. Aucun prestataire réel (Resend, Twilio, WhatsApp, Firebase, APNs), conformément au cahier.

## Sécurité (détail : `NOTIFICATIONS-SECURITE.md`)

- Secrets chiffrés, lus à l'envoi seulement, jamais renvoyés, journalisés ni publiés.
- OTP jamais stocké (HMAC, clé dédiée), comparaison en temps constant, tentatives et fréquence limitées.
- TenantId issu du contexte authentifié ; LocationId vérifié ; lecture croisée impossible (404).

À FAIRE AVANT LA PRODUCTION :
- fournir `Notifications:CredentialsKey` et `Notifications:OtpKey` par un coffre, jamais par un fichier versionné ;
- laisser `Include Error Detail` désactivé dans la chaîne de connexion ;
- prévoir la rotation de `CredentialsKey` (préfixe `v1:` déjà en place).

## Suites possibles

1. Premier prestataire réel (Resend pour l'e-mail ou Twilio pour le SMS).
2. Prestataire Push (Firebase / APNs).
3. Quotas reliés au module Billing.
4. Fabriques de conception alignées sur le port 5433 du docker-compose.
