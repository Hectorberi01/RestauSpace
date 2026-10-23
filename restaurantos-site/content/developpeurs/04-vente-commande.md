# Panier, créneaux et commande

> Faire chiffrer le panier par le serveur, proposer un créneau, passer la commande, puis la suivre jusqu'au retrait ou à
> la livraison.

Toutes les routes sont sous `{{API_URL}}/v1/storefront` et exigent `X-Storefront-Key`.

## Déroulé

```
GET  /                                   établissements, modes, zones de livraison
GET  /locations/{id}/catalog              la carte
POST /cart/quote                          à chaque changement du panier
GET  /locations/{id}/slots                si le client choisit une heure
POST /orders            (Idempotency-Key)  la commande → trackingToken
POST /orders/{jeton}/payments             si paiement en ligne (page Paiement)
GET  /orders/{jeton}                      suivi, toutes les 5 à 10 s
POST /orders/{jeton}/cancel               tant que canCancel vaut true
```

## Une ligne de panier

```json
{"productId": "0199…", "quantity": 2, "variantId": "0199…", "modifierIds": ["0199…"], "note": "Sans oignon"}
```

- `quantity` : de 1 à 99 ; un panier compte au plus 50 lignes.
- `variantId` : obligatoire si le produit a des déclinaisons.
- `modifierIds` : les compléments choisis, dans le respect des minimums et maximums de chaque groupe.
- `note` : 200 caractères au plus. Ne demandez jamais d'information personnelle dans ce champ.

## Chiffrer le panier : `POST /cart/quote`

Rien n'est enregistré. Appelez-la à chaque modification du panier, du mode, de la zone, du code promo ou de la
récompense.

```json
{
  "locationId": "0199…",
  "mode": "Delivery",
  "lines": [{"productId": "0199…", "quantity": 2, "variantId": null, "modifierIds": ["0199…"], "note": null}],
  "couponCode": "BIENVENUE",
  "deliveryZoneId": "0199…",
  "rewardId": null
}
```

```json
{
  "locationId": "0199…", "mode": "Delivery", "currencyCode": "XOF", "pricesIncludeTax": true,
  "lines": [{
    "productId": "0199…", "variantId": null, "name": "Poulet braisé", "variantName": null, "quantity": 2,
    "unitPrice": 4000, "modifiers": [{"id": "0199…", "name": "Moyo", "priceDelta": 300}], "note": null, "lineTotal": 8600
  }],
  "subTotal": 8600,
  "discountTotal": 860,
  "taxTotal": 0,
  "total": 8740,
  "minimumOrderAmount": 5000,
  "meetsMinimum": true,
  "canPayAtCounter": true,
  "canPayOnline": true,
  "onlinePaymentOptions": [{"provider": "fedapay", "displayName": "FedaPay", "methods": ["MobileMoney", "Card"]}],
  "deliveryFee": 1000,
  "deliveryZoneId": "0199…",
  "coupon": {"code": "BIENVENUE", "isValid": true, "refusalCode": null, "label": "-10 % sur la première commande", "discount": 860},
  "reward": null
}
```

| Champ | Utilisation |
|---|---|
| `lines[].lineTotal` | (prix unitaire + compléments) × quantité. |
| `total` | **Le montant à payer**, frais de livraison compris, remises déduites. À afficher tel quel. |
| `meetsMinimum` | `false` : désactivez « Commander » et affichez `minimumOrderAmount` (comparé au total hors frais de livraison). |
| `canPayAtCounter` / `canPayOnline` | Les choix de paiement à proposer pour ce montant. |
| `onlinePaymentOptions` | Les prestataires disponibles et leurs moyens (`MobileMoney`, `Card`, `ApplePay`, `GooglePay`, `PayPal`, `BankTransfer`). |
| `coupon` | Un code refusé ne fait pas échouer le devis : `isValid: false` et `refusalCode` (à traduire, voir [Codes d'erreur](12-reference-erreurs.md#codes-promo)). |
| `reward` | La récompense de fidélité appliquée (client connecté). |

Le devis ne vérifie pas que l'établissement est ouvert : utilisez `acceptsOrdersNow` et `serviceModes` de `GET /`.

## Proposer une heure : `GET /locations/{id}/slots`

```bash
curl -s "{{API_URL}}/v1/storefront/locations/$LOCATION/slots?mode=Delivery&zoneId=$ZONE" -H "X-Storefront-Key: $KEY"
```

```json
{
  "locationId": "0199…", "mode": "Delivery", "timeZoneId": "Africa/Porto-Novo", "slotMinutes": 15, "capacity": 6,
  "slots": [{"startUtc": "2026-10-14T12:15:00+00:00", "isAvailable": true}, {"startUtc": "2026-10-14T12:30:00+00:00", "isAvailable": false}]
}
```

- Créneaux de 15 minutes sur 7 jours, à partir de maintenant + délai de préparation, dans les horaires de
  l'établissement.
- `mode` : `DineIn`, `Takeaway` (par défaut) ou `Delivery`, **casse exacte**. Pour la livraison, passez `zoneId` : chaque
  heure est alors l'**heure de livraison** (trajet compris).
- `isAvailable: false` : créneau complet, ou commande en ligne en pause.
- Envoyez l'heure choisie dans `scheduledForUtc`. Sans heure : « dès que possible ».

## Passer la commande : `POST /orders`

En-têtes : `Idempotency-Key` (obligatoire), `X-Human-Check` si l'application l'exige, `X-Customer-Token` si le client
est connecté.

```json
{
  "locationId": "0199…",
  "mode": "Delivery",
  "lines": [{"productId": "0199…", "quantity": 2, "variantId": null, "modifierIds": ["0199…"], "note": null}],
  "payment": "Online",
  "customer": {"name": "Afi", "email": null, "phone": "+2290197000000", "marketingConsent": false, "language": "fr"},
  "scheduledForUtc": null,
  "expectedTotal": 8740,
  "couponCode": "BIENVENUE",
  "delivery": {"zoneId": "0199…", "addressLine": "Rue 12.150, maison bleue", "landmark": "Face à la pharmacie", "instructions": "Appeler en arrivant"},
  "rewardId": null,
  "tableToken": null
}
```

| Champ | Règle |
|---|---|
| `mode` | Doit faire partie de `serviceModes` de l'établissement. |
| `payment` | `Counter` (au comptoir, à la livraison ou à table) ou `Online`, selon `canPayAtCounter` / `canPayOnline`. |
| `customer` | Invité : `name` et (`phone` ou `email`) obligatoires ; `phone` au format international `+229…`. Client connecté : facultatif (le profil est utilisé ; seuls `phone` et `marketingConsent` sont lus). Borne : ignoré. |
| `customer.marketingConsent` | `true` seulement si le client a coché une case non pré-cochée. |
| `scheduledForUtc` | Une heure de `/slots`, au plus 14 jours plus tard ; `null` = dès que possible. |
| `expectedTotal` | Le `total` du dernier devis. S'il diffère du total recalculé : `409 storefront.checkout.price_changed` → refaites le devis et montrez le nouveau total. |
| `delivery` | Obligatoire en livraison : zone, adresse (200 caractères), repère et instructions facultatifs ; un téléphone est exigé. |
| `rewardId` | Client connecté : une récompense de `GET /customer`. Les points sont débités à la commande. |
| `tableToken` | Commande à table (voir plus bas). |

Réponse `201` :

```json
{
  "orderId": "0199…",
  "number": "A-1042",
  "status": "Received",
  "currencyCode": "XOF",
  "total": 8740,
  "payment": "Online",
  "acceptBeforeUtc": null,
  "payBeforeUtc": "2026-10-14T12:20:00+00:00",
  "trackingToken": "pZ8xQ…",
  "ticketPrinted": false
}
```

- **Gardez `trackingToken`** (stockage de l'appareil) : c'est le seul moyen de suivre, payer ou annuler la commande. Il
  n'expire pas ; traitez-le comme un secret.
- `status: "Accepted"` : acceptation automatique et paiement au comptoir, la commande part en cuisine.
- `status: "Received"` + `acceptBeforeUtc` : le restaurant doit l'accepter avant cette heure (sinon elle est annulée).
- `status: "Received"` + `payBeforeUtc` : paiement en ligne attendu avant cette heure (15 minutes par défaut), sinon la
  commande est annulée. Enchaînez sur [Paiement en ligne](05-vente-paiement.md).

### Refus fréquents

| Code | Que faire |
|---|---|
| `storefront.checkout.closed`, `paused`, `unavailable` | Afficher « Commande en ligne indisponible pour le moment » et proposer une heure plus tard. |
| `storefront.checkout.closed_at_schedule`, `schedule_too_soon`, `slot_full` (409) | Recharger `/slots` et faire choisir une autre heure. |
| `storefront.checkout.price_changed` (409) | Refaire le devis, montrer le nouveau total, redemander la confirmation. |
| `storefront.checkout.below_minimum` | Montrer le minimum. |
| `storefront.checkout.counter_limit_exceeded` | Proposer le paiement en ligne. |
| `catalog.product.not_sellable`, `variant_unavailable`, `catalog.modifier_group.selection_modifier_unavailable` | Recharger la carte, retirer l'article épuisé du panier. |
| `storefront.checkout.customer_required`, `common.phone.invalid`, `common.email.invalid` | Corriger le formulaire. |
| `promotions.*` | Le code promo n'est plus valable : le retirer, refaire le devis. |
| `storefront.human_check.failed` | Nouveau jeton anti-robot, même `Idempotency-Key`. |

## Exemples complets

Les types d'erreur (`StorefrontError`, `StorefrontException`) et le rangement des jetons reprennent les
[clients prêts à l'emploi](13-exemples-clients.md).

```bash
curl -s -X POST {{API_URL}}/v1/storefront/orders \
  -H "X-Storefront-Key: $KEY" \
  -H "Content-Type: application/json" -H "Accept: application/json" \
  -H "Idempotency-Key: $(uuidgen)" \
  -d '{"locationId":"'$LOCATION'","mode":"Takeaway","payment":"Counter","expectedTotal":8000,
       "lines":[{"productId":"'$PRODUCT'","quantity":2}],
       "customer":{"name":"Afi","phone":"+2290197000000","language":"fr"}}'
```

```ts
type CartLine = {productId: string; quantity: number; variantId?: string | null; modifierIds?: string[]; note?: string | null};

export async function placeOrder(cart: CartLine[], locationId: string, expectedTotal: number, idempotencyKey: string) {
  const response = await fetch('{{API_URL}}/v1/storefront/orders', {
    method: 'POST',
    headers: {
      'X-Storefront-Key': STOREFRONT_KEY,
      'Content-Type': 'application/json',
      Accept: 'application/json',
      'Idempotency-Key': idempotencyKey, // crypto.randomUUID() au clic sur « Commander », gardé pour les nouveaux essais
    },
    body: JSON.stringify({
      locationId, mode: 'Takeaway', payment: 'Counter', lines: cart, expectedTotal,
      customer: {name: 'Afi', phone: '+2290197000000', email: null, marketingConsent: false, language: 'fr'},
    }),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.code ?? `http_${response.status}`);
  }
  const placed = await response.json();
  localStorage.setItem(`order:${placed.orderId}`, placed.trackingToken);
  return placed;
}
```

```dart
Future<Map<String, dynamic>> placeOrder(List<Map<String, dynamic>> lines, String locationId, num expectedTotal, String idempotencyKey) async {
  final response = await http.post(
    Uri.parse('$api/orders'),
    headers: {
      'X-Storefront-Key': key,
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'Idempotency-Key': idempotencyKey, // const Uuid().v4(), gardé pour les nouveaux essais
    },
    body: jsonEncode({
      'locationId': locationId, 'mode': 'Takeaway', 'payment': 'Counter', 'lines': lines, 'expectedTotal': expectedTotal,
      'customer': {'name': 'Afi', 'phone': '+2290197000000', 'email': null, 'marketingConsent': false, 'language': 'fr'},
    }),
  );
  final body = response.body.isEmpty ? null : jsonDecode(utf8.decode(response.bodyBytes));
  if (response.statusCode != 201) throw StorefrontException(response.statusCode, body?['code'] as String?, null);
  await secureStorage.write(key: 'order:${body['orderId']}', value: body['trackingToken']);
  return body;
}
```

```swift
struct PlacedOrder: Decodable { let orderId: String; let number: String; let status: String; let total: Decimal; let trackingToken: String }

func placeOrder(lines: [[String: Any]], locationId: String, expectedTotal: Decimal, idempotencyKey: String) async throws -> PlacedOrder {
    var request = URLRequest(url: URL(string: "{{API_URL}}/v1/storefront/orders")!)
    request.httpMethod = "POST"
    request.setValue(storefrontKey, forHTTPHeaderField: "X-Storefront-Key")
    request.setValue("application/json", forHTTPHeaderField: "Content-Type")
    request.setValue("application/json", forHTTPHeaderField: "Accept")
    request.setValue(idempotencyKey, forHTTPHeaderField: "Idempotency-Key") // UUID().uuidString, gardé pour les nouveaux essais
    request.httpBody = try JSONSerialization.data(withJSONObject: [
        "locationId": locationId, "mode": "Takeaway", "payment": "Counter", "lines": lines, "expectedTotal": expectedTotal,
        "customer": ["name": "Afi", "phone": "+2290197000000", "marketingConsent": false, "language": "fr"],
    ])
    let (data, response) = try await URLSession.shared.data(for: request)
    guard (response as? HTTPURLResponse)?.statusCode == 201 else {
        let code = (try? JSONSerialization.jsonObject(with: data) as? [String: Any])?["code"] as? String
        throw StorefrontError(status: (response as? HTTPURLResponse)?.statusCode ?? 0, code: code, retryAfter: nil)
    }
    let placed = try JSONDecoder().decode(PlacedOrder.self, from: data)
    try Keychain.save(placed.trackingToken, account: "order:\(placed.orderId)")
    return placed
}
```

```kotlin
fun placeOrder(lines: JSONArray, locationId: String, expectedTotal: BigDecimal, idempotencyKey: String): JSONObject {
    val body = JSONObject()
        .put("locationId", locationId).put("mode", "Takeaway").put("payment", "Counter")
        .put("lines", lines).put("expectedTotal", expectedTotal)
        .put("customer", JSONObject().put("name", "Afi").put("phone", "+2290197000000").put("marketingConsent", false).put("language", "fr"))
    val request = Request.Builder()
        .url("{{API_URL}}/v1/storefront/orders")
        .header("X-Storefront-Key", BuildConfig.STOREFRONT_KEY)
        .header("Accept", "application/json")
        .header("Idempotency-Key", idempotencyKey) // UUID.randomUUID().toString(), gardé pour les nouveaux essais
        .post(body.toString().toRequestBody("application/json".toMediaType()))
        .build()
    client.newCall(request).execute().use { response ->
        val text = response.body?.string().orEmpty()
        if (response.code != 201) throw StorefrontException(response.code, text.takeIf { it.isNotEmpty() }?.let { JSONObject(it).optString("code") }, null)
        return JSONObject(text).also { secureStore.put("order:${it.getString("orderId")}", it.getString("trackingToken")) }
    }
}
```

## Suivre la commande : `GET /orders/{trackingToken}`

Interrogez toutes les 5 à 10 secondes tant que l'écran de suivi est visible, et arrêtez-vous à `Completed` ou
`Cancelled`.

```json
{
  "orderId": "0199…", "number": "A-1042", "locationId": "0199…", "locationName": "Chez Awa Cotonou",
  "status": "Preparing",
  "payment": "Online", "currencyCode": "XOF",
  "lines": [{"productId": "0199…", "name": "Poulet braisé", "quantity": 2, "unitPrice": 4000, "modifiers": [], "lineTotal": 8000, "…": "…"}],
  "total": 8740, "discountTotal": 860, "amountDueAtCounter": 0,
  "placedAtUtc": "2026-10-14T12:05:00+00:00", "scheduledForUtc": null, "estimatedReadyAtUtc": "2026-10-14T12:35:00+00:00",
  "acceptBeforeUtc": null, "canCancel": false,
  "paymentState": "Paid", "amountPaid": 8740, "payBeforeUtc": null, "canPay": false, "paymentOptions": [],
  "mode": "Delivery",
  "delivery": {"zoneName": "Haie Vive", "fee": 1000, "status": "Assigned", "promisedAtUtc": "2026-10-14T12:40:00+00:00"}
}
```

| `status` | Affichage suggéré |
|---|---|
| `Received` | « Commande reçue » (+ « En attente de paiement » si `paymentState` = `AwaitingPayment`) |
| `Accepted` | « Acceptée par le restaurant » |
| `Preparing` | « En préparation » |
| `Ready` | « Prête » (à retirer, ou en attente du livreur) |
| `Completed` | « Terminée » |
| `Cancelled` | « Annulée » |

| `paymentState` | Sens |
|---|---|
| `AtCounter` | Paiement au comptoir ; `amountDueAtCounter` = montant à régler |
| `AwaitingPayment` | Paiement en ligne attendu ; `canPay` dit si un essai est possible |
| `Processing` | Paiement en cours chez le prestataire |
| `Paid` | Payée |
| `Refunded` | Remboursée (commande refusée ou annulée après paiement) |

`delivery.status` : `Pending`, `Assigned`, `OutForDelivery`, `Completed`, `Failed`, `Cancelled`. L'adresse et le
livreur ne sont jamais renvoyés.

`404 storefront.order.not_found` : jeton invalide ou d'un autre restaurant.

## Annuler : `POST /orders/{trackingToken}/cancel`

Possible tant que `canCancel` vaut `true` (commande pas encore acceptée). Réponse `204`. Un paiement en ligne déjà
encaissé fait l'objet d'une demande de remboursement auprès du prestataire ; `paymentState` passe à `Refunded` quand
il est confirmé. Après acceptation : `409 storefront.order.not_cancellable` (le client doit
appeler le restaurant).

## Commande à table

Avec le jeton du QR code de la table (`GET /tables/{jeton}`) :

- `mode` = `DineIn`, `tableToken` = le jeton, `scheduledForUtc` = `null` ;
- les coordonnées du client sont facultatives ;
- la commande rejoint la note de la table (plusieurs commandes successives possibles).

Refus : `storefront.table.dine_in_only`, `storefront.table.not_scheduled`, `storefront.table.out_of_service`,
`storefront.table.not_found`.

## Livraison

1. Le client choisit sa zone parmi `locations[].delivery.zones` (`GET /`).
2. Devis avec `mode: "Delivery"` et `deliveryZoneId` : `deliveryFee` est inclus dans `total`.
3. Commande avec `delivery: {zoneId, addressLine, landmark, instructions}` et un téléphone.
4. Le suivi affiche `delivery.status` et `promisedAtUtc`.

Le minimum de commande se compare au total **hors** frais de livraison ; le plafond du paiement au comptoir au total
**avec** frais.

## Codes promo et récompenses

- Un seul code promo par commande (`couponCode`), vérifié au devis puis appliqué à la commande. Les promotions
  automatiques sans code ne s'appliquent pas en ligne.
- Une seule récompense de fidélité par commande (`rewardId`), pour un client connecté (voir
  [Comptes clients](06-vente-comptes-clients.md)).
