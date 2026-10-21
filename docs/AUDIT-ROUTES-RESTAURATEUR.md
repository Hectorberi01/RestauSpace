# Audit des routes non branchées sur la console restaurateur

Date : 11 octobre 2026. Périmètre : routes « restaurant » du backend (`/v1`, membres d'un tenant), comparées aux appels
de `restaurantos-app`.

Méthode :
- les routes sont extraites des fichiers `*Endpoints.cs`, groupes `MapGroup` résolus ;
- les routes déclarées par une boucle sont ajoutées à la main (étapes de livraison, actions des modèles de notification) ;
- les appels du front sont extraits de `api`, `post`, `put`, `del`, `all`, `downloadFile`, `putBlob` et `fetchBlob` ;
- les segments dynamiques (`${action}`, ternaires) sont développés.

Hors périmètre : `/admin/...`, `/auth/...`, webhooks des prestataires, stockage local de fichiers, formulaire de démo,
menu public.

## Synthèse

| | Routes |
|---|---|
| Routes restaurant | 373 |
| Branchées sur la console | 309 |
| **Non branchées** | **64** |
| dont lectures de détail `GET /{id}` (normal : la liste suffit) | 18 |
| dont fonctionnalités réellement absentes | 46 |

Aucun appel du front ne vise une route inexistante.

## 1. Fonctionnalités absentes (46 routes)

### P1 — gênant au quotidien

**Cuisine : routage par produit (2)**
- `PUT /kitchen/routings/{locationId}/routes/{productId}` et `DELETE …` (`kitchen.configure`).
- Les postes existent (Bar, Cuisine froide, Grill), mais la console ne règle que le poste par défaut. Tous les articles
  partent donc sur le même poste : impossible d'envoyer les boissons au Bar.
- À faire : dans Réglages cuisine, une liste produit → poste, avec recherche.

**Catalogue : modifier sans supprimer (9)**
- Taxes : `PUT /catalog/tax-categories/{id}/name` et `…/rate`.
- Options : `PUT /catalog/modifier-groups/{id}/name`, `…/modifiers/{modifierId}/name` et `…/price`.
- Variantes : `PUT /catalog/products/{id}/variants/{variantId}/price-delta`.
- Cartes : `PUT /catalog/menus/{id}/name`, `…/sections/{sectionId}/name`, `…/sections/{sectionId}/products/{productId}/position`.
- Aujourd'hui, changer le prix d'une option ou d'une variante oblige à la supprimer puis la recréer. On ne peut pas non
  plus renommer une carte ou une section, ni ordonner les produits dans une section.
- À faire : édition en ligne dans Menu & produits.

**Réservations : politique (1)**
- `PUT /reservations/policies/{locationId}` (`reservations.configure`).
- La console lit la politique (taille max, durées) mais ne permet pas de la modifier : délai minimum, réservation
  jusqu'à N jours, durée par défaut, minimum et maximum, limite d'annulation, taille max.
- À faire : un onglet « Règles » dans Réservations.

**Notifications aux clients : modèles et historique (13)**
- Modèles : `GET/POST /notification-templates`, `GET/PUT/DELETE /notification-templates/{id}`, `POST …/{id}/versions`,
  `…/publish`, `…/enable`, `…/disable`.
  - À faire : personnaliser les messages envoyés aux clients (confirmation de réservation, commande prête…), avec
    brouillon, versions et publication.
- Historique : `GET /notifications`, `POST /notifications`, `POST /notifications/{id}/cancel` (`notifications.view`,
  `notifications.send`).
  - À faire : journal des SMS, WhatsApp et e-mails envoyés (statut, échec), envoi manuel à un client, annulation d'un envoi
    programmé.
- Lieu : Paramètres › Notifications, ou une page dédiée.

### P2 — utile

| Route | Manque |
|---|---|
| `PUT /notification-configurations/{id}/locations`, `DELETE …/locations/{locationId}` | Réserver un fournisseur (SMS, WhatsApp) à certains établissements. Aujourd'hui : tous. |
| `GET/PUT /notifications/preferences/customers/{customerId}` | Préférences de canal d'un client. À clarifier : risque de doublon avec les consentements du CRM (déjà branchés). |
| `PUT /floor/dining-areas/{id}/tables/{tableId}/capacity` | Changer le nombre de places d'une table sans la recréer. |
| `PUT /kitchen/stations/{id}/name` | Renommer un poste. |
| `POST /kitchen/tickets/{id}/cancel` | Annuler un ticket seul (l'annulation de commande le fait déjà par le pont). |
| `GET /reporting/exports` | Historique des exports (re-télécharger un CSV envoyé par e-mail). |
| `POST /reporting/definitions/{id}/run` | Ouvrir un rapport enregistré à l'écran, sur une période choisie. |
| `GET /reporting/dashboard` | Le tableau de bord recalcule ses KPI côté navigateur à partir des commandes depuis la veille, et son top des plats charge 30 commandes une par une (31 appels, `topProducts`). La route agrégée du reporting éviterait ces appels. |
| `GET /reporting/catalog` | Liste des mesures pour un rapport personnalisé (aujourd'hui : préréglages codés en dur). |
| `DELETE /me/sessions` | « Me déconnecter partout » dans Mon profil (seule la révocation une par une existe). |
| `POST /me/erase` | Suppression de son compte (RGPD) depuis Mon profil. |

### P3 — hors console web ou de secours

| Route | Pourquoi |
|---|---|
| `POST /notifications/otp`, `…/otp/{id}/verify` | Parcours client (site, application mobile), pas la console. |
| `PUT /me/devices`, `DELETE /me/devices/{deviceId}` | Jetons push de l'application mobile. |
| `POST /kitchen/tickets` | Tickets créés par le pont Orders → Kitchen ; manuel inutile. |
| `POST /inventory/orders/{orderId}/consumption` | Déstockage fait par le pont Orders → Inventory ; utile seulement pour rattraper un échec. |
| `POST /inventory/items/{id}/consumptions` | Sortie de stock manuelle hors commande (repas du personnel, etc.). Peut passer en P2. |
| `GET /customers/{id}/consents/{channel}/marketing-permission` | Contrôle technique ; la fiche client affiche déjà les consentements. |

## 2. Lectures de détail non utilisées (18 routes, sans action)

La console lit ces objets depuis leur liste, ce qui suffit. Ces routes ne seront utiles que pour un lien direct ou une
mise à jour ciblée.

`GET` sur :
- `/catalog/tax-categories/{id}`, `/catalog/modifier-groups/{id}`
- `/delivery/fulfillments/{id}`, `/files/{id}`
- `/floor/dining-areas/{id}`, `/floor/table-sessions/{id}`
- `/integrations/webhooks/{id}`, `/integrations/api-keys/{id}`
- `/inventory/ingredients/{id}`, `/inventory/items/{id}`, `/inventory/recipes/{id}`
- `/kitchen/tickets/{id}`
- `/loyalty/rewards/{id}`, `/loyalty/accounts/{id}`
- `/promotions/coupons/{couponId}`
- `/reporting/definitions/{id}`, `/reviews/surveys/{id}`, `/staff/members/{id}`

## 3. Ordre conseillé

1. Routage cuisine par produit.
2. Édition du catalogue (prix des options et variantes, renommages, ordre dans les sections).
3. Politique de réservation.
4. Modèles et historique des notifications clients.
5. Tableau de bord sur `/reporting/dashboard`, puis le reste du P2.

Limite de la méthode : une route déclarée autrement que par `Map*("…")` avec un littéral ou les deux boucles connues
échapperait à l'inventaire (aucune autre trouvée).

## 4. Avancement (11 octobre 2026)

Branché depuis l'audit : routage cuisine par produit, édition du catalogue, politique de réservation, modèles et
historique des messages clients, puis tout le P2 :
- tableau de bord : indicateurs du jour via `/reporting/dashboard` (chiffre d'affaires et panier moyen du reporting) ;
- fournisseurs de notification par établissement, messages de service par client ;
- capacité de table, nom de poste, annulation de ticket ;
- historique des exports, rapport enregistré à l'écran, mesures au choix (catalogue du reporting) ;
- « Se déconnecter partout » et suppression du compte dans Mon profil.

Fait ensuite côté backend :
- mesures `sales.items_sold` et `sales.product_revenue` (dimension : id du produit), alimentées à la fin d'une commande ;
  le top des plats (tableau de bord, Menu & produits) les lit sur 30 jours, et ne charge plus le détail des commandes que
  si le reporting est vide ou interdit ;
- le seul propriétaire actif d'un restaurant ne peut plus effacer son compte lui-même (`identity.user.sole_owner`) ;
  l'effacement par le back-office reste possible ;
- `GET /notification-templates/types` : le catalogue des types (variables, nature, destinataire) ; la console ne garde
  que les libellés ;
- lien de désinscription marketing : jeton signé (HMAC, clé dérivée de `Notifications:OtpKey`) produit à l'envoi,
  jamais repris de la demande ; page `/desinscription` du site public ; routes anonymes
  `POST /public/notifications/unsubscribe/preview` (sans effet) et `POST /public/notifications/unsubscribe`.

À régler au déploiement : `Notifications:UnsubscribePageUrl` (URL de la page du site). Sans elle, aucun e-mail marketing
ne part.

Limite : les ventes par produit ne comptent que les commandes terminées après la mise en production.
