# Codes d'erreur et référence des routes

> Les codes d'erreur stables, avec un message à afficher au client, et la liste complète des routes de la vente en
> ligne.

Affichez toujours un message choisi d'après `code`, et un message général pour un code inconnu (« La demande n'a pas
abouti. Réessayez. »). Les messages proposés ci-dessous, en français et en anglais, sont aussi disponibles en JSON :
[`erreurs.json`](erreurs.json) (`{"fr": {code: texte}, "en": {code: texte}}`).

```ts
import messages from './erreurs.json';

export function errorText(code: string | null, status: number, lang: 'fr' | 'en' = 'fr') {
  if (code && messages[lang][code]) return messages[lang][code];
  if (status === 429) return lang === 'fr' ? 'Trop de demandes : réessayez dans un instant.' : 'Too many requests: try again in a moment.';
  if (status === 0) return lang === 'fr' ? 'Le service est injoignable. Vérifiez votre connexion.' : 'The service is unreachable. Check your connection.';
  return lang === 'fr' ? 'La demande n’a pas abouti. Réessayez.' : 'The request failed. Please try again.';
}
```

## Vente en ligne : accès et établissements

| Code | Statut | Cause | Message proposé |
|---|---|---|---|
| `storefront.location.not_found` | 404 | Établissement inconnu, non servi par l'application, ou pas celui de la borne | Cet établissement n’est pas disponible. |
| `storefront.human_check.failed` | 422 | Jeton anti-robot absent ou refusé | Le contrôle anti-robot n’a pas abouti. Réessayez. |
| `storefront.kiosk.device_required` | 422 | Clé de borne sans jeton d'appareil | Cette borne n’est pas appairée. |
| `storefront.kiosk.not_a_kiosk_app` | 422 | Appairage demandé avec une clé qui n'est pas celle d'une borne | — |
| `storefront.kiosk.pairing_invalid` | 422 | Appairage impossible (code expiré) : demander un nouveau code | Le code a expiré : un nouveau code s’affiche. |

## Vente en ligne : panier et commande

| Code | Statut | Cause | Message proposé |
|---|---|---|---|
| `storefront.checkout.unavailable` | 422 | Canal (site, application, borne) absent de l'abonnement du restaurant, plan ou option | Le restaurant ne prend pas de commandes en ligne pour le moment. |
| `storefront.checkout.paused` | 422 | Commande en ligne en pause dans l'établissement | Les commandes en ligne sont en pause dans cet établissement. |
| `storefront.checkout.closed` | 422 | Établissement fermé (commande « dès que possible ») | L’établissement est fermé : choisissez une heure de retrait. |
| `storefront.checkout.closed_at_schedule` | 422 | Fermé à l'heure choisie | L’établissement est fermé à l’heure choisie. |
| `storefront.checkout.schedule_too_soon` | 422 | Heure plus proche que le délai de préparation | Cette heure est trop proche : laissez le temps de préparer la commande. |
| `storefront.checkout.service_mode_unavailable` | 422 | Mode non proposé (ou pas de livraison active) | Ce mode n’est pas proposé par l’établissement. |
| `storefront.checkout.payment_mode_not_allowed` | 422 | Mode de paiement non autorisé sur ce canal | Ce moyen de paiement n’est pas proposé. |
| `storefront.checkout.online_payment_unavailable` | 422 | Paiement en ligne choisi mais aucun prestataire disponible | Le paiement en ligne n’est pas disponible : payez au comptoir. |
| `storefront.checkout.cart_empty` | 422 | Panier vide | Votre panier est vide. |
| `storefront.checkout.cart_too_large` | 422 | Plus de 50 lignes ou quantité hors 1 à 99 | Votre panier est trop grand (50 lignes de 99 articles au plus). |
| `storefront.checkout.below_minimum` | 422 | Minimum de commande non atteint (hors frais de livraison) | Le minimum de commande n’est pas atteint. |
| `storefront.checkout.counter_limit_exceeded` | 422 | Montant au-dessus du plafond du paiement au comptoir | Ce montant ne peut pas être payé au comptoir : payez en ligne. |
| `storefront.checkout.price_changed` | 409 | `expectedTotal` différent du total recalculé | Les prix ont changé : vérifiez votre panier avant de valider. |
| `storefront.checkout.slot_full` | 409 | Créneau complet | Ce créneau est complet : choisissez une autre heure. |
| `storefront.checkout.customer_required` | 422 | Invité sans nom, ou sans téléphone ni e-mail | Indiquez votre nom et un téléphone ou un e-mail. |
| `storefront.checkout.delivery_zone_required` | 422 | Livraison sans zone | Choisissez votre zone de livraison. |
| `storefront.checkout.delivery_zone_unavailable` | 422 | Zone inconnue ou inactive | L’établissement ne livre plus cette zone. |
| `storefront.checkout.delivery_address_required` | 422 | Livraison sans adresse ou sans téléphone | Indiquez votre nom, votre téléphone et votre adresse de livraison. |
| `storefront.checkout.coupon_invalid` | 422 | Code promo refusé (même code pour tout motif) | Ce code promo ne peut pas être utilisé. |
| `storefront.checkout.too_many_pending_payments` | 409 | Trop de commandes en attente de paiement en ligne (même adresse IP ou même client) | Plusieurs commandes attendent déjà leur paiement : payez-les ou annulez-les avant d’en passer une autre. |
| `storefront.checkout.reward_unavailable` | 422 | Récompense inconnue, inactive ou programme désactivé | Cette récompense n’est plus disponible. |
| `storefront.checkout.reward_not_affordable` | 422 | Pas assez de points | Vous n’avez pas assez de points pour cette récompense. |
| `storefront.order.not_found` | 404 | Jeton de suivi invalide ou d'un autre restaurant | Cette commande est introuvable. |
| `storefront.order.not_cancellable` | 409 | Commande déjà acceptée | Le restaurant a déjà accepté la commande : contactez-le pour l’annuler. |
| `storefront.table.not_found` | 404 | Jeton de table inconnu, table retirée ou non servie | Ce QR code de table n’est plus valable. Demandez au personnel de vous aider. |
| `storefront.table.dine_in_only` | 422 | Commande à table qui n'est pas « sur place » | Une commande passée à table est servie sur place. |
| `storefront.table.out_of_service` | 422 | Table hors service | Cette table n’est pas en service pour le moment. |
| `storefront.table.not_scheduled` | 422 | Commande à table avec une heure programmée | Une commande passée à table est servie tout de suite. |
| `orders.plan.monthly_limit_reached` | 422 | Quota mensuel de commandes de l'abonnement atteint | Le restaurant ne prend plus de commandes en ligne ce mois-ci. |
| `common.plan.over_limit` | 403 | Établissement gelé : au-delà de la limite du plan du restaurant | Cet établissement ne prend pas de commandes ni de réservations en ligne pour le moment. |
| `orders.order.schedule_too_far` | 422 | Heure programmée à plus de 14 jours | Choisissez une heure dans les 14 prochains jours. |
| `orders.product.not_found` | 404 | Produit inconnu | Un produit du panier n’est plus disponible. |
| `catalog.product.not_sellable` | 422 | Produit épuisé ou retiré | Un produit du panier n’est plus disponible. |
| `catalog.product.archived` | 422 | Produit archivé | Un produit du panier n’est plus disponible. |
| `catalog.product.variant_required` | 422 | Déclinaison à choisir | Choisissez une option pour ce produit. |
| `catalog.product.variant_unavailable` | 422 | Déclinaison épuisée | Une variante du panier n’est plus disponible. |
| `catalog.product.variant_not_found` | 422 | Déclinaison inconnue | Une variante du panier n’est plus disponible. |
| `catalog.modifier_group.selection_below_min` | 422 | Pas assez de compléments choisis dans un groupe | Complétez les choix obligatoires du produit. |
| `catalog.modifier_group.selection_above_max` | 422 | Trop de compléments choisis dans un groupe | Trop de choix pour ce produit. |
| `catalog.modifier_group.selection_modifier_unavailable` | 422 | Complément épuisé | Un supplément choisi n’est plus disponible. |
| `delivery.fulfillment.below_minimum_order` | 422 | Minimum de la zone non atteint | Le minimum de commande de votre zone n’est pas atteint. |
| `delivery.fulfillment.delivery_time_invalid` | 422 | Heure de livraison au-delà de 7 jours | L’heure de livraison doit être dans les 7 prochains jours. |
| `customers.customer.contact_required` | 422 | Ni téléphone ni e-mail | Indiquez un téléphone ou un e-mail. |
| `common.phone.invalid` | 422 | Téléphone invalide (format international attendu : +229…) | Le numéro de téléphone n’est pas valide. |
| `common.email.invalid` | 422 | E-mail invalide | L’adresse e-mail n’est pas valide. |
| `loyalty.account.insufficient_balance` | 422 | Solde de points insuffisant | Vous n’avez pas assez de points pour cette récompense. |

## Vente en ligne : paiement

| Code | Statut | Cause | Message proposé |
|---|---|---|---|
| `storefront.payment.not_expected` | 409 | La commande n'attend pas de paiement en ligne | Cette commande n’attend pas de paiement en ligne. |
| `storefront.payment.return_url_not_allowed` | 422 | Page de retour non déclarée | La page de retour n’est pas déclarée pour ce site. |
| `payments.payer_phone.invalid` | 422 | Numéro du payeur invalide | Le numéro de téléphone n’est pas valide. |
| `payments.payment.too_many_attempts` | 422 | Trop d'essais de paiement | Trop d’essais de paiement : payez au comptoir ou réessayez plus tard. |
| `payments.provider.call_failed` | 422 | Le prestataire ne répond pas | Le service de paiement ne répond pas : réessayez. |

## Vente en ligne : comptes clients

| Code | Statut | Cause | Message proposé |
|---|---|---|---|
| `storefront.customer.sign_in_required` | 403 | Session absente ou expirée | Votre session a expiré : reconnectez-vous. |
| `storefront.customer.ticket_invalid` | 422 | Ticket expiré ou d'une autre application | Ce code a expiré : demandez-en un nouveau. |
| `storefront.customer.address_invalid` | 422 | Canal et adresse incompatibles | Indiquez un numéro de téléphone (SMS, WhatsApp) ou une adresse e-mail. |
| `storefront.customer.code_required` | 422 | Code non saisi | Saisissez le code reçu. |
| `storefront.customer.kiosk_not_allowed` | 422 | Connexion depuis une borne | La connexion n’est pas possible sur une borne. |
| `notifications.otp.invalid` | 422 | Code faux | Code incorrect. |
| `notifications.otp.expired` | 422 | Code expiré | Ce code a expiré : demandez-en un nouveau. |
| `notifications.otp.already_used` | 422 | Code déjà utilisé | Ce code a déjà servi : demandez-en un nouveau. |
| `notifications.otp.too_many_attempts` | 422 | Trop d'essais pour ce code | Trop d’essais : demandez un nouveau code. |
| `notifications.otp.too_many_requests` | 429 | Trop de codes demandés (voir `Retry-After`) | Trop de codes demandés : patientez un peu avant de réessayer. |
| `notifications.otp.not_sent` | 422 | Envoi du code impossible | Le code n’a pas pu être envoyé : essayez un autre moyen. |

## Vente en ligne : réservations, avis et questionnaires

| Code | Statut | Cause | Message proposé |
|---|---|---|---|
| `storefront.reservation.not_found` | 404 | Lien de réservation invalide | Ce lien de réservation n’est pas valable. |
| `storefront.reservation.disabled` | 422 | Pas de réservation en ligne dans cet établissement | Cet établissement ne prend pas de réservations en ligne : appelez-le. |
| `storefront.reservation.date_invalid` | 422 | Date passée ou trop lointaine | Choisissez une date dans la période proposée. |
| `storefront.reservation.kiosk_not_allowed` | 422 | Réservation depuis une borne | La réservation n’est pas possible sur une borne. |
| `reservations.online.disabled` | 422 | Réservation en ligne désactivée | Cet établissement ne prend pas de réservations en ligne : appelez-le. |
| `reservations.online.contact_required` | 422 | Téléphone manquant | Indiquez votre téléphone pour recevoir la confirmation. |
| `reservations.guest_contact.name_required` | 422 | Nom manquant | Indiquez votre nom. |
| `reservations.reservation.capacity_insufficient` | 422 | Plus de place | Plus assez de places à cette heure : choisissez-en une autre. |
| `reservations.reservation.location_closed_during_slot` | 422 | Établissement fermé à cette heure | L’établissement est fermé à cette heure : choisissez-en une autre. |
| `reservations.reservation.lead_time_not_respected` | 422 | Heure trop proche | Cette heure est trop proche : choisissez-en une plus tard. |
| `reservations.reservation.beyond_booking_horizon` | 422 | Date trop lointaine | Cette date est trop lointaine. |
| `reservations.reservation.party_size_too_large` | 422 | Groupe trop nombreux | Groupe trop nombreux pour une réservation en ligne : appelez le restaurant. |
| `reservations.reservation.notes_too_long` | 422 | Message trop long | Votre message est trop long. |
| `reservations.reservation.cancellation_cutoff_passed` | 422 | Trop tard pour annuler | Il est trop tard pour annuler en ligne : appelez le restaurant. |
| `reservations.reservation.already_terminal` | 422 | Réservation déjà annulée ou terminée | Cette réservation est déjà annulée ou terminée. |
| `storefront.reviews.take_invalid` | 422 | `take` hors de 0 à 50 | — |
| `storefront.review.order_not_completed` | 422 | Avis demandé avant la fin de la commande | Vous pourrez donner votre avis une fois la commande remise. |
| `reviews.feedback.order_already_reviewed` | 409 | Déjà un avis pour cette commande | Vous avez déjà donné votre avis sur cette commande. Merci ! |
| `reviews.rating.out_of_range` | 422 | Note hors de 1 à 5 | Choisissez une note de 1 à 5 étoiles. |
| `reviews.feedback.comment_too_long` | 422 | Commentaire de plus de 2 000 caractères | Votre commentaire fait 2 000 caractères au plus. |
| `storefront.survey.not_found` | 404 | Aucun questionnaire en cours | Ce questionnaire n’existe pas ou plus. |
| `storefront.survey.order_not_completed` | 422 | Commande pas encore terminée | Vous pourrez répondre une fois la commande remise. |
| `reviews.survey_response.not_open` | 409 | Questionnaire clos | Ce questionnaire est clos : merci de votre intérêt. |
| `reviews.survey_response.already_answered` | 409 | Déjà répondu pour cette commande | Vous avez déjà répondu pour cette commande. Merci ! |
| `reviews.survey_response.required_missing` | 422 | Question obligatoire sans réponse | Répondez aux questions marquées d’un astérisque. |
| `reviews.survey_response.empty` | 422 | Aucune réponse | Répondez à au moins une question. |
| `reviews.survey_response.text_too_long` | 422 | Réponse de plus de 1 000 caractères | Une réponse fait 1 000 caractères au plus. |

## Idempotence (les deux API)

| Code | Statut | Cause | Message proposé |
|---|---|---|---|
| `idempotency.key_required` | 422 | `Idempotency-Key` manquant sur une route qui l'exige | La demande n’a pas abouti. Réessayez. |
| `idempotency.key_invalid` | 422 | Clé de 1 à 100 caractères ASCII visibles attendue | La demande n’a pas abouti. Réessayez. |
| `idempotency.key_reused` | 422 | Même clé avec un autre contenu | La demande n’a pas abouti. Réessayez. |
| `idempotency.request_in_progress` | 409 | Même clé en cours de traitement | Votre demande est en cours de traitement… |

## Codes promo

Un code promo refusé n'est pas une erreur au devis : `coupon.isValid` vaut `false` et `coupon.refusalCode` vaut
toujours `storefront.checkout.coupon_invalid`, quel que soit le motif (inconnu, épuisé, expiré, non applicable) : on ne
laisse pas deviner les codes. À la commande, le même code est renvoyé comme erreur (422). Message proposé : « Ce code
promo ne peut pas être utilisé. »

## API d'intégration

| Code | Statut | Cause |
|---|---|---|
| `orders.order.external_reference_duplicate` | 409 | Commande de plateforme déjà importée (même `platform` et `externalReference`) |
| `orders.tenant.not_writable` et `*.tenant.not_writable` | 422 | Restaurant suspendu ou clos : lecture seule |
| `reporting.user_required` | 403 | Requête sans utilisateur ni clé d'API |
| `paging.invalid` | 422 | `page` ou `pageSize` hors limites |
| `*.not_found` | 404 | Ressource inconnue ou d'un autre restaurant |
| `*.concurrent_update` | 409 | Modifiée en même temps par quelqu'un d'autre : relisez puis recommencez |

Une clé refusée (`401`), une route fermée aux clés ou un scope manquant (`403`) et la limite de débit (`429`)
répondent **sans corps**.

## Routes de la vente en ligne

Toutes sous `{{API_URL}}/v1/storefront`, avec `X-Storefront-Key`.

| Méthode | Route | En-têtes en plus | Page |
|---|---|---|---|
| GET | `/` | | [Catalogue](03-vente-catalogue.md) |
| GET | `/locations/{locationId}` | | [Catalogue](03-vente-catalogue.md) |
| GET | `/locations/{locationId}/catalog` | `If-None-Match` | [Catalogue](03-vente-catalogue.md) |
| GET | `/tables/{token}` | | [Catalogue](03-vente-catalogue.md) |
| POST | `/cart/quote` | `X-Customer-Token` (récompense) | [Commande](04-vente-commande.md) |
| GET | `/locations/{locationId}/slots?mode&zoneId` | | [Commande](04-vente-commande.md) |
| POST | `/orders` | `Idempotency-Key`, `X-Human-Check`, `X-Customer-Token` | [Commande](04-vente-commande.md) |
| GET | `/orders/{token}` | | [Commande](04-vente-commande.md) |
| POST | `/orders/{token}/cancel` | | [Commande](04-vente-commande.md) |
| POST | `/orders/{token}/payments` | `Idempotency-Key` | [Paiement](05-vente-paiement.md) |
| POST | `/customer/code` | `X-Human-Check` | [Comptes clients](06-vente-comptes-clients.md) |
| POST | `/customer/code/verify` | | [Comptes clients](06-vente-comptes-clients.md) |
| GET | `/customer` | `X-Customer-Token` | [Comptes clients](06-vente-comptes-clients.md) |
| GET | `/customer/orders?take` | `X-Customer-Token` | [Comptes clients](06-vente-comptes-clients.md) |
| POST | `/customer/sign-out` | `X-Customer-Token` | [Comptes clients](06-vente-comptes-clients.md) |
| GET | `/locations/{locationId}/booking?date&partySize` | | [Réservations](07-vente-reservations-questionnaires.md) |
| POST | `/reservations` | `Idempotency-Key`, `X-Human-Check`, `X-Customer-Token` | [Réservations](07-vente-reservations-questionnaires.md) |
| GET | `/reservations/{token}` | | [Réservations](07-vente-reservations-questionnaires.md) |
| POST | `/reservations/{token}/cancel` | | [Réservations](07-vente-reservations-questionnaires.md) |
| GET | `/reviews?locationId&take` | | [Avis](07-vente-reservations-questionnaires.md#avis-clients) |
| POST | `/reviews` | | [Avis](07-vente-reservations-questionnaires.md#avis-clients) |
| GET | `/surveys/active` | | [Questionnaires](07-vente-reservations-questionnaires.md) |
| GET | `/surveys/{surveyId}` | | [Questionnaires](07-vente-reservations-questionnaires.md) |
| POST | `/surveys/{surveyId}/responses` | | [Questionnaires](07-vente-reservations-questionnaires.md) |
| POST | `/kiosk/pairings` | | [Borne](08-vente-borne.md) |
| POST | `/kiosk/pairings/{pairingId}/claim` | | [Borne](08-vente-borne.md) |

Une borne appairée ajoute `X-Kiosk-Device` à toutes ses requêtes. La description OpenAPI de ces routes est publique :
`{{API_URL}}/openapi/storefront.json`.
