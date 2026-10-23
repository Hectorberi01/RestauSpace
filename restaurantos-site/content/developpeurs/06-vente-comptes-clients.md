# Comptes clients et fidélité

> Connexion sans mot de passe par code reçu (SMS, WhatsApp ou e-mail), historique des commandes et récompenses de
> fidélité.

Les comptes clients existent pour les sites et les applications mobiles, pas pour les bornes
(`storefront.customer.kiosk_not_allowed`).

## Connexion

### 1. Demander un code : `POST /customer/code`

```json
{"channel": "Sms", "address": "+2290197000000", "language": "fr"}
```

- `channel` : `Sms`, `WhatsApp` ou `Email` ; `address` : téléphone international pour `Sms` / `WhatsApp`, e-mail pour
  `Email`.
- `X-Human-Check` si l'application l'exige.
- Réponse identique que le contact soit connu ou non (on ne révèle pas qui est client).

```json
{"ticket": "Q2hp…", "expiresAtUtc": "2026-10-14T12:15:00+00:00", "channel": "Sms", "destination": "••• 00"}
```

Le code a 6 chiffres, vaut 10 minutes et accepte 5 essais. Un nouveau code par minute au plus, 5 par heure pour une
même adresse (`429 notifications.otp.too_many_requests` avec `Retry-After`). Gardez `ticket` en mémoire, pas sur
disque.

### 2. Vérifier le code : `POST /customer/code/verify`

```json
{"ticket": "Q2hp…", "code": "482913", "marketingConsent": false, "language": "fr"}
```

Client existant :

```json
{"status": "SignedIn", "token": "cst_V2VsY29tZS…", "expiresAtUtc": "2026-12-13T12:06:00+00:00", "ticket": null,
 "account": {"name": "Afi", "email": null, "phone": "+2290197000000", "currencyCode": "XOF", "loyalty": {"…": "…"}}}
```

Nouveau client :

```json
{"status": "NameRequired", "token": null, "expiresAtUtc": null, "ticket": "TmV3…", "account": null}
```

Demandez alors son nom et renvoyez **le nouveau ticket** avec `name` (sans code) :

```json
{"ticket": "TmV3…", "name": "Afi", "marketingConsent": true, "language": "fr"}
```

### 3. Utiliser la session

Envoyez `X-Customer-Token: cst_…` sur toutes les requêtes suivantes. La session vaut 60 jours, sans renouvellement :
à l'expiration, le client se reconnecte. Rangez le jeton dans le stockage sécurisé de l'appareil (Keychain, Keystore,
`flutter_secure_storage`).

Un jeton invalide ou expiré **n'est pas une erreur** sur les routes publiques : la requête est traitée comme celle
d'un invité. Seules les routes du compte (et `rewardId`) répondent `403 storefront.customer.sign_in_required` : oubliez
alors le jeton et proposez de se reconnecter.

### 4. Se déconnecter : `POST /customer/sign-out`

Réponse `204`, même sans session. Le jeton cesse aussitôt de fonctionner ; effacez-le de l'appareil.

## Le compte : `GET /customer`

```json
{
  "name": "Afi", "email": null, "phone": "+2290197000000", "currencyCode": "XOF",
  "loyalty": {
    "enabled": true, "balance": 120, "pointsPerCurrencyUnit": 0.01,
    "rewards": [{"id": "0199…", "name": "Boisson offerte", "pointCost": 100, "discount": 500, "affordable": true}]
  }
}
```

- `pointsPerCurrencyUnit` : points gagnés par unité de devise (0,01 = 1 point pour 100 F CFA).
- `rewards` : les récompenses actives, triées par coût ; `affordable` dit si le solde suffit.

## Historique : `GET /customer/orders?take=20`

Les dernières commandes du client (1 à 50), dans les établissements servis par l'application :

```json
[{"orderId": "0199…", "number": "A-1042", "locationId": "0199…", "locationName": "Chez Awa Cotonou", "mode": "Takeaway",
  "status": "Completed", "currencyCode": "XOF", "total": 8740, "createdAtUtc": "2026-10-14T12:05:00+00:00", "trackingToken": "pZ8xQ…"}]
```

`trackingToken` est présent pour les commandes passées en ligne : il ouvre le suivi (et le questionnaire de
satisfaction).

## Utiliser une récompense

1. `GET /customer` → choisir une récompense `affordable`.
2. Devis avec `rewardId` → `reward.discount` est déduit de `total`.
3. Commande avec le même `rewardId` : les points sont débités à la commande et rendus si elle est annulée.

Refus : `storefront.checkout.reward_unavailable`, `storefront.checkout.reward_not_affordable`,
`loyalty.account.insufficient_balance`.

## Commander connecté

Avec `X-Customer-Token`, `customer` devient facultatif dans `POST /orders` : le nom et les contacts du profil sont
utilisés. Seuls `customer.phone` (téléphone de livraison) et `customer.marketingConsent` sont lus.
