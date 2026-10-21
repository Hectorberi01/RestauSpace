# Borne de commande

> Une borne en libre-service : appairage à un établissement, commande sans compte client, ticket imprimé par le
> restaurant.

## Principe

1. Le restaurant crée une application de type **Borne** (Console › Paramètres › Vente en ligne).
2. La borne, avec cette clé, demande un **code d'appairage** à 6 chiffres et l'affiche.
3. Un membre du personnel saisit le code dans la console (Vente en ligne › Bornes) et choisit l'établissement.
4. La borne récupère son **jeton d'appareil** `kd_…` et l'envoie dans `X-Kiosk-Device` sur toutes ses requêtes.

Une borne appairée ne voit et ne commande que **son** établissement. Le restaurant peut révoquer un appareil à tout
moment : toutes ses requêtes répondent alors `401` ; revenez à l'écran d'appairage.

Si la borne est une page web (navigateur en mode kiosque), déclarez son adresse dans l'application (sinon `401`).

## Appairage

### Demander un code : `POST /kiosk/pairings`

Sans corps, avec la clé de l'application Borne (pas encore de `X-Kiosk-Device`).

```json
{"pairingId": "0199…", "code": "482913", "secret": "c2VjcmV0LWRl…", "expiresAtUtc": "2026-10-14T12:10:00+00:00"}
```

Affichez `code` en grand ; gardez `secret` en mémoire. Le code vaut 10 minutes.

### Attendre la validation : `POST /kiosk/pairings/{pairingId}/claim`

```json
{"secret": "c2VjcmV0LWRl…"}
```

Interrogez toutes les 3 secondes :

- `{"state": "Pending"}` : pas encore validé ;
- `{"state": "Paired", "deviceToken": "kd_…", "locationId": "0199…", "deviceName": "Borne entrée"}` : **une seule fois** ;
  enregistrez `deviceToken` dans le stockage protégé de l'appareil ;
- `{"state": "Expired"}` : code expiré (ou déjà utilisé) ; demandez-en un nouveau.

```ts
async function pair(): Promise<string> {
  const pairing = await storefront.post('/kiosk/pairings');
  showPairingCode(pairing.code);
  for (;;) {
    await new Promise(resolve => setTimeout(resolve, 3000));
    const state = await storefront.post(`/kiosk/pairings/${pairing.pairingId}/claim`, {secret: pairing.secret});
    if (state.state === 'Paired') return state.deviceToken;
    if (state.state === 'Expired') return pair();
  }
}
```

## Commander depuis la borne

Toutes les routes de la vente en ligne s'utilisent comme sur un site, avec `X-Kiosk-Device`, sauf :

- pas de compte client, pas de coordonnées (`customer` est ignoré), pas de réservation ;
- pas de livraison : `DineIn` ou `Takeaway` ;
- pas de vérification anti-robot ;
- sans jeton d'appareil, la plupart des routes répondent `422 storefront.kiosk.device_required`.

## Ticket imprimé

L'impression est faite **par la plateforme**, sur l'imprimante de tickets de l'établissement ; la borne n'a rien à
imprimer.

- Paiement au comptoir (`"payment": "Counter"`) : le ticket « À PAYER AU COMPTOIR » part à la commande ; la réponse
  dit `ticketPrinted: true` s'il a bien été envoyé. Sinon, affichez le numéro de commande en grand.
- Paiement en ligne : affichez `checkoutUrl` en QR code (`returnUrl: null`) ; le ticket « PAYÉE » s'imprime quand le
  paiement est confirmé.

Le ticket ne contient aucune donnée personnelle.

## Bonnes pratiques

- Revenez à l'accueil après 60 à 90 secondes d'inactivité et videz le panier.
- Rechargez la carte avec `If-None-Match` à chaque retour à l'accueil.
- Gardez l'écran « Commande n° A-1042 » assez longtemps pour être lu (10 à 15 secondes).
- Ne mettez jamais la clé ou le jeton d'appareil dans l'adresse de la page.
