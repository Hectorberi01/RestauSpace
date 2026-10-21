# Audit console restaurateur — pages et fonctionnalités manquantes

Date : 11 octobre 2026. Périmètre : `restaurantos-app` (console du restaurant) comparée aux routes « restaurant » des 22
modules du backend. Méthode : inventaire des pages et des appels API du front, puis des routes de chaque module (avec
leur permission). Les routes d'administration de la plateforme (`/admin/...`) sont hors périmètre.

## 1. Ce qui existe

| Page | Couverture |
|---|---|
| Tableau de bord | KPI du jour, graphiques, commandes et réservations récentes |
| Commandes (+ détail) | tableau en direct, historique, création, lignes, remises manuelles, transitions, annulation, fiche retrait/livraison |
| Réservations | calendrier par salle, liste, plan de salle, création, modification, installation à table, politique |
| Cuisine | tableau des tickets, priorités, postes et routage |
| Livraison & retrait | suivi, livreurs, échecs, historique du jour, réglages et zones |
| Salle & tables | salles, tables, hors service, sessions de table |
| Menu & produits | produits, variantes, options, photos, cartes, catégories, taxes |
| Rapports | 6 vues (synthèse, ventes, commandes, réservations, cuisine, paiements), exports CSV |
| Personnel | membres, invitations, affectations, rôles et permissions |
| Établissements | création, ouverture, fermeture temporaire, réouverture, archivage |
| Mon profil | identité, photo, mot de passe, préférences, appareils, activité |

Sur 22 modules, **10 n'ont aucun écran** côté restaurateur alors que leurs routes existent : Payments, Customers,
Loyalty, Promotions, Reviews, Inventory, Integrations, Notifications (côté restaurant), Billing (côté restaurant), Audit.

## 2. Bloquant (P0) — le parcours de base ne va pas au bout

### 2.1 Encaissement : une commande ne peut pas être terminée
Orders refuse `complete` tant que la commande n'est pas soldée (`orders.order.not_settled`). Or la console n'appelle
**aucune** route de Payments : le bouton « Servie / remise » échoue en réel (le simulateur le masquait).

À faire :
- **Écran d'encaissement** sur la commande : reste à payer, paiement au comptoir (`POST /payments/offline` : espèces,
  carte sur TPE, Mobile Money sur place), paiement partagé (plusieurs règlements), rendu de monnaie, puis clôture.
- **Paiement en ligne** (`POST /payments/online`), relance et annulation d'une tentative (`retry`, `cancel`).
- **Remboursement** (`POST /payments/{id}/refunds`, permission `payments.refund`) depuis le détail de commande.
- **Liste des paiements** (`GET /payments`) : journal du jour, filtres, rapprochement de caisse.
- **Ticket / reçu** imprimable ou envoyé (aucune génération de reçu côté backend aujourd'hui).

### 2.2 Paramètres du restaurant : rien n'est modifiable par le restaurateur
`/tenant` n'expose que `GET`. Nom, branding, fuseau, devise et mode TTC/HT ne se changent que par l'administration de
la plateforme (`/admin/tenants/...`).

À faire :
- **Backend** : routes `/tenant/name`, `/tenant/branding`, `/tenant/time-zone`, `/tenant/price-tax-mode`,
  `/tenant/default-currency` sous une nouvelle permission (par exemple `tenant.configure`) ; devise et mode de prix restent bloqués dès qu'un prix
  existe, règle déjà dans le domaine.
- **Page Paramètres** : identité (nom, logo, couleurs), devise, fuseau, mode de prix, moyens de paiement acceptés
  (`/payments/configurations`), notifications (2.3).

### 2.3 Configuration des paiements et des notifications du restaurant
- **Moyens de paiement** : `GET/POST /payments/configurations`, `credentials`, `mode`, `methods`, `enable/disable`.
  Écran à faire dans Paramètres. Les identifiants vont au coffre : le formulaire écrit, ne relit jamais.
- **Fournisseurs de notifications** (SMS, WhatsApp, email du restaurant) : `/notification-configurations` (configure,
  credentials, mode, établissements). Même règle : secrets en écriture seule, jamais affichés.

## 3. Important (P1) — modules prêts sans écran

| Page à créer | Routes disponibles | Contenu attendu |
|---|---|---|
| **Clients (CRM)** | `/customers` (16 routes) | liste et recherche, fiche (contact, tags, préférences, historique commandes/réservations), consentements marketing, règle de contact, anonymisation RGPD |
| **Stock** | `/inventory` (23 routes) | ingrédients, articles par établissement, réceptions, pertes, inventaires, transferts, seuils et alertes, recettes liées aux produits, mouvements |
| **Avis clients** | `/reviews` (16 routes) | liste et filtres, note moyenne et répartition, traitement (examiné, archivé), questionnaires (brouillon, questions, publication, clôture) |
| **Marketing — Promotions** | `/promotions` (14 routes) | promotions (règles, période, limites), activation/pause/expiration, codes promo, application à une commande depuis la console |
| **Marketing — Fidélité** | `/loyalty` (19 routes) | programme (barème, inscription auto, expiration), récompenses, comptes clients, ajustements, échange d'une récompense sur une commande |
| **Abonnement** | `/billing` (7 routes restaurant) | plan actuel et usage des quotas, catalogue des plans, changement de plan, résiliation et annulation de la résiliation, historique des paiements |
| **Journal d'activité** | `/audit` | journal du restaurant (qui a fait quoi), filtres module/acteur/période, détail d'une écriture (`audit.view`) |
| **Intégrations** | `/integrations` (18 routes) | clés d'API (création affichée une fois, rotation, révocation), webhooks (événements, test, secret, livraisons et relances) |
| **Notifications** | `/me/notifications`, `/me/notification-preferences` | page complète de la cloche (lues, archivées), préférences par type ; la cloche n'affiche aujourd'hui que les 20 dernières |

## 4. Écrans existants incomplets

- **Établissements** : création et changements de statut seulement. Manquent la modification du nom, de l'adresse, de la
  capacité, du fuseau et des horaires (`PUT .../name|address|capacity|time-zone|business-hours`), et les **fermetures ou
  ouvertures exceptionnelles** (`exceptional-closures`, `exceptional-openings`, `exceptions/{date}`).
- **Commandes** : pas de rattachement d'un client à la commande, pas d'application d'une promotion ou d'une récompense
  fidélité, pas de reçu. Les frais de livraison ne s'ajoutent pas au total (Orders n'a pas de ligne de frais : backend).
- **Rapports** : exécution ponctuelle et exports seulement. Manquent les rapports enregistrés et leur envoi planifié
  (`/reporting/definitions`, `schedule`).
- **Salle & tables** : pas de QR code par table (commande ou paiement à table).
- **Réservations** : pas de rattachement à une fiche client depuis l'écran (le backend le permet).
- **Mon profil** : 2FA et documents (reportés) ; préférences de notifications à brancher sur `/me/notification-preferences`.
- **Tableau de bord** : aucune alerte stock bas ni avis négatif (dès que Stock et Avis auront leurs écrans).

## 5. Manques côté backend (pour aller plus loin)

- Paramètres du restaurant modifiables par lui-même (2.2).
- Reçu / facture client (PDF ou email) après encaissement.
- Frais de livraison ajoutés au total de la commande (ligne de frais dans Orders).
- Commande en ligne et réservation par le client lui-même (site public ou QR) : pas de parcours client aujourd'hui.
- 2FA (TOTP) dans Identity ; documents KYC du restaurant (Tenants).
- Synchronisation automatique des permissions des rôles système quand un module en ajoute (aujourd'hui : script SQL).
- Paramètres de notifications du personnel (quelles alertes chaque membre reçoit, par canal).

## 6. Ordre recommandé

1. **Encaissement** (2.1) : sans lui, aucune commande ne se termine.
2. **Paramètres** : restaurant + moyens de paiement + notifications (2.2, 2.3), backend compris.
3. **Établissements complets** (horaires, exceptions) : nécessaire aux réservations.
4. **Clients** puis **Marketing** (promotions, fidélité) : ils s'appuient sur les clients.
5. **Stock**, **Avis**, **Abonnement**.
6. **Journal d'activité**, **Intégrations**, **Notifications** (page complète).
7. Compléments : rapports planifiés, QR codes, reçus, frais de livraison.

Navigation cible : Tableau de bord, Commandes, Encaissements, Réservations, Cuisine, Livraison & retrait, Salle & tables,
Menu & produits, Stock, Clients, Marketing, Avis, Rapports | Personnel, Établissements, Abonnement, Paramètres,
Journal, Intégrations, Mon profil.
