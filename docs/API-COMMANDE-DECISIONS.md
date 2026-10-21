# API de commande en ligne : décisions (étape 0)

Date : 12 octobre 2026. Complète `API-COMMANDE-AUDIT.md`.

Chaque décision indique qui l'a prise :

- **Produit** : tranchée par Hector.
- **Technique** : proposée par Claude et retenue par défaut. Elle peut être revue avant la phase concernée.

## 1. Décisions produit

| # | Sujet | Décision |
|---|---|---|
| P1 | Qui construit les interfaces | **La plateforme et l'agence (ou le développeur) du restaurant.** L'API est publique et documentée dès la première version. Nos propres interfaces l'utilisent comme n'importe quel client |
| P2 | Application mobile | **Marque blanche** : une application par restaurant, à son nom et à son logo, produite depuis un même code |
| P3 | Paiement | **Au choix du restaurant**, par canal : « payer en ligne », « payer au comptoir », ou les deux. Plafond de montant possible pour le comptoir |
| P4 | Acceptation des commandes | **Réglable, automatique par défaut**, par établissement et par canal. Acceptée à la validation si paiement au comptoir, au paiement capturé si payée en ligne. Le restaurant peut passer en validation manuelle |
| P5 | Offre | **Vendue en option**, en plus du plan d'abonnement |
| P6 | Borne | **Tablette Android + imprimante thermique 80 mm**, avec **ticket imprimé** |
| P7 | Comptes clients | **Invité d'abord** : nom et téléphone ou email au checkout. Comptes par code OTP ensuite (phase 8). Les clients appartiennent au restaurant, ce qui va avec la marque blanche |

## 2. Conséquences des décisions produit

### P1 : API publique dès la première version

- La documentation OpenAPI `storefront` passe dans les fondations : elle n'est plus la dernière phase.
- Le restaurateur crée lui-même, dans la console, les applications et leurs clés, puis les remet à son agence. L'agence n'a **aucun accès** à la console ni aux secrets de paiement ou de notification.
- Deux sortes de clés :
  - **`pk_…`** (publique) : dans un navigateur, une application ou une borne. Lecture du catalogue, commande, paiement et suivi seulement. Rattachée à une application, à des établissements et à des origines.
  - **`srk_…`** (secrète, existante) : pour le serveur d'une agence ou d'un partenaire. Elle garde ses scopes, mais il faut **corriger le défaut actuel** : une clé `srk_` doit pouvoir lire ce qu'elle a le droit d'écrire (catalogue, établissements, commandes).
- Pour tester, une agence travaille sur un **restaurant de démonstration** (tenant dédié avec le prestataire de paiement `sandbox`). Il n'y a pas de « mode test » mêlé aux vraies commandes en première version.

### P2 : application mobile en marque blanche

- **Un seul code Flutter**, paramétré par restaurant : nom, icône, couleurs, clé `pk_`, établissements. Il est généré par un script de build.
- **Publication sous le compte développeur du restaurant** (Apple et Google). Apple refuse les applications issues d'un modèle commercialisé quand elles ne sont pas soumises par le propriétaire du contenu (règle 4.2.6 de l'App Store). Il faut le prévoir dans l'offre :
  - soit accompagnement à l'ouverture des comptes ;
  - soit publication assurée par nous au nom du restaurant.
- L'application mobile vient après le site web : elle réutilise exactement la même API.

### P3 : paiement au choix du restaurant

- Réglage par établissement et par canal (web, application, borne) :
  - `payer en ligne` activé ou non ;
  - `payer au comptoir` activé ou non ;
  - plafond pour le comptoir.
- **Sur la borne :**
  - **Au comptoir** : ticket imprimé avec numéro et code-barres, payé à la caisse, puis la commande part en cuisine ou y est déjà selon P4.
  - **En ligne** : QR affiché à l'écran et scanné par le téléphone du client (mobile money ou carte), sans saisie sur la borne.
  - **TPE intégré à la borne** : hors périmètre de la première version.
- **Commande payée en ligne et non payée** : annulée automatiquement après **15 minutes** (réglable).

### P4 : acceptation automatique par défaut

- C'est elle qui crée le ticket cuisine aujourd'hui : le flux actuel (`OrderAccepted` → ticket) reste inchangé.
- **Validation manuelle** : la console signale la nouvelle commande par une sonnerie. Délai d'acceptation au-delà duquel la commande est refusée automatiquement : réglable, **10 minutes** par défaut. Le client est alors prévenu et remboursé s'il a payé.

### P5 : option payante

Billing ne connaît aujourd'hui qu'**un plan par abonnement**, avec des droits (`Entitlement`) activés ou non et une limite éventuelle. Il n'y a **pas d'options**.

**À ajouter :** des **options d'abonnement** (`SubscriptionAddOn`), avec ce schéma :

- une option ouvre un ou plusieurs droits ;
- elle a son propre prix, mensuel ou annuel ;
- elle suit la période de l'abonnement ;
- les droits effectifs d'un restaurant = ceux du plan **+** ceux de ses options.

**Droits proposés :**

| Code | Ouvre | Limite |
|---|---|---|
| `storefront.web` | Commande sur le site web (le nôtre ou celui de l'agence) | — |
| `storefront.mobile_app` | Application en marque blanche | — |
| `storefront.kiosk` | Bornes | Nombre de bornes appairées |
| `storefront.api` | Clés `srk_` pour les serveurs d'agences et de partenaires | — |

Le découpage commercial (une option « Vente en ligne » qui regroupe web et application, une option « Borne » à l'unité…) se règle dans le super-admin sans code.

**Sans option active :**

- le canal concerné refuse les commandes (réponse claire, code stable) ;
- le menu en lecture reste disponible ;
- les commandes déjà passées continuent leur cycle.

### P6 : borne et ticket imprimé

**Borne**

- Application web plein écran (PWA) ouverte en mode kiosque Android.
- Appairée par un membre du personnel : un code à 6 chiffres s'affiche sur la borne et se saisit dans la console. La borne reçoit alors un jeton d'appareil révocable.

**Impression pilotée par le serveur**

Un navigateur ne sait pas imprimer en silence sur une imprimante réseau. Le ticket est donc **produit par le serveur** et **récupéré par l'imprimante**.

- **Imprimantes conseillées** : modèles « cloud » qui interrogent eux-mêmes une adresse HTTPS à intervalle régulier. Exemples : Star (CloudPRNT, mC-Print3 / TSP143IV) et Epson (Server Direct Print, TM-m30III). Aucun logiciel à installer au restaurant.
- **Imprimante ESC/POS classique** : en repli, un petit **relais d'impression** (application Android sur la tablette, ou boîtier) qui récupère les tickets en HTTPS et les envoie à l'imprimante en réseau local.
- **Un module `Printing` réutilisable**, qui gère :
  - les imprimantes déclarées par établissement ;
  - une file de tickets, chacun avec son statut (en attente, imprimé, en échec) et une réimpression possible ;
  - des gabarits (ticket client, ticket cuisine, reçu).

  Il pourra servir ensuite au ticket cuisine et au reçu.

**Contenu du ticket client**

- l'enseigne et l'établissement ;
- le **numéro de commande** en grand ;
- le mode (sur place / à emporter) ;
- les lignes et le total ;
- le mode de paiement, et « À PAYER AU COMPTOIR » s'il y a lieu ;
- un code-barres du numéro ;
- l'heure.

Aucune donnée personnelle.

### P7 : client invité d'abord

- Au checkout : nom, et téléphone **ou** email selon la règle du restaurant (`ContactRequirementPolicy`), plus le consentement marketing facultatif (source « vente en ligne »).
- Une fiche client est créée, ou retrouvée par téléphone ou email dans le restaurant, pour que les notifications (confirmation, commande prête) partent.
- Sur la borne : prénom facultatif seulement. Pas de fiche client, rien n'est envoyé.

## 3. Décisions techniques

| # | Sujet | Décision |
|---|---|---|
| T1 | Découpage | Nouveau module **`Storefront`**, plus un module **`Printing`**. Ils orchestrent Catalog, Locations, Orders, Payments, Delivery, Promotions, Customers et Notifications par des ports, avec des ponts dans l'hôte |
| T2 | Routes | Préfixe `/v1/storefront`, avec un document OpenAPI séparé (`storefront`), publié en production |
| T3 | Résolution du tenant | Par la clé `pk_` ou le jeton de borne, **jamais** par la requête. Chaque `locationId` est vérifié comme appartenant au tenant et à l'application |
| T4 | Origines | CORS dynamique : les origines viennent de l'application. Une clé `pk_` envoyée depuis une autre origine est refusée. Les applications mobiles et les bornes natives n'envoient pas d'`Origin` et sont identifiées par leur clé ou leur jeton ; une application en WebView ou une borne web déclare son origine |
| T5 | Prix | Calculés uniquement par le serveur. Le devis (`/carts/quote`) et la validation passent par le même code. Le total renvoyé à la validation fait foi |
| T6 | Remises et frais | Coupon et frais de livraison calculés **de façon synchrone** au devis et à la validation. Les événements actuels restent pour les autres usages |
| T7 | Idempotence | `Idempotency-Key` obligatoire sur la validation et le paiement. Le périmètre inclut l'application ou l'appareil (corrige le périmètre anonyme `-|-`) |
| T8 | Suivi client | Jeton de suivi signé (HMAC, comme le lien de désinscription), remis à la validation. Il ne contient aucune donnée personnelle. Suivi par interrogation régulière en première version ; flux temps réel plus tard |
| T9 | Canal de commande | `Channel` sur la commande : `Counter`, `QrCode`, `Web`, `MobileApp`, `Kiosk`, `Api`, plus `SourceAppId` |
| T10 | Retrait programmé | `ScheduledFor` sur la commande. Les créneaux sont calculés à partir des horaires de l'établissement (`IsOpenAtInstant`), du délai de préparation et d'une capacité par créneau (facultative) |
| T11 | Catalogue | DTO commandable avec ids, groupes d'options et règles, images, allergènes et disponibilité. ETag calculé sur la version du catalogue. Visibilité par canal. Webhook `catalog.changed` |
| T12 | Images | URL publiques stables et mises en cache (variantes redimensionnées), à la place des liens présignés de 5 minutes |
| T13 | Limites de débit | Par clé, par adresse et par appareil. Compteurs à déplacer dans Redis pour le multi-instance |
| T14 | Anti-robot | Défi facultatif (Turnstile) à la validation sur le web, activable par application |
| T15 | Plafond du plan | Le plafond mensuel de commandes s'applique à tous les canaux. Au-delà, refus avec un message clair au client (« le restaurant ne prend plus de commandes en ligne ») |
| T16 | Données personnelles | Jamais dans les logs, les événements, les traces ni le ticket imprimé |

## 4. Plan révisé

| Phase | Contenu | Estimation |
|---|---|---|
| 1. Fondations | Module Storefront : applications, clés `pk_`, CORS dynamique, idempotence. `Channel` et `ScheduledFor` sur la commande. Réglages de vente en ligne par établissement (modes, délai, paiements acceptés, acceptation). Correction de `IsOpen` et des lectures `srk_`. Billing : options d'abonnement et droits `storefront.*`. Console : **Paramètres › Vente en ligne**. OpenAPI `storefront` | 6 à 7 j |
| 2. Catalogue commandable | DTO complet, images publiques, ETag, visibilité par canal, allergènes, `catalog.changed` | 3 à 4 j |
| 3. Commande invité | Devis, `POST /orders`, jeton de suivi, suivi et annulation, acceptation automatique ou manuelle (sonnerie, refus après délai), notifications invité, paiement au comptoir | 5 à 6 j |
| 4. Paiement en ligne | Démarrage par jeton, URL de retour par application, expiration à 15 minutes, acceptation au paiement | 3 j |
| 5. Site de commande | Pages menu → panier → checkout → suivi sur `restaurantos-site` (implémentation de référence pour les agences) | 4 à 5 j |
| 6. Borne et impression | Module Printing (CloudPRNT / Server Direct Print, relais ESC/POS), appairage, PWA plein écran, ticket client, paiement au comptoir ou par QR | 6 à 7 j |
| 7. Livraison et coupons | Créneaux, devis de livraison, coupon au checkout | 3 j |
| 8. Comptes clients | OTP public → jeton client, rattachement à la fiche Customer, historique, fidélité | 4 j |
| 9. Application marque blanche | Flutter, build paramétré par restaurant, guide de publication | 8 à 10 j |

## 5. Points encore ouverts

À trancher avant la phase indiquée, sans bloquer la phase 1.

- **Phase 6 : modèle d'imprimante de référence.** Une seule marque à certifier au départ (Star ou Epson), selon ce qui se trouve facilement au Bénin et en France.
- **Phase 9 : publication des applications.** Comptes Apple et Google du restaurant avec notre accompagnement, ou publication par nous en son nom.
- **Phase 1 : prix des options.** Il se règle dans le super-admin et ne bloque rien.
