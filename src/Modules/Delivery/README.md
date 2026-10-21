# Module Delivery

But : organiser le retrait au comptoir et la livraison des commandes.

## 1. Frontières d'agrégat

| Racine | Enfants | Référencé par id seulement |
|---|---|---|
| `DeliverySettings` (un par établissement) | `DeliveryZone` (entité) | `LocationId` |
| `Fulfillment` (une fiche active par commande) | — | `OrderId`, `LocationId`, `DeliveryZoneId`, `CourierId`, `CourierRemittanceId` |
| `Courier` (fiche livreur du restaurant) | — | — |
| `CourierRemittance` (remise d'un livreur) | `CourierRemittanceItem` (valeur) | `LocationId`, `CourierId`, `FulfillmentId` |

- Les réglages portent la règle « pas de livraison sans zone active » : les zones n'existent que dans leurs réglages.
- La fiche fige le nom et les frais de la zone au moment de sa création : modifier une zone ne change pas les fiches en cours.
- Orders n'est jamais référencé : `OrderReference` `(TenantId, OrderId, LocationId, Kind, Total, IsOpen)` est rempli par l'hôte.

## 2. Classes

| Classe | Rôle | Transitions | Events |
|---|---|---|---|
| `DeliverySettings` | Services proposés, délais, zones | — | `DeliverySettingsCreated`, `DeliveryServicesChanged`, `DeliveryZonesChanged` |
| `DeliveryZone` | Nom unique, frais, minimum de commande, durée du trajet (5 à 240 min), active | — | — |
| `Fulfillment` | Retrait ou livraison d'une commande ; livreur (enregistré ou saisi), tarif de la course figé, montant à encaisser et encaissé | Retrait : `Pending -> ReadyForPickup -> Completed`. Livraison : `Pending -> Assigned -> OutForDelivery -> Completed`, `OutForDelivery -> Failed -> Pending`. Toute fiche ouverte peut passer `Cancelled` | `FulfillmentCreated`, `FulfillmentStatusChanged`, `DeliveryPaymentCollected` |
| `Courier` | Nom, téléphone, salarié ou indépendant, tarif par course facultatif, actif | Actif <-> désactivé | — |
| `CourierRemittance` | Solde les courses terminées d'un livreur : espèces encaissées, tarifs, attendu (espèces - tarifs), compté (signé), écart | — | `CourierRemittanceRecorded` |

Erreurs : `delivery.settings.*`, `delivery.zone.*`, `delivery.fulfillment.*`, `delivery.courier.*`, `delivery.remittance.*`, `delivery.collection.not_accepted`, `delivery.location.not_found`, `delivery.order.not_found`.

## 3. Choix

- Retrait proposé par défaut (20 min) ; livraison désactivée (45 min) tant qu'aucune zone active n'existe. Sans réglages enregistrés, la lecture renvoie ces valeurs (`IsConfigured = false`) ; ils sont créés à la première modification.
- Heure de retrait promise = max(maintenant + délai de retrait, heure demandée), demandée au plus 7 jours à l'avance. Heure de livraison promise = maintenant + délai de préparation + durée de la zone.
- La livraison exige un téléphone (E.164) et une adresse ; le total de la commande doit atteindre le minimum de la zone.
- Les frais de livraison sont ajoutés au total de la commande par l'hôte (`DeliveryFeeFlow`) et retirés si la fiche est annulée.
- Paiement à la livraison : au départ du livreur, le reste à payer de la commande (port `IDeliveryPayments`) devient le montant à encaisser ; il faut alors que l'établissement accepte l'encaissement par le livreur (moyen « Espèces encaissées par le livreur » `CashOnDelivery`, ou mobile money sur le téléphone du restaurant). À la remise, le livreur dit combien il a reçu (au plus le dû) et comment ; `DeliveryPaymentCollected` est transformé par l'hôte en paiement de la commande, référence `LIV-<fiche>` (idempotent).
- Les espèces `CashOnDelivery` ne sont pas comptées à la clôture de caisse : elles entrent dans la caisse à la remise du livreur. `CourierRemittanceRecorded` devient un mouvement d'espèces (entrée de ce que le livreur remet, sortie de ce qu'on lui paie) sur la caisse ouverte, motif `Remise livreur R-xxxxxxxx` (idempotent). Sans caisse ouverte, rien n'est fait (journalisé) et la console prévient avant.
- Une course n'est soldée qu'une fois (index unique sur `courier_remittance_items.fulfillment_id`, concurrence optimiste sur la fiche). Une remise couvre un seul livreur (même fiche, ou même nom pour deux livreurs saisis à la main) et un seul établissement.
- `orders.OrderReady` passe la fiche de retrait à « prête » ; `orders.OrderCancelled` annule la fiche active (hôte, par l'outbox, idempotent).
- Les events ne portent que des ids, le mode, le statut et des montants : jamais le nom, le téléphone ni l'adresse du client ou du livreur. Rien de tout cela n'est journalisé.
- Le lien WhatsApp de la course (console) est un simple lien `wa.me` ouvert depuis le téléphone de l'équipier : la plateforme n'envoie rien.

## 4. Permissions

- `delivery.operate` : lire les réglages, créer et suivre les fiches.
- `delivery.configure` : régler les services, les délais et les zones.
- `delivery.couriers` : gérer les fiches livreurs (la liste est lisible avec `delivery.operate`).
- `delivery.remit` : voir l'argent des livreurs et enregistrer leurs remises.

Les rôles système Owner et Manager reçoivent les nouvelles permissions au démarrage. Migrations : `scripts/migrations-livreurs.sh`.
