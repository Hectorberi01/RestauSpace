# Prestataires de paiement en ligne

Date : 11 octobre 2026.

## Principe

- La plateforme propose les prestataires, et chaque restaurateur active les siens dans **Paramètres › Moyens de paiement**.
- Le restaurateur encaisse sur **son propre compte marchand** : il saisit ses identifiants, qui sont chiffrés au coffre et jamais réaffichés.
- Au démarrage, l'API ajoute au catalogue les six prestataires intégrés s'ils n'y sont pas déjà. La plateforme peut ensuite les retirer ou les modifier dans le super-admin.

| Code | Prestataire | Moyens | Devises | Paiement | Notification | Remboursement par l'API |
|---|---|---|---|---|---|---|
| `fedapay` | FedaPay | mobile money, carte | XOF, GNF | page FedaPay | webhook déclaré dans FedaPay, signé, puis relecture | non (tableau de bord) |
| `kkiapay` | KKiaPay | mobile money, carte | XOF | widget KKiaPay | webhook déclaré dans KKiaPay (`x-kkiapay-secret`), puis relecture | non (tableau de bord) |
| `cinetpay` | CinetPay | mobile money, carte | XOF, XAF, GNF, CDF | page CinetPay | adresse transmise à chaque paiement, puis vérification | non (tableau de bord) |
| `mtn_momo` | MTN Mobile Money | mobile money | XOF, XAF, GNF… | demande poussée sur le téléphone du client | adresse de rappel transmise à chaque paiement, puis relecture | non (portail MTN) |
| `stripe` | Stripe | carte, Apple Pay, Google Pay | EUR, USD, GBP, CHF, CAD, XOF | Stripe Checkout | webhook déclaré dans Stripe, signé | oui |
| `paypal` | PayPal | PayPal | EUR, USD, GBP, CHF, CAD | page PayPal | webhook déclaré dans PayPal, vérifié par PayPal ; capture à l'approbation | oui |

Quand la notification n'est pas signée (MTN, CinetPay), ou en plus de la signature (FedaPay, KKiaPay), la transaction est relue chez le prestataire avec les identifiants du restaurant : c'est le prestataire qui fait foi, pas le corps reçu.

## Identifiants saisis par le restaurateur

- **FedaPay** :
  - clé secrète (`sk_sandbox_…` ou `sk_live_…` : l'environnement en découle) ;
  - clé secrète du webhook.
- **KKiaPay** :
  - clés publique, privée et secrète ;
  - secret du webhook ;
  - environnement.
- **CinetPay** : clé API et identifiant de site.
- **MTN MoMo** :
  - utilisateur et clé d'API ;
  - clé d'abonnement Collection ;
  - environnement cible (`sandbox`, `mtnbenin`, `mtnivorycoast`…).
- **Stripe** : clé secrète, secret de signature du webhook (`whsec_…`).
- **PayPal** :
  - Client ID et secret ;
  - ID du webhook ;
  - environnement.

Pour FedaPay, KKiaPay, Stripe et PayPal, la fenêtre des identifiants affiche l'adresse du webhook à déclarer chez le prestataire : `{API}/v1/payments/webhooks/{code}/{configuration}`.

## Configuration de l'hôte

- `Payments:PublicApiBaseUrl` :
  - adresse publique de l'API ;
  - obligatoire pour CinetPay, et pour le rappel de MTN (HTTPS seulement).
- `Payments:CheckoutReturnUrl` :
  - page où le client revient après avoir payé ;
  - obligatoire pour Stripe et PayPal.
- En local, les prestataires ne peuvent pas joindre `localhost` : utiliser un tunnel (ngrok, Cloudflare Tunnel) et le déclarer comme `PublicApiBaseUrl`.

## Tester un compte (restaurateur)

Le restaurateur vérifie ses configurations depuis la console, sans rien encaisser ni envoyer de contenu réel.

### Paiements

- **Où** : page de configuration du moyen (**Paramètres › Moyens de paiement › Configurer**), bouton **Tester la connexion**.
- **Route** : `POST /v1/payments/configurations/{id}/test` (permission `payments.configure`, 10 essais par 10 minutes et par utilisateur).
- **Appel sans effet chez le prestataire** :

| Prestataire | Appel | Identifiants refusés si |
|---|---|---|
| Stripe | `GET /v1/balance` | 401 ou 403 |
| FedaPay | `GET /transactions?per_page=1` | 401 ou 403 |
| PayPal | jeton OAuth, puis lecture du webhook déclaré | 401 ou 403 ; webhook inconnu signalé |
| MTN MoMo | jeton Collection | 401 ou 403 |
| CinetPay | vérification d'une transaction inexistante | code 606, 609 ou 613 |
| KKiaPay | statut d'une transaction inexistante | 401 ou 403 |

- **Réponse** : `outcome` (`verified`, `credentials_rejected`, `credentials_missing`, `credentials_incomplete`, `credentials_unreadable`, `provider_unavailable`, `provider_error`, `not_verifiable`), `isSandbox`, `providerStatus`, `warnings`.
- **Avertissements** : `checkout_return_url_missing`, `public_api_url_missing`, `public_api_url_not_https`, `webhook_id_unknown`.
- Le secret de webhook (FedaPay, KKiaPay, Stripe) ne se vérifie qu'à la réception d'une notification signée.

### Notifications

- **Où** : **Paramètres › Notifications**, bouton **Tester** sur un prestataire configuré (email, SMS, WhatsApp). La fenêtre s'ouvre aussi après l'enregistrement des identifiants.
- **Route** : `POST /v1/notification-configurations/{id}/test` avec `{ "address": "…" }` (permission `notifications.configure`, 10 messages par 10 minutes et par utilisateur).
- Le message a un contenu fixe. Il n'est pas enregistré dans l'historique et ne compte pas dans le quota.
- **Réponse** : `outcome` (`sent`, `delivered`, `failed`, `credentials_missing`, `credentials_invalid`), `failureCode` (par exemple `PROVIDER_AUTH_FAILED`, `SENDER_REJECTED`, `TEMPLATE_INVALID`, `MJ_…`).

Aucune réponse ne contient d'identifiant ni le message brut du prestataire.

### À vérifier en sandbox

- **KKiaPay** : le statut renvoyé pour une transaction inconnue (400 ou 404 attendu).
- **CinetPay** : les codes renvoyés pour une clé ou un site invalide.

## Encaissement en caisse

- **MTN MoMo** : le caissier saisit le numéro du client, qui n'est jamais conservé. Une relance demande le numéro à nouveau.
- **Autres prestataires** : un lien de paiement à copier ou à ouvrir.

## Points à vérifier en sandbox avant la production

- **KKiaPay** : le lien du widget hébergé (`widget-v3.kkiapay.me` avec `partnerId`) et le champ `partnerId` renvoyé par l'API de statut.
- **FedaPay** : la forme de la réponse (`v1/transaction` ou à plat), qui est gérée dans les deux cas.
- **CinetPay** : le montant doit être un multiple de 5 en XOF et XAF.
- **MTN** : en sandbox, la devise acceptée est EUR.
