# API de commande en ligne : audit de faisabilité et étapes

Date : 12 octobre 2026.

## 1. Objectif

Permettre au restaurateur de brancher son catalogue sur trois types de points de vente, pour que ses clients commandent eux-mêmes :

- **Borne de commande** sur place (sur place ou à emporter).
- **Site web** du restaurant : le nôtre (`restaurantos-site`) ou un site fait par une agence.
- **Application mobile** : la nôtre, en marque blanche, ou une application développée par le restaurant.

On cible deux publics :

- **Nos propres interfaces** : site public, borne, application. Elles utilisent l'API comme n'importe quel client.
- **Les développeurs du restaurant ou de son agence** : ils reçoivent une clé, une documentation OpenAPI et des webhooks.

## 2. Verdict

**C'est faisable, sans refonte.** Le domaine couvre déjà l'essentiel :

- **Calcul côté serveur** : prix, taxes, remises.
- **Cycle de vie des commandes** et **cuisine**.
- **Paiement en ligne** : 6 prestataires.
- **Livraison** : zones et frais.
- **Promotions**, **fidélité**, **notifications**.
- **Webhooks signés**.

Ce qui manque, c'est la **couche d'exposition aux clients finaux**. Aujourd'hui, toute commande passe par un membre du personnel ou par une clé d'API serveur. Plusieurs points bloquent même cette clé serveur.

## 3. Ce qui existe et se réutilise

| Brique | État | Où |
|---|---|---|
| Prix, taxes, remises calculés côté serveur | Prêt : le client n'envoie que des ids | `IOrderCatalog.DescribeForSaleAsync`, `Order.Recalculate()` |
| Commande par étapes : création, lignes, validation | Prêt | `Orders.Application/Commands/OrderCommands.cs` |
| Commande sans fiche client | Possible (`CustomerId` facultatif) | `CreateOrderCommand` |
| Numéro de commande lisible | Existe (`OrderNumber`) | `Order.cs:61` |
| Plafond mensuel de commandes du plan | Vérifié à la validation | `PlaceOrderHandler`, `IOrderPlanLimits` |
| Horaires, exceptions, fuseau, « ouvert à tel instant » | Prêt dans le domaine, non exposé | `RestaurantLocation.IsOpenAtInstant`, `BusinessHours` |
| Paiement en ligne avec page du prestataire | Prêt, mais réservé au personnel | `StartOnlinePaymentCommand` |
| Retrait programmé (heure, délai minimal) | Existe dans Delivery | `CreatePickupCommand(PickupAt)` |
| Livraison : zones, frais, minimum | Prêt, frais posés en asynchrone | `Delivery` |
| Coupons | Prêts, remise posée en asynchrone | `ApplyPromotionToOrderCommand` |
| OTP par SMS, email ou WhatsApp | Prêt, mais réservé au personnel et ne renvoie pas de jeton | `RequestOtpCommand`, `VerifyOtpCommand` |
| Consentements avec source libre | Prêt | `GrantConsentCommand` |
| Clés d'API serveur (`srk_`, hash, scopes, rotation, limite de débit) | Prêt | Module Integrations |
| Webhooks signés (Standard Webhooks, anti-SSRF, réessais) | Prêt, 11 événements | `WebhookEvents.cs` |
| Idempotence (`Idempotency-Key`, 24 h, PostgreSQL) | Prêt pour les utilisateurs authentifiés | `BuildingBlocks.Api/Idempotency.cs` |
| Menu public anonyme | Lecture seule, insuffisant pour commander | `Host/PublicMenu.cs` |

## 4. Écarts à combler

### Bloquants

1. **Aucune route de commande pour le client final.** `POST /orders`, `/payments/online` et `/otp` exigent un membre du personnel ou une permission.

2. **Une clé d'API ne peut pas lire ce qu'elle écrit.** `UseApiKeyRestrictions` renvoie 403 sur toute route sans `RequirePermission`. Les lectures du catalogue, des établissements et des commandes n'en ont pas, donc une clé peut créer une commande mais ni lire le catalogue ni suivre la commande.

3. **Le menu public ne permet pas de constituer un panier.** Il ne donne :
   - aucun id de produit, de variante ou d'option ;
   - aucun groupe d'options ni ses règles min/max ;
   - ni images, ni allergènes.

4. **Une seule sorte de clé, et elle est secrète.** Il n'existe pas de clé publique (*publishable*) utilisable dans un navigateur, une borne ou une application. Il n'y a pas non plus de rattachement d'une clé à des établissements, ni d'origines autorisées par clé.

5. **Idempotence anonyme dangereuse.** Sans `tid` ni `sub`, tous les appelants anonymes partagent le même périmètre (`-|-`) : deux clients qui envoient la même clé recevraient la réponse l'un de l'autre.

6. **Une commande en ligne n'arrive pas en cuisine.** Le ticket est créé sur `OrderAccepted`, et rien n'accepte automatiquement une commande en ligne, ni à la validation ni après le paiement.

7. **`IsOpen` est faux sur le menu public.** Il reflète le statut de l'établissement, pas ses horaires.

### Importants

8. **Pas de canal sur la commande.** Rien ne distingue comptoir, QR, en ligne, borne, application ou API. Les rapports, les règles d'acceptation et le plafond du plan en ont besoin.

9. **Remises et frais de livraison asynchrones.** Ils sont posés par événement après coup. Le total peut changer entre l'affichage du panier et le paiement. Il faut un chemin synchrone pour le checkout.

10. **URL de retour du paiement globale.** `Payments:CheckoutReturnUrl` vaut pour toute la plateforme. Chaque site ou application a besoin de la sienne, prise dans une liste autorisée.

11. **Commandes impayées.** Rien n'annule une commande en ligne non payée au bout de N minutes.

12. **Images sans URL publique stable.** Les liens de téléchargement sont présignés et valables 5 minutes, donc inutilisables pour un catalogue mis en cache.

13. **Synchronisation du catalogue impossible.** Il n'y a :
    - ni ETag ni version du catalogue ;
    - aucun webhook de changement de catalogue ;
    - aucun produit visible par canal (par exemple masqué sur la borne).

14. **Pas de réglages de commande en ligne par établissement** :
    - modes proposés (sur place, à emporter, livraison) ;
    - délai de préparation ;
    - créneaux ;
    - pause (« on ne prend plus de commandes »).

15. **Pas d'identité consommateur.** Identity ne gère que le personnel. L'unicité de l'email ou du téléphone d'un client n'est pas garantie.

16. **Suivi de commande.** `GET /orders/{id}` est réservé au personnel. Le client n'a ni jeton de suivi, ni flux de statut. Par ailleurs, `OrderAccepted` et `OrderReady` ne partent pas en webhook.

17. **Notifications client.** Elles ne partent que si la commande a un `CustomerId` : un invité ne reçoit rien.

### Mineurs

18. Pas de note au niveau de la commande, seulement par ligne.

19. CORS : une seule politique globale. Il faut des origines par application.

20. Un seul document OpenAPI, qui mélange API interne et API publique.

## 5. Architecture proposée

### Un module `Storefront` (vente en ligne)

C'est un nouveau module plutôt qu'une façade dans l'hôte : il y a de vraies règles et des données propres. Il possède :

- **Applications de vente** (`SalesChannelApp`) : nom, type (`Web`, `Mobile`, `Kiosk`, `Api`), établissements servis, origines autorisées, URL de retour de paiement autorisées, statut.
- **Clés publiques** `pk_live_…` / `pk_test_…`, liées à une application et au tenant. Seul leur hash est stocké, comme pour `srk_`. Elles ne donnent accès qu'au périmètre `storefront`, jamais aux routes de gestion.
- **Bornes** : appairage d'un appareil par le personnel (code à 6 chiffres affiché sur la borne), puis jeton d'appareil révocable.
- **Paniers et checkouts** : prix calculés, validation, expiration.
- **Jetons de suivi** : un HMAC signé par commande, donné au client à la validation.

Il orchestre les modules existants par des ports, avec des ponts dans l'hôte (même modèle que les autres modules) :

- Catalog pour la lecture ;
- Locations pour les horaires ;
- Orders pour créer et valider ;
- Payments pour démarrer le paiement ;
- Delivery pour le devis et la création ;
- Promotions pour le coupon ;
- Customers pour l'invité et les consentements ;
- Notifications pour l'OTP.

**Règles de sécurité (non négociables) :**

- **Tenant** : il vient de la clé publique ou du jeton d'appareil, jamais de la requête.
- **Établissement** : chaque `locationId` est vérifié comme appartenant au tenant **et** à l'application.
- **Prix** : jamais fournis par le client. Le serveur recalcule et renvoie le total qui fait foi.
- **Origines** : une clé publique utilisée depuis un navigateur ne fonctionne que pour ses origines (CORS dynamique et contrôle de l'en-tête `Origin`).
- **Débit et anti-abus** : limite de débit par clé, par adresse et par appareil. Défi anti-robot facultatif sur le web (Turnstile ou hCaptcha) à la validation.
- **Idempotence** : `Idempotency-Key` obligatoire sur la validation et le paiement. Le périmètre inclut la clé publique et l'appareil, ou une session.
- **Données personnelles** : jamais dans les logs, les événements ou les traces. Le jeton de suivi ne contient aucune donnée personnelle.
- **Tenant suspendu** ou établissement archivé : réponse 404.

### Routes (`/v1/storefront`)

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/locations` | Établissements de l'application : horaires, ouvert maintenant, modes, délai, prochain créneau |
| GET | `/locations/{id}/catalog` | Catalogue commandable (ids, variantes, groupes d'options et règles, images, allergènes, disponibilité), avec ETag |
| POST | `/carts/quote` | Panier → lignes prix, remises, frais de livraison, total, erreurs par ligne. Sans effet |
| GET | `/locations/{id}/slots` | Créneaux de retrait ou de livraison |
| POST | `/orders` | Panier complet + mode + créneau + coordonnées + coupon. Crée et valide en un appel (`Idempotency-Key` obligatoire). Renvoie numéro, jeton de suivi, total et action suivante |
| POST | `/orders/{id}/payment` | Démarre le paiement en ligne (jeton de suivi) et renvoie l'URL du prestataire |
| GET | `/orders/{id}` | Suivi : statut, paiement, numéro, heure estimée (jeton de suivi) |
| POST | `/orders/{id}/cancel` | Annulation par le client tant que la commande n'est pas acceptée |
| POST | `/customers/otp`, `/customers/otp/verify` | Phase comptes clients : jeton client |

Webhooks à ajouter :

- `orders.order_accepted` et `orders.order_ready` ;
- `catalog.changed` (menu publié, produit indisponible) ;
- `storefront.order_expired`.

### Changements dans les modules existants

- **Orders**
  - `Channel` : Counter, QrCode, Online, Kiosk, MobileApp, Api.
  - `SourceAppId`.
  - `ScheduledFor` : retrait ou livraison à une heure donnée.
  - Note de commande.
  - Commande de création « tout en un » avec le panier complet, dans une transaction.
- **Acceptation automatique**, réglée par établissement et par canal :
  - à la validation, si paiement au comptoir ;
  - au paiement capturé, si paiement en ligne ;
  - ou manuelle.
- **Expiration** : un worker annule une commande en ligne non payée après N minutes.
- **Catalog**
  - visibilité par canal ;
  - ETag / version du catalogue ;
  - référentiel des allergènes ;
  - URL publiques d'images (dérivées et mises en cache).
- **Locations** : réglages de vente en ligne par établissement :
  - modes ;
  - délai de préparation ;
  - minimum de commande ;
  - pause ;
  - horaires spécifiques à la vente en ligne (facultatif).
- **Payments** :
  - URL de retour choisie dans la liste de l'application ;
  - démarrage par le jeton de suivi.
- **Promotions et Delivery** :
  - calcul synchrone pour le devis et la validation (même code que les ponts actuels, appelé directement) ;
  - les événements restent pour les autres usages.
- **Notifications** : confirmation, commande prête et annulation envoyées aussi à un invité, avec le contact saisi au checkout et son consentement.
- **Billing** : la vente en ligne et la borne deviennent des options du plan. Le plafond de commandes s'applique ; au-delà, on refuse avec un message clair.

## 6. Étapes

Les durées indiquées sont celles du développement backend et front, tests compris.

| Phase | Contenu | Livrable | Estimation |
|---|---|---|---|
| **0. Décisions** | Trancher les questions de la section 7 | Ce document validé | 0,5 j |
| **1. Fondations** | Module Storefront : applications, clés `pk_`, CORS dynamique, idempotence par clé. `Channel` sur la commande. Réglages de vente en ligne par établissement. Correction de `IsOpen`. Console : **Paramètres › Vente en ligne** (applications, clés, origines) | Une application peut lire établissements et horaires | 4 à 5 j |
| **2. Catalogue commandable** | DTO complet, images publiques, ETag, visibilité par canal, allergènes, webhook `catalog.changed` | `GET /catalog` utilisable par un front | 3 à 4 j |
| **3. Commande invité** | Devis synchrone, `POST /orders` tout-en-un, jeton de suivi, `GET /orders/{id}`, annulation client, acceptation automatique, notifications invité, retrait programmé, paiement au comptoir | Commande de bout en bout jusqu'à la cuisine | 5 à 6 j |
| **4. Paiement en ligne** | Démarrage par jeton, URL de retour par application, expiration des impayés, acceptation au paiement capturé | Commande payée en ligne | 3 j |
| **5. Livraison et coupons** | Créneaux, devis de livraison, coupon au checkout | Livraison et promotions en ligne | 3 j |
| **6. Site de référence** | Pages de commande sur `restaurantos-site` (menu → panier → checkout → suivi) | Site de commande prêt | 4 à 5 j |
| **7. Borne** | Appairage des appareils, application plein écran (PWA) sur place ou à emporter, gros boutons, mise en veille, numéro affiché ou imprimé, paiement au comptoir puis en ligne par QR | Borne utilisable | 4 à 5 j |
| **8. Comptes clients** | OTP public → jeton client, rattachement à la fiche Customer (règle d'unicité), historique, fidélité au checkout | Clients reconnus | 4 j |
| **9. Ouverture aux développeurs** | Document OpenAPI `storefront` séparé, guide, environnement de test (`pk_test_`), exemples, webhooks complétés | API publiable | 2 à 3 j |

**Ordre conseillé :** 1 → 2 → 3 → 4 → 6 pour une première mise en service sur le site web, puis 7, 5, 8 et 9 selon la demande.

**Paiement par carte sur la borne** (terminal de paiement intégré) : hors périmètre au départ. Il dépend du matériel et du prestataire (Stripe Terminal, SumUp…). À étudier séparément.

## 7. Questions à trancher avant de coder

1. **Qui construit les interfaces ?** Nous seulement (site, borne, application en marque blanche), ou aussi les agences des restaurants ? Cela décide si la phase 9 et les clés `pk_` exposées publiquement sont prioritaires.
2. **Application mobile** : une application par restaurant (marque blanche), ou une application unique multi-restaurants (place de marché) ? Le second cas change la gestion des comptes clients.
3. **Paiement** : payer obligatoirement en ligne, ou proposer « payer au comptoir » ? Le second cas expose aux commandes non honorées.
4. **Acceptation** : automatique par défaut, ou validation manuelle par le restaurant (tablette qui sonne) ?
5. **Comptes clients** : invité seulement au départ, ou OTP dès la première version ?
6. **Offre** : la vente en ligne et la borne sont-elles incluses dans un plan ou vendues en option ?
7. **Borne** : quel matériel visé (tablette Android, écran Windows) ? Impression d'un ticket nécessaire ?

## 8. Risques

- **Pic de charge à l'heure du repas** : catalogue mis en cache (ETag, CDN), limites de débit par clé, et le compteur en mémoire actuel est à revoir en multi-instance (Redis).
- **Fraude et commandes fantômes** : exiger le paiement en ligne au-delà d'un montant, faire expirer les impayés, protection anti-robot sur le web.
- **Cohérence du total** : le devis et la validation doivent passer par le même calcul. Le total renvoyé à la validation fait foi.
- **Rupture pendant la commande** : la disponibilité est revérifiée à la validation, avec une erreur par ligne, compréhensible.
