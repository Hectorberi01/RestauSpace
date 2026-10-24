# Audit : application des limites des plans

Ce document vérifie, dans le code, que chaque restriction du catalogue (`scripts/plans/catalogue.json`) est bien appliquée. Il s'agit d'une lecture du code, sans aucune modification.

## 1. Ce que vendent les plans (marché XOF)

| Droit | Découverte | Essentiel | Pro | Groupe |
|---|---|---|---|---|
| `locations.count` | 1 | 1 | 2 | 5 |
| `staff.count` | 2 | 5 | 15 | illimité |
| `orders.monthly` | 150 | 1 500 | illimité | illimité |
| `notifications.email.monthly` | 200 | 1 000 | 5 000 | 20 000 |
| `notifications.sms.monthly` | désactivé | 50 | 200 | 1 000 |
| `notifications.whatsapp.monthly` | désactivé | 100 | 500 | 2 000 |
| `notifications.push.monthly` | 500 | illimité | illimité | illimité |
| `notifications.inapp.monthly` | illimité | illimité | illimité | illimité |

Le marché EUR a ses propres valeurs : Essentiel offre 8 membres et 3 000 commandes, Pro 25 membres. Le mécanisme d'application est le même.

Options : `storefront.web`, `storefront.mobile_app`, `storefront.kiosk` (limite = nombre de bornes × quantité souscrite) et `storefront.api`.

La page Tarifs indique que les plans « ne diffèrent que par les volumes ». Aucun module n'est donc réservé à un plan : il n'y a rien d'autre à contrôler que ces droits.

## 2. Calcul du droit (Billing)

- `EntitlementAsync` additionne le droit du plan de l'abonnement vivant et celui des options actives, multiplié par la quantité. Un droit absent du plan est **désactivé**, et un droit illimité l'emporte. Le calcul est correct.
- « Abonnement vivant » signifie tout statut sauf `Cancelled`. Un restaurant suspendu (essai expiré, impayé) ne peut plus écrire, ce qui bloque de fait l'usage.
- Différence entre Essentiel et Pro : chaque restaurant lit le droit **de son propre plan** (`GetFeatureLimitQuery` sur le tenant courant). Essentiel et Pro reçoivent donc bien des limites différentes.

## 3. Application, droit par droit

| Droit | Où il est appliqué | Résultat |
|---|---|---|
| `locations.count` | `CreateLocationHandler` : compte les établissements non archivés, refus `PlanLimitReached` | **Appliqué.** L'archivage est définitif, donc impossible de contourner la limite en archivant puis en réactivant. |
| `staff.count` | `InviteStaffHandler` : compte invités, actifs et suspendus (pas ceux partis), refus `PlanLimitReached` | **Appliqué.** Le propriétaire compte. Réintégrer un membre suspendu ne dépasse pas la limite, car il était déjà compté. |
| `orders.monthly` | `PlaceOrderHandler` : compte les commandes passées dans le mois civil UTC, refus `MonthlyOrderLimitReached` | **Appliqué** à tous les canaux : caisse, console, site, application, borne. Tous passent par `PlaceOrderCommand`, et le site passe la commande **avant** le paiement, donc aucun client ne paie une commande refusée. |
| `notifications.*.monthly` | `NotificationPolicy` : quota du mois par canal, envoi ignoré avec le motif `QuotaExceeded` | **Appliqué**, avec deux réserves (constats 3 et 4). |
| `storefront.web` / `mobile_app` | `CreateSalesApp` et `EnableSalesApp` (`RequireOptionAsync`) | **Appliqué à la création seulement** (constat 1). |
| `storefront.kiosk` | appairage d'une borne (`KioskLimits.EnsureRoomAsync`) : bornes actives + appairages approuvés ≥ limite → refus | **Appliqué à l'appairage seulement** (constats 1 et 5). |
| `storefront.api` | création et rotation d'une clé d'API (`RequireApiKeysOptionAsync`) | **Appliqué à la création seulement** (constat 1). |

## 4. Constats

| # | Gravité | Constat | Effet concret |
|---|---|---|---|
| 1 | **Haute** | Les options ne sont vérifiées qu'à la **création** : application de vente, borne, clé d'API. Aucun contrôle à l'**usage**, et aucune réaction à la résiliation d'une option (l'événement `TenantAddOn*` n'est lu que par Reporting). | Un restaurant résilie « Site web », « Borne » ou « API » et garde son site de commande, ses bornes et ses clés d'API en service, sans payer. |
| 2 | **Haute** | Passage à un plan inférieur (Pro → Essentiel, Groupe → Pro) : `ChangeSubscriptionPlanHandler` ne compare pas l'usage actuel aux limites du nouveau plan. | Exemple : un restaurant Pro avec 2 établissements et 12 membres passe en Essentiel (1 établissement, 5 membres). Il garde ses 2 établissements et ses 12 membres indéfiniment : seuls les **ajouts** sont bloqués. |
| 3 | Moyenne | Les messages « essentiels » (`account.verify_email`, `account.password_reset`, `security.otp`) passent outre le quota **et** le canal désactivé. | Un restaurant en Découverte, sans SMS, peut envoyer des OTP par SMS sans aucune limite de plan. Seuls les plafonds de fréquence protègent la facture SMS : délai entre deux envois et plafond horaire des envois anonymes. |
| 4 | Basse | Quotas de notifications et de commandes : le comptage puis l'envoi ne sont pas atomiques. | Quelques envois ou commandes de plus que la limite en cas d'actions simultanées, exactement au seuil. |
| 5 | Moyenne | Baisser la quantité de bornes (`ChangeMyAddOnQuantity`), ou résilier l'option, ne révoque aucune borne déjà appairée. | Exemple : 3 bornes payées, puis option ramenée à 1. Les 3 bornes continuent de fonctionner. |
| 6 | Moyenne | Une option peut être souscrite sur le plan **Découverte** : seul l'abonnement offert illimité est refusé. Elle est « facturée avec la prochaine période payée », et Découverte n'en a pas. | Site web, borne ou API gratuits pendant le mois de Découverte. La page Tarifs annonce des options « à ajouter à n'importe quel plan **payant** ». |
| 7 | Basse | Les ponts Host (`PlanLimitsFromBilling`, `NotificationEntitlementsFromBilling`, `StorefrontEntitlementsFromBilling`) n'ont aucun test, et aucun test ne compare deux plans de bout en bout (Essentiel contre Pro). | Une régression dans la traduction « désactivé = 0, sans abonnement = sans plafond » passerait inaperçue. |
| 8 | Info | La page Tarifs présente Pro comme « Pour un restaurant établi, **avec livraison** », alors que la livraison est incluse dans tous les plans. | Libellé trompeur, aucun effet sur le code. |
| 9 | Info | Les droits réellement enregistrés en production viennent de `seed-plans.py`. Ce document ne peut pas les vérifier. | À contrôler dans le back-office : Plans › chaque plan › droits. |

## 5. Corrections proposées

1. **Options à l'usage** : vérifier le droit dans l'authentification par clé publique (`StorefrontKeyAuthentication`), par clé d'API, et au jeton de borne. Un droit perdu doit donner 403 `storefront.option_required`. En complément, à la réception de `TenantAddOn*`, désactiver les applications, révoquer les bornes en trop et suspendre les clés d'API.
2. **Passage à un plan inférieur** : refuser le changement tant que l'usage dépasse les limites du plan visé, avec une erreur `billing.plan.usage_exceeds_limits` qui détaille quels droits et de combien. Autre possibilité : l'autoriser, puis geler les éléments en trop (établissements en lecture seule, membres suspendus).
3. **Messages essentiels** : ils ne passent plus outre un canal **désactivé** ; ils restent hors quota sur un canal activé. Sur Découverte, l'OTP part alors par email.
4. **Atomicité** : pour les notifications, réserver la place dans `notification_usage` (incrément conditionnel) avant l'envoi. Pour les commandes, verrou consultatif par tenant pendant `PlaceOrder`.
5. **Bornes** : refuser une baisse de quantité inférieure au nombre de bornes actives, ou révoquer les plus anciennes.
6. **Options sur Découverte** : refuser `SubscribeAddOn` quand le plan est gratuit (`FreePlan.Is`).
7. **Tests** : un test par pont, et un test d'intégration qui charge le vrai `catalogue.json`. Pour chaque plan, ce test atteint chaque limite et vérifie le refus à N+1.
8. **Libellé** : corriger le texte de Pro sur la page Tarifs.
