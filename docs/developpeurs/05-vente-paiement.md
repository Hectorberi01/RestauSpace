# Paiement en ligne

> Faire payer une commande par Mobile Money, carte, Apple Pay, Google Pay ou PayPal, sans jamais toucher aux données
> bancaires.

Le paiement se fait **chez le prestataire** que le restaurant a configuré (FedaPay, KkiaPay, CinetPay, MTN MoMo, Wave,
Stripe, PayPal…). Votre interface ne voit jamais de numéro de carte : elle redirige vers une page de paiement, ou
demande au client de valider sur son téléphone.

## Déroulé

1. Devis : `canPayOnline` et `onlinePaymentOptions` disent quels prestataires et moyens proposer.
2. Commande avec `"payment": "Online"` → `trackingToken`, `payBeforeUtc`.
3. `POST /orders/{trackingToken}/payments` → `checkoutUrl` (redirection) ou `null` (validation sur le téléphone).
4. Le client paie ; le prestataire le renvoie sur votre page de retour avec `?paiement=<paymentId>`.
5. Interrogez `GET /orders/{trackingToken}` jusqu'à `paymentState` = `Paid`.

La confirmation vient **toujours** du serveur (le prestataire prévient la plateforme directement) : ne considérez jamais
la commande payée sur la seule foi du retour du navigateur.

## Démarrer le paiement : `POST /orders/{trackingToken}/payments`

En-têtes : `X-Storefront-Key`, `Idempotency-Key` (obligatoire, une nouvelle valeur pour chaque nouvel essai voulu par
le client).

```json
{
  "provider": "fedapay",
  "method": "MobileMoney",
  "payerPhone": "+22997000000",
  "returnUrl": "https://chezawa.bj/commande?jeton=pZ8xQ…"
}
```

| Champ | Règle |
|---|---|
| `provider` | Un code de `onlinePaymentOptions` (devis) ou de `paymentOptions` (suivi). |
| `method` | Un des `methods` du prestataire ; obligatoire s'il en propose plusieurs. |
| `payerPhone` | Numéro au format international, pour les prestataires qui envoient la demande sur le téléphone (MTN MoMo, certains Mobile Money). |
| `returnUrl` | Une page de retour **déclarée** sur l'application (comparée sans `?…`). Pour une application mobile : un lien universel (`https://…`) qui rouvre l'application. `null` : page de retour par défaut de la plateforme. |

Réponse `200` :

```json
{"paymentId": "0199…", "checkoutUrl": "https://checkout.fedapay.com/…"}
```

- `checkoutUrl` présent : ouvrez-le (redirection sur le web ; `ASWebAuthenticationSession` / `SFSafariViewController` sur
  iOS ; Custom Tabs sur Android ; QR code à scanner sur une borne).
- `checkoutUrl` = `null` : la demande est partie sur le téléphone du client ; affichez « Validez le paiement sur votre
  téléphone » et interrogez le suivi.

Un nouvel appel annule les essais précédents non aboutis : un seul lien reste valable.

## Retour et confirmation

Le prestataire renvoie le client sur `returnUrl` en ajoutant `paiement=<paymentId>` (`?paiement=…` ou `&paiement=…`).
Sur cette page :

```ts
async function waitForPayment(trackingToken: string, timeoutMs = 120_000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    const order = await storefront.get(`/orders/${encodeURIComponent(trackingToken)}`);
    if (order.paymentState === 'Paid') return 'paid';
    if (order.status === 'Cancelled') return 'cancelled';
    if (order.paymentState === 'AwaitingPayment' && order.canPay) return 'retry'; // échec : proposer de réessayer
    await new Promise(resolve => setTimeout(resolve, 3000));
  }
  return 'pending';
}
```

- `Processing` : le prestataire n'a pas encore confirmé ; continuez d'interroger.
- `AwaitingPayment` avec `canPay: true` : l'essai a échoué ou a été abandonné ; proposez un nouvel essai
  (`paymentOptions` liste les prestataires).
- Après `payBeforeUtc`, la commande est annulée automatiquement.
- Une commande payée puis refusée ou annulée fait l'objet d'une demande de remboursement auprès du prestataire :
  `paymentState` passe à `Refunded` quand il est confirmé.

## Refus

| Code | Sens |
|---|---|
| `storefront.payment.not_expected` (409) | La commande n'attend plus de paiement (payée, annulée, délai dépassé, paiement au comptoir). |
| `storefront.payment.return_url_not_allowed` | `returnUrl` n'est pas déclarée sur l'application. |
| `payments.method.payment_method_required` | Le prestataire propose plusieurs moyens : précisez `method`. |
| `payments.method.not_accepted`, `payments.configuration.method_not_accepted` | Ce moyen n'est pas proposé. |
| `payments.configuration.amount_out_of_bounds` | Montant hors des limites du prestataire. |
| `payments.configuration.provider_unavailable`, `disabled`, `not_assigned_to_location` | Prestataire indisponible pour cet établissement : proposez-en un autre. |
| `payments.payer_phone.invalid` | Numéro invalide. |
| `payments.payment.too_many_attempts` | Trop d'essais : proposer le paiement au comptoir ou réessayer plus tard. |
| `payments.provider.call_failed` | Le prestataire ne répond pas : réessayer. |

## Prestataires et moyens

| Code | Moyens | Devises |
|---|---|---|
| `fedapay` | MobileMoney, Card | XOF, GNF |
| `kkiapay` | MobileMoney, Card | XOF |
| `cinetpay` | MobileMoney, Card | XOF, XAF, GNF, CDF |
| `mtn_momo` | MobileMoney (demande envoyée sur le téléphone, sans page de paiement) | selon le pays |
| `wave` | MobileMoney | selon le pays |
| `stripe` | Card, ApplePay, GooglePay | EUR, USD, GBP, CHF, CAD, XOF |
| `paypal` | PayPal | selon le compte |
| `sandbox` | Tous, sans argent réel | restaurant de démonstration |

La liste exacte proposée pour une commande dépend du restaurant, de l'établissement, de la devise et du montant : fiez-vous
toujours à `onlinePaymentOptions` / `paymentOptions`.

## Borne

Sur une borne, envoyez `returnUrl: null` et affichez `checkoutUrl` sous forme de QR code : le client paie avec son
téléphone. Le ticket « PAYÉE » s'imprime quand le paiement est confirmé (voir [Borne](08-vente-borne.md)).
