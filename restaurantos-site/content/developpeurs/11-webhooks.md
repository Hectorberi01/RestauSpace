# Webhooks

> Être prévenu en temps réel des événements du restaurant (commande passée, paiement encaissé, réservation, stock
> bas…) par un appel HTTPS signé vers votre serveur.

## Configurer

**Console › Intégrations › Webhooks › Nouveau webhook** (droit « Gérer les intégrations ») :

- une adresse **HTTPS publique** (pas d'adresse locale ni privée, pas d'identifiants dans l'adresse) ;
- les événements voulus ;
- le **secret** `whsec_…` s'affiche une seule fois : rangez-le dans le coffre de votre serveur.

Depuis le même écran : **Tester** (envoie `integrations.ping`), voir les livraisons et leur contenu exact, renvoyer une
livraison abandonnée, désactiver ou réactiver, régénérer le secret.

Un webhook reçoit les événements de **tous les établissements** du restaurant : filtrez sur `location_id` si besoin.

## Ce que vous recevez

```http
POST /webhooks/restauspace HTTP/1.1
Content-Type: application/json; charset=utf-8
User-Agent: RestauSpace-Webhooks/1.0
webhook-id: 0199a3c4-5b1e-8c2d-9f00-1a2b3c4d5e6f
webhook-timestamp: 1760444400
webhook-signature: v1,K5oZfzN95Z9UVu1EsfQmfVNQhnkZ2pj9o9NDN/H/pI4=

{"id":"0199a3c4-5b1e-8c2d-9f00-1a2b3c4d5e6f","type":"orders.order_placed","timestamp":"2026-10-14T12:20:00.0000000+00:00","data":{"order_id":"0199…","location_id":"0199…","order_number":"A-1042","order_type":"Takeaway","sub_total":{"amount":8000,"currency":"XOF"},"discount_total":{"amount":0,"currency":"XOF"},"tax_total":{"amount":0,"currency":"XOF"},"total":{"amount":8000,"currency":"XOF"}}}
```

- `id` = `webhook-id` : identifiant de l'événement, identique à chaque nouvel essai. **Clé de déduplication.**
- `timestamp` : moment de l'événement. `webhook-timestamp` : moment de l'envoi (secondes Unix), change à chaque essai.
- `data` : champs en snake_case, montants `{amount, currency}` en unités de la devise.
- **Aucune donnée personnelle** : pas de nom, téléphone, e-mail, adresse ni commentaire. Pour le détail, appelez l'API
  avec une clé (`GET /v1/orders/{order_id}`).

## Événements

| Type | Quand | `data` |
|---|---|---|
| `orders.order_placed` | Une commande est validée (tous canaux) | `order_id`, `location_id`, `order_number`, `order_type` (`DineIn`, `Takeaway`, `Delivery`), `sub_total`, `discount_total`, `tax_total`, `total` |
| `orders.order_completed` | Une commande est terminée | `order_id`, `location_id`, `total` |
| `orders.order_cancelled` | Une commande est annulée | `order_id`, `location_id`, `previous_status`, `total` |
| `payments.payment_captured` | Un paiement est encaissé | `payment_id`, `order_id`, `amount` |
| `payments.refund_succeeded` | Un remboursement aboutit | `payment_id`, `order_id`, `refund_id`, `amount`, `total_refunded`, `is_full_refund` |
| `reservations.reservation_created` | Une réservation est créée | `reservation_id`, `location_id`, `party_size`, `start_at_utc`, `duration_minutes` |
| `reservations.reservation_confirmed` | Elle est confirmée | `reservation_id`, `location_id` |
| `reservations.reservation_cancelled` | Elle est annulée | `reservation_id`, `location_id`, `cancelled_by` (`Customer`, `Staff`, `System`) |
| `reservations.reservation_no_show` | Le client n'est pas venu | `reservation_id`, `location_id` |
| `kitchen.ticket_ready` | Un ticket de cuisine est prêt | `ticket_id`, `order_id`, `location_id`, `ready_item_count`, `total_item_count` |
| `inventory.low_stock_reached` | Un article passe sous son seuil | `inventory_item_id`, `location_id`, `ingredient_id`, `current_quantity`, `reorder_threshold`, `unit` |
| `catalog.changed` | La carte a changé | `{}` : rechargez la carte |
| `integrations.ping` | Bouton « Tester » | `endpoint_id` |

De nouveaux types peuvent apparaître : ignorez (en répondant `2xx`) un type que vous ne connaissez pas.

## Vérifier la signature

La signature suit la convention *Standard Webhooks* :

1. Prenez le secret sans le préfixe `whsec_` et décodez-le en **base64** : ce sont les 32 octets de la clé HMAC.
2. Construisez la chaîne `{webhook-id}.{webhook-timestamp}.{corps brut}` (le corps **tel que reçu**, avant tout
   décodage JSON).
3. Calculez HMAC-SHA256, encodé en base64 standard.
4. `webhook-signature` contient une ou plusieurs signatures séparées par des espaces, chacune préfixée `v1,` (deux
   pendant les 24 heures qui suivent une régénération du secret). Acceptez si **l'une** est égale à la vôtre
   (comparaison à temps constant).
5. Refusez si `webhook-timestamp` est à plus de 5 minutes de votre horloge (protection contre le rejeu).

```ts
// Node.js (Express) : utiliser le corps brut.
import crypto from 'node:crypto';
import express from 'express';

const secret = Buffer.from(process.env.RESTAUSPACE_WEBHOOK_SECRET!.replace(/^whsec_/, ''), 'base64');

function verify(id: string, timestamp: string, signatures: string, body: Buffer): boolean {
  if (Math.abs(Date.now() / 1000 - Number(timestamp)) > 300) return false;
  const expected = crypto.createHmac('sha256', secret).update(`${id}.${timestamp}.`).update(body).digest();
  return signatures.split(' ').some(entry => {
    const [version, value] = entry.split(',', 2);
    const received = Buffer.from(value ?? '', 'base64');
    return version === 'v1' && received.length === expected.length && crypto.timingSafeEqual(received, expected);
  });
}

const app = express();
app.post('/webhooks/restauspace', express.raw({type: 'application/json'}), async (req, res) => {
  const id = req.header('webhook-id') ?? '';
  if (!verify(id, req.header('webhook-timestamp') ?? '', req.header('webhook-signature') ?? '', req.body)) return res.sendStatus(401);
  if (await alreadyProcessed(id)) return res.sendStatus(200);
  const event = JSON.parse(req.body.toString('utf8'));
  await queue.push(event); // traiter plus tard, répondre vite
  await markProcessed(id);
  res.sendStatus(200);
});
```

```kotlin
// JVM (Ktor, Spring…).
import java.security.MessageDigest
import java.util.Base64
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

private val key = Base64.getDecoder().decode(System.getenv("RESTAUSPACE_WEBHOOK_SECRET").removePrefix("whsec_"))

fun verify(id: String, timestamp: String, signatures: String, body: ByteArray): Boolean {
    val sentAt = timestamp.toLongOrNull() ?: return false
    if (kotlin.math.abs(System.currentTimeMillis() / 1000 - sentAt) > 300) return false
    val mac = Mac.getInstance("HmacSHA256").apply { init(SecretKeySpec(key, "HmacSHA256")) }
    mac.update("$id.$timestamp.".toByteArray(Charsets.UTF_8))
    val expected = mac.doFinal(body)
    return signatures.split(' ').any { entry ->
        val parts = entry.split(',', limit = 2)
        parts.size == 2 && parts[0] == "v1" &&
            runCatching { MessageDigest.isEqual(Base64.getDecoder().decode(parts[1]), expected) }.getOrDefault(false)
    }
}
```

```dart
// Serveur Dart (shelf) ; package:crypto.
import 'dart:convert';
import 'dart:io';
import 'package:crypto/crypto.dart';

final key = base64.decode(Platform.environment['RESTAUSPACE_WEBHOOK_SECRET']!.replaceFirst('whsec_', ''));

bool verify(String id, String timestamp, String signatures, List<int> body) {
  final sentAt = int.tryParse(timestamp);
  if (sentAt == null || (DateTime.now().millisecondsSinceEpoch ~/ 1000 - sentAt).abs() > 300) return false;
  final expected = Hmac(sha256, key).convert([...utf8.encode('$id.$timestamp.'), ...body]).bytes;
  return signatures.split(' ').any((entry) {
    final parts = entry.split(',');
    if (parts.length != 2 || parts[0] != 'v1') return false;
    final received = base64.decode(parts[1]);
    if (received.length != expected.length) return false;
    var diff = 0;
    for (var i = 0; i < expected.length; i++) {
      diff |= received[i] ^ expected[i];
    }
    return diff == 0;
  });
}
```

```swift
// Serveur Swift (Vapor) ; CryptoKit (ou swift-crypto sous Linux).
import Crypto
import Foundation

let key = SymmetricKey(data: Data(base64Encoded: String(webhookSecret.dropFirst("whsec_".count)))!)

func verify(id: String, timestamp: String, signatures: String, body: Data) -> Bool {
    guard let sentAt = TimeInterval(timestamp), abs(Date().timeIntervalSince1970 - sentAt) <= 300 else { return false }
    var signed = Data("\(id).\(timestamp).".utf8)
    signed.append(body)
    return signatures.split(separator: " ").contains { entry in
        let parts = entry.split(separator: ",", maxSplits: 1)
        guard parts.count == 2, parts[0] == "v1", let received = Data(base64Encoded: String(parts[1])) else { return false }
        return HMAC<SHA256>.isValidAuthenticationCode(received, authenticating: signed, using: key)
    }
}
```

Pour tester votre vérification en ligne de commande :

```bash
SECRET_B64=${RESTAUSPACE_WEBHOOK_SECRET#whsec_}
KEY_HEX=$(printf '%s' "$SECRET_B64" | base64 -d | od -An -vtx1 | tr -d ' \n')
printf '%s.%s.%s' "$WEBHOOK_ID" "$WEBHOOK_TIMESTAMP" "$(cat body.json)" \
  | openssl dgst -sha256 -mac HMAC -macopt hexkey:$KEY_HEX -binary | base64
```

## Répondre

- Répondez **`2xx` en moins de 10 secondes**. Mettez le traitement en file et répondez tout de suite.
- Toute autre réponse (y compris `3xx` et `4xx`), un dépassement de délai ou une erreur réseau est un échec. Les
  redirections ne sont pas suivies.
- Le corps de votre réponse est ignoré.

## Nouveaux essais et désactivation

| Essai | Délai après l'échec précédent |
|---|---|
| 1 | immédiat |
| 2 | 30 s |
| 3 | 1 min |
| 4 | 2 min |
| 5 | 4 min |
| 6 | 8 min |

Après 6 échecs, la livraison est **abandonnée** ; vous pouvez la renvoyer depuis la console (même `webhook-id`, même
contenu). Après **5 livraisons abandonnées d'affilée**, le webhook est désactivé et les responsables du restaurant
sont prévenus. Tant qu'il est désactivé, les nouveaux événements ne lui sont pas envoyés et ne sont pas mis de côté :
après la réactivation, resynchronisez-vous par l'API (par exemple `GET /v1/orders` depuis la dernière commande connue).

L'historique des livraisons est gardé 30 jours.

## Garanties

- **Au moins une fois** : un même événement peut arriver plusieurs fois. Dédupliquez sur `webhook-id`.
- **Pas d'ordre garanti** : `orders.order_completed` peut arriver avant `orders.order_placed` après un nouvel essai.
  Fiez-vous à l'état lu par l'API, ou au champ `timestamp`, plutôt qu'à l'ordre d'arrivée.
- Une adresse qui se résout vers un réseau privé est refusée à l'envoi.

## Régénérer le secret

Console › Intégrations › Webhooks › Régénérer le secret. Pendant 24 heures, chaque envoi est signé avec le nouveau
**et** l'ancien secret : déployez le nouveau secret à votre rythme. L'option « Invalider l'ancien secret
immédiatement » coupe cette période (en cas de fuite).
