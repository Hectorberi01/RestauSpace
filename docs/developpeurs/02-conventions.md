# Conventions communes

> Les règles valables pour les deux API : format des données, erreurs, idempotence, limites de débit, cache et
> en-têtes.

## Adresses et versions

- API de vente en ligne : `{{API_URL}}/v1/storefront/…`
- API d'intégration : `{{API_URL}}/v1/…`
- La version est dans le chemin (`v1`). Une évolution compatible (nouveau champ, nouvelle valeur d'énumération, nouvelle
  route) ne change pas de version : **ignorez les champs inconnus** et prévoyez un affichage par défaut pour une valeur
  d'énumération inconnue.
- Toujours en HTTPS. Envoyez `Accept: application/json` : sans lui, un client qui n'accepte que `text/html` reçoit les
  erreurs sans corps.

## Format des données

| Donnée | Format | Exemple |
|---|---|---|
| Noms des champs | camelCase | `trackingToken` |
| Identifiants | GUID en texte | `"0199a3c4-5b1e-7c2d-9f00-1a2b3c4d5e6f"` |
| Montants | Nombre JSON en **unités de la devise** (jamais en centimes), avec le code de devise ISO 4217 à côté | `"total": 7000, "currencyCode": "XOF"` = 7 000 F CFA ; `12.5` en `EUR` = 12,50 € |
| Instants | ISO 8601 avec décalage ; les champs `…Utc` sont en UTC et sortent avec `+00:00` | `"2026-10-14T12:30:00+00:00"` |
| Dates seules | `AAAA-MM-JJ`, date locale de l'établissement | `"2026-10-14"` |
| Heures d'ouverture | `"HH:mm"` locale, avec le fuseau IANA de l'établissement (`timeZoneId`) | `"11:30"`, `"Africa/Porto-Novo"` |
| Énumérations | Leur nom en texte, PascalCase | `"Takeaway"`, `"MobileMoney"` |
| Absence de valeur | `null` (les champs ne sont jamais omis) | `"scheduledForUtc": null` |

En entrée :

- une énumération dans le **corps** accepte n'importe quelle casse (`"takeaway"`) ; dans l'**adresse** (`?mode=`), elle
  doit avoir la casse exacte (`Takeaway`) ;
- un instant accepte tout décalage (`Z`, `+01:00`) ;
- un montant avec plus de décimales que la devise n'en permet est refusé (`common.money.invalid_precision`).

Les prix sont TTC quand `pricesIncludeTax` vaut `true` (cas général), sinon hors taxe ; le devis donne `taxTotal`.

## Erreurs

Les erreurs métier suivent la RFC 9457 (`application/problem+json`) avec un champ **`code`** stable :

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "Business rule violated",
  "status": 422,
  "detail": "The cart is empty.",
  "code": "storefront.checkout.cart_empty",
  "traceId": "00-32ecd3dfa9d84c6cc4926182e0cf65d6-1934ba4fd3a68c6a-00"
}
```

| Statut | Sens | Corps |
|---|---|---|
| `400` | JSON mal formé, valeur illisible dans l'adresse, énumération inconnue | vide |
| `401` | Clé absente, inconnue ou régénérée, application désactivée, restaurant suspendu, origine non déclarée, appareil de borne invalide ou révoqué | vide |
| `403` | Droit manquant (clé d'API sans le scope, route fermée aux clés) | vide |
| `403` | Règle d'accès métier (ex. `storefront.customer.sign_in_required`) | problem details |
| `404` | Ressource introuvable **ou** d'un autre restaurant (même réponse) | problem details |
| `409` | Conflit d'état (prix changé, créneau complet, commande déjà acceptée, envoi en cours avec la même clé d'idempotence) | problem details |
| `422` | Règle métier refusée | problem details |
| `429` | Trop de requêtes ; attendre `Retry-After` secondes | vide (limite de débit) ou problem details (codes de connexion) |
| `5xx` | Incident de la plateforme ; réessayer plus tard, avec la même clé d'idempotence | variable |

- `detail` est en anglais et peut changer : **affichez un texte choisi d'après `code`**, avec un texte générique pour
  un code inconnu. Liste complète : [Codes d'erreur](12-reference-erreurs.md).
- Communiquez le `traceId` au support pour toute demande d'aide.
- Un `404` ne dit jamais si la ressource existe chez un autre restaurant.

## Idempotence

Un `POST` qui crée quelque chose (commande, paiement, réservation, remboursement…) porte l'en-tête
`Idempotency-Key` : une valeur unique par **intention** (un UUID v4 convient), de 1 à 100 caractères ASCII visibles.

- Il est **obligatoire** sur `POST /storefront/orders`, `POST /storefront/orders/{jeton}/payments`,
  `POST /storefront/reservations`, et côté intégration sur `POST /payments/offline`, `/payments/online`,
  `/payments/{id}/retry`, `/payments/{id}/refunds`, `/payments/{id}/refunds/store-credit`, l'ouverture d'une caisse et
  ses mouvements. Il est accepté sur tout autre `POST`.
- Même clé, même requête, après une réponse : la réponse d'origine est rejouée, avec l'en-tête
  `Idempotency-Replayed: true`. Rien n'est créé une deuxième fois.
- Même clé pendant que la première requête est en cours : `409 idempotency.request_in_progress`.
- Même clé, autre contenu : `422 idempotency.key_reused`.
- Après un refus métier (réponse avec un `code`), la clé est libérée : corrigez puis renvoyez avec la même clé. Un
  `400` (JSON mal formé) reste attaché à la clé : après avoir corrigé une requête mal formée, prenez une nouvelle clé.
- Les clés sont gardées 24 heures. Elles sont partagées par tous les appareils d'une même application : **générez-les
  aléatoirement**, jamais à partir d'un compteur.

Règle pratique : générez la clé quand le client appuie sur « Commander », gardez-la tant que vous réessayez cet envoi,
et prenez-en une nouvelle si le panier change.

## Limites de débit

| API | Limite | Par |
|---|---|---|
| Vente en ligne | 120 requêtes par minute | application et adresse IP |
| Vente en ligne : `POST /customer/code` et `/customer/code/verify` | 30 requêtes par 10 minutes | application et adresse IP |
| Codes de connexion | 1 code par minute et 5 par heure pour une même adresse | adresse e-mail ou téléphone |
| Intégration | 600 requêtes par minute (fenêtre glissante) | clé d'API |
| Images `/v1/media/…` | 600 requêtes par minute | adresse IP |

Au-delà : `429` avec `Retry-After` (en secondes). Espacez les interrogations (suivi de commande : toutes les 5 à 10
secondes, seulement quand l'écran est visible) et mettez la carte en cache.

## Cache de la carte

`GET /storefront/locations/{id}/catalog` renvoie un `ETag`. Renvoyez-le dans `If-None-Match` : si la carte n'a pas
changé, la réponse est `304` sans corps. Le champ `version` de la carte contient la même valeur.

## Navigateurs (CORS)

- Méthodes : `GET`, `POST`.
- En-têtes acceptés : `X-Storefront-Key`, `X-Kiosk-Device`, `X-Customer-Token`, `X-Human-Check`, `Content-Type`,
  `Idempotency-Key`, `Accept-Language`, `If-None-Match`.
- En-têtes lisibles : `Retry-After`, `Idempotency-Replayed`, `ETag`, `Location`.
- Sans cookie : l'authentification passe uniquement par les en-têtes.
- L'origine de la page doit être déclarée sur l'application (sinon `401`). L'API d'intégration n'est pas appelable
  depuis un navigateur.

## Langue

Les noms de produits, de catégories et d'établissements sont renvoyés tels que le restaurant les a saisis. Les textes
d'erreur sont en anglais : traduisez par `code`. La langue du client (`fr`, `en`, `es`, `pt`, `de`, `it`, `nl`, `ar`) se
passe dans les champs `language` des demandes de code de connexion et des coordonnées de commande : elle sert aux SMS,
e-mails et messages WhatsApp envoyés au client. `Accept-Language` est accepté mais n'est pas utilisé.

## Vérification anti-robot

Quand l'application l'exige, `GET /storefront/` renvoie `app.humanCheckSiteKey`. Affichez le widget Cloudflare
Turnstile avec cette clé de site, puis envoyez le jeton obtenu dans `X-Human-Check` sur `POST /orders`,
`POST /reservations` et `POST /customer/code`. Un jeton ne sert qu'une fois. Refus : `422 storefront.human_check.failed`
(redemandez un jeton et renvoyez avec la même clé d'idempotence). Une borne appairée n'est jamais soumise à cette
vérification. Pour une application mobile native, laissez l'option décochée.

## Pagination (API d'intégration)

Les listes paginées renvoient :

```json
{"items": [], "page": 1, "pageSize": 20, "totalCount": 134, "hasMore": true}
```

- `page` commence à 1 ; `pageSize` vaut au plus 100.
- Certaines listes ne comptent pas le total (`totalCount: null`) : continuez tant que `hasMore` vaut `true` (la dernière
  page peut être vide).

## Sécurité et données personnelles

- Le restaurant (et donc les données accessibles) vient toujours de la clé, jamais d'un paramètre.
- Un identifiant d'établissement est toujours vérifié : il doit appartenir au restaurant et être servi par
  l'application.
- N'écrivez jamais dans vos journaux : clés, jetons, codes de connexion reçus par le client, noms, téléphones, e-mails,
  adresses.
- Ne stockez jamais de données de carte bancaire : le paiement se fait chez le prestataire.
