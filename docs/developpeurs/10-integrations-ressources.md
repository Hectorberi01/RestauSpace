# Ressources du restaurant

> Les routes de l'API d'intégration les plus utiles : commandes, carte, établissements, stocks, clients,
> réservations, paiements, cuisine, rapports. Toutes sous `{{API_URL}}/v1`, avec `X-Api-Key`.

Les écritures sans contenu à renvoyer répondent `204`. Une création répond en général `201` avec `{"id": "…"}` (les
exceptions sont indiquées : paiement en ligne, exports en `202`).

## Commandes

| Méthode | Route | Scope | Description |
|---|---|---|---|
| GET | `/orders?locationId&status&page&pageSize` | `orders.take` ou `orders.process` | Commandes, les plus récentes d'abord (`pageSize` 20 par défaut, 100 au plus) |
| GET | `/orders/{id}` | idem | Une commande avec ses lignes, remises et totaux |
| GET | `/orders/by-customer/{customerId}` | `customers.view` | Commandes d'un client |
| POST | `/orders` | `orders.take` | Créer une commande en cours (brouillon) |
| POST | `/orders/{id}/items` | `orders.take` | Ajouter une ligne `{productId, quantity, variantId, modifierIds, note}` |
| PUT | `/orders/{id}/items/{itemId}` | `orders.take` | Modifier une ligne |
| DELETE | `/orders/{id}/items/{itemId}` | `orders.take` | Retirer une ligne |
| PUT | `/orders/{id}/customer` | `orders.take` | Rattacher un client `{customerId}` |
| POST | `/orders/{id}/discounts` | `orders.discount` | Remise manuelle `{amount, reference, label}` |
| POST | `/orders/{id}/place` | `orders.take` | Valider : la commande part en cuisine |
| POST | `/orders/{id}/accept` | `orders.process` | Accepter |
| POST | `/orders/{id}/start-preparation` | `orders.process` | Lancer la préparation |
| POST | `/orders/{id}/ready` | `orders.process` | Prête |
| POST | `/orders/{id}/complete` | `orders.process` | Terminée |
| POST | `/orders/{id}/cancel` | `orders.process` | Annuler `{reason}` |

Statuts : `Draft` → `Placed` → `Accepted` → `Preparing` → `Ready` → `Completed`, ou `Cancelled`.
Canaux (`channel`) : `Counter`, `QrCode`, `Web`, `MobileApp`, `Kiosk`, `Api`, `Phone`, `WhatsApp`, `DeliveryPlatform`.

### Créer une commande depuis votre logiciel

```bash
# 1. Brouillon
curl -s -X POST {{API_URL}}/v1/orders -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
  -d '{"locationId":"'$LOCATION'","type":"Takeaway"}'
# → {"id":"0199…"}

# 2. Lignes
curl -s -X POST {{API_URL}}/v1/orders/$ORDER/items -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
  -d '{"productId":"'$PRODUCT'","quantity":2}'

# 3. Validation
curl -s -X POST {{API_URL}}/v1/orders/$ORDER/place -H "X-Api-Key: $KEY"
```

Une commande créée avec une clé a le canal `Api`.

### Commandes des plateformes de livraison

Pour importer une commande reçue sur une plateforme (Gozem, Glovo, Uber Eats…), créez-la avec :

```json
{"locationId": "0199…", "type": "Delivery", "channel": "DeliveryPlatform", "platform": "glovo", "externalReference": "GLV-88231"}
```

- `platform` : le nom de la plateforme en minuscules (30 caractères), `externalReference` : sa référence (64).
- La même référence sur la même plateforme est refusée une deuxième fois (`409 orders.order.external_reference_duplicate`) :
  un nouvel import ne crée pas de doublon.
- Encaissez-la ensuite avec le moyen `DeliveryPlatform` (voir Paiements) : elle est payée par la plateforme et ne compte
  pas dans la caisse.

## Carte

Lectures : `catalog.manage` ou `orders.take`. Écritures : `catalog.manage`.

| Méthode | Route | Description |
|---|---|---|
| GET | `/catalog/products?status&page&pageSize` | Produits (`Draft`, `Active`, `Unavailable`, `Archived`) |
| GET | `/catalog/products/{id}` | Produit : prix, déclinaisons, compléments, allergènes, canaux masqués, prix par établissement et canal |
| GET | `/catalog/menus?locationId` · `/catalog/menus/{id}` | Menus, sections, créneaux |
| GET | `/catalog/modifier-groups` · `/catalog/tax-categories` · `/catalog/allergens` | Référentiels |
| POST | `/catalog/products/{id}/mark-unavailable` · `/activate` | Rupture / retour d'un produit (tous les établissements) |
| PUT | `/catalog/products/{id}/variants/{variantId}/availability` | `{isAvailable}` d'une déclinaison |
| PUT | `/catalog/modifier-groups/{id}/modifiers/{modifierId}/availability` | `{isAvailable}` d'un complément |
| PUT | `/catalog/products/{id}/base-price` | Prix de base |
| PUT | `/catalog/products/{id}/price-overrides` | Prix d'un établissement et/ou d'un canal `{locationId, channel, basePrice}` (canal : `InHouse`, `Web`, `MobileApp`, `Kiosk`) |

Toute modification de la carte émet le webhook `catalog.changed`.

## Établissements

| Méthode | Route | Scope | Description |
|---|---|---|---|
| GET | `/locations?page&pageSize&includeArchived` | `locations.manage`, `catalog.manage` ou `orders.take` | Établissements |
| GET | `/locations/{id}` | idem | Adresse, fuseau, horaires, fermetures, état |

## Stocks

Lectures : `inventory.manage` ou `inventory.operate`.

| Méthode | Route | Scope | Description |
|---|---|---|---|
| GET | `/inventory/items?locationId&lowStockOnly` | lecture | Articles en stock : quantité, seuil, coût moyen, alerte |
| GET | `/inventory/items/{id}/movements?page&pageSize` | lecture | Mouvements d'un article |
| GET | `/inventory/reorder-suggestions?locationId` | lecture | Suggestions de réapprovisionnement |
| GET | `/inventory/recipe-costs?locationId` | lecture | Coût de revient des recettes |
| GET | `/inventory/purchase-orders?locationId&status` | lecture | Commandes fournisseurs |
| POST | `/inventory/items/{id}/receipts` | `inventory.operate` | Réception `{quantity: {amount, unit}, reference, totalCost}` |
| POST | `/inventory/items/{id}/consumptions` | `inventory.operate` | Consommation |
| POST | `/inventory/items/{id}/waste` | `inventory.operate` | Perte `{quantity, reason}` |
| POST | `/inventory/items/{id}/counts` | `inventory.operate` | Inventaire physique `{countedQuantity, reason}` |
| POST | `/inventory/transfers` | `inventory.operate` | Transfert entre établissements |

Unités : `Gram`, `Kilogram`, `Milliliter`, `Liter`, `Piece`.

## Clients

| Méthode | Route | Scope | Description |
|---|---|---|---|
| GET | `/customers?name&email&phone&tag&page&pageSize` | `customers.view` | Recherche (50 par page par défaut, total non compté) |
| GET | `/customers/{id}` | `customers.view` | Fiche : contacts, étiquettes, préférences |
| GET | `/customers/{id}/consents` | `customers.view` | Consentements par canal |
| POST | `/customers` | `customers.manage` | Créer `{name, email, phone, tags, preferences}` |
| POST | `/customers/import` | `customers.manage` | Import |
| GET | `/customers/{id}/export` | `customers.manage` | Export des données d'un client (droit d'accès) |
| POST | `/customers/{id}/anonymize` | `customers.anonymize` | Effacement définitif |

Ne contactez un client à des fins commerciales que si son consentement marketing est accordé sur ce canal.

## Réservations

| Méthode | Route | Scope | Description |
|---|---|---|---|
| GET | `/reservations?from&to&locationId` | `reservations.manage` ou `configure` | Agenda sur une période |
| GET | `/reservations/{id}` | idem | Une réservation |
| GET | `/reservations/availability?locationId&startAt&partySize` | idem | Disponibilité d'un créneau |
| POST | `/reservations` | `reservations.manage` | Créer `{locationId, startAt, partySize, guestName, guestPhone, guestEmail, notes, durationMinutes, customerId}` |
| POST | `/reservations/{id}/confirm` · `/seat` · `/complete` · `/no-show` · `/cancel` | `reservations.manage` | Faire avancer la réservation |
| PUT | `/reservations/{id}/slot` · `/party-size` | `reservations.manage` | Déplacer, changer le nombre de personnes |

## Paiements

| Méthode | Route | Scope | Description |
|---|---|---|---|
| GET | `/payments?orderId` | `payments.collect` ou `payments.refund` | Paiements d'une commande |
| GET | `/payments/{id}` | idem | Un paiement |
| GET | `/payments/journal?from&to&locationId&limit` | `payments.collect` | Journal des encaissements et remboursements (93 jours au plus) |
| POST | `/payments/offline` | `payments.collect` | Encaissement hors ligne `{orderId, provider, reference, amount, paymentMethod}` — `Idempotency-Key` obligatoire |
| POST | `/payments/online` | `payments.collect` | Paiement en ligne `{orderId, provider, amount, paymentMethod, payerPhone}` — `Idempotency-Key` obligatoire |
| POST | `/payments/{id}/refunds` | `payments.refund` | Remboursement `{reason, amount}` — `Idempotency-Key` obligatoire |
| GET / POST | `/payments/cash-sessions/**` | `payments.collect` | Caisse : ouverture, mouvements, clôture |

Moyens hors ligne (`provider`) : `cash` (espèces), `card_terminal` (terminal de paiement), `mobile_money` (Mobile Money
sur le téléphone du restaurant), `bank_transfer` (virement), `gift_card` (carte cadeau ou avoir), `delivery_platform`
(payé à la plateforme de livraison). Un remboursement en ligne passe par le prestataire ; certains (FedaPay, KkiaPay…)
ne remboursent que depuis leur propre tableau de bord.

## Cuisine

| Méthode | Route | Scope | Description |
|---|---|---|---|
| GET | `/kitchen/tickets?locationId&stationId` | `kitchen.operate` ou `configure` | Tickets ouverts |
| GET | `/kitchen/tickets/{id}` | idem | Un ticket |
| POST | `/kitchen/tickets/{id}/start` · `/ready` · `/complete` | `kitchen.operate` | Faire avancer un ticket |

## Rapports et exports

| Méthode | Route | Scope | Description |
|---|---|---|---|
| GET | `/reporting/catalog` | `reporting.view` | Indicateurs et filtres disponibles |
| GET | `/reporting/dashboard?from&to&locationId` | `reporting.view` | Tableau de bord |
| POST | `/reporting/run` | `reporting.view` | Calculer `{metrics, filters, from, to, granularity, byLocation}` |
| POST | `/reporting/exports` | `reporting.export` | Demander un export `{metrics, from, to, granularity, format: "Csv" \| "Xlsx" \| "Pdf"}` → `202 {id}` |
| POST | `/reporting/exports/accounting` | `reporting.export` | Journal comptable (SYSCOHADA) `{from, to}` → `202 {id}` |
| GET | `/reporting/exports/{id}` | `reporting.view` | État : `Queued`, `Running`, `Completed`, `Failed`, `Expired` |
| GET | `/reporting/exports/{id}/file` | `reporting.view` | Le fichier, une fois `Completed` |

Un export demandé par une clé n'envoie aucune notification : interrogez son état (toutes les 5 à 10 secondes), puis
téléchargez le fichier.

```ts
const {id} = await restauspace('/reporting/exports/accounting', {method: 'POST', body: JSON.stringify({from: '2026-09-01', to: '2026-09-30'})});
let job;
do {
  await new Promise(resolve => setTimeout(resolve, 5000));
  job = await restauspace(`/reporting/exports/${id}`);
} while (job.status === 'Queued' || job.status === 'Running');
if (job.status === 'Completed') {
  const file = await fetch(`{{API_URL}}/v1/reporting/exports/${id}/file`, {headers: {'X-Api-Key': process.env.RESTAUSPACE_API_KEY!}});
  await writeFile('journal-2026-09.csv', Buffer.from(await file.arrayBuffer()));
}
```

## Livraison

| Méthode | Route | Scope | Description |
|---|---|---|---|
| GET | `/delivery/fulfillments` · `/delivery/fulfillments/{id}` · `/delivery/fulfillments/by-order/{orderId}` | `delivery.operate` | Livraisons et retraits |
| GET | `/delivery/settings/{locationId}` | `delivery.operate` | Réglages et zones |

## Avis

| Méthode | Route | Scope | Description |
|---|---|---|---|
| GET | `/reviews/feedbacks?locationId&status&minRating&maxRating&from&to&hasComment&published&page&pageSize` | `reviews.view` | Avis, du plus récent au plus ancien (commentaire = parole du client) |
| GET | `/reviews/feedbacks/summary?locationId&from&to` | `reviews.view` | Nombre, moyenne, répartition par note et par statut |
| POST | `/reviews/feedbacks` | `reviews.submit` | Enregistrer l'avis d'un client : `{locationId, rating, comment, customerId, orderId}` |
| POST | `/reviews/feedbacks/{id}/reviewed` · `/archive` | `reviews.manage` | Traiter (réservé à un membre de l'équipe : refusé à une clé), archiver (un avis archivé n'est plus publié) |
| POST | `/reviews/feedbacks/{id}/publish` · `/unpublish` | `reviews.manage` | Afficher l'avis sur le site et l'application (note, commentaire, date ; jamais l'auteur), ou le retirer |

`publishedAtUtc` indique si un avis est publié. Le site le lit par `GET /storefront/reviews`
(voir [Avis clients](07-vente-reservations-questionnaires.md#avis-clients)).

## Recettes courantes

- **Caisse ou ERP qui reçoit les commandes** : abonnez-vous à `orders.order_placed`, puis lisez `GET /orders/{id}` pour
  le détail ; faites avancer la commande avec `/accept`, `/ready`, `/complete`.
- **Rupture de stock depuis un autre logiciel** : `POST /catalog/products/{id}/mark-unavailable`, puis `/activate` au
  retour ; la carte en ligne est mise à jour immédiatement.
- **Comptabilité** : un export comptable par mois, ou le journal des paiements (`/payments/journal`) chaque nuit.
- **Tableau de bord** : `POST /reporting/run` sur la journée, rafraîchi toutes les 5 minutes.
