# Vente en ligne

Date : 12 octobre 2026. Suite de `API-COMMANDE-AUDIT.md` et `API-COMMANDE-DECISIONS.md`.

## Ce qui est en place

| Domaine | Ajout |
|---|---|
| **Billing** | Plans de type **Option** (`PlanKind.AddOn`) et **options souscrites** par restaurant (`TenantAddOn`, quantité). Droits effectifs = plan + options, les limites étant multipliées par la quantité. Droits `storefront.mobile_app`, `storefront.kiosk` (limite = nombre de bornes) et `storefront.api` (site web du restaurant et clés d'API ; `storefront.web` retiré le 25/10/2026) |
| **Orders** | Canal de la commande (`Counter`, `QrCode`, `Web`, `MobileApp`, `Kiosk`, `Api`), application source et heure programmée (`ScheduledForUtc`, au plus 14 jours à l'avance). Le canal est posé par le serveur : `Api` pour une clé `srk_`, `Counter` pour le personnel |
| **Storefront** (nouveau module) | Applications de vente (site, application, borne) avec clé publique `pk_live_…` dont seule l'empreinte est gardée, origines autorisées, établissements servis, pages de retour de paiement. Réglages de vente en ligne par établissement : pause, modes, préparation, minimum, règles par canal (paiements, plafond au comptoir, acceptation), délais d'acceptation manuelle et d'expiration des impayés |
| **Hôte** | Schéma d'authentification `StorefrontKey` (en-tête `X-Storefront-Key`), jamais schéma par défaut. CORS et limite de débit propres aux routes `/v1/storefront`. Document OpenAPI `storefront` toujours publié |
| **Corrections** | `IsOpen` du menu public = horaires réels. Une clé `srk_` lit maintenant le catalogue, les établissements et les commandes (scopes `catalog.manage`, `orders.take`…). Idempotence : les appelants anonymes ne partagent plus un même périmètre. Les rôles système (Owner, Manager) reçoivent au démarrage les permissions des nouveaux modules |
| **Console** | **Paramètres › Vente en ligne** : options, applications et clés (affichées une seule fois), réglages par établissement, lien de la documentation |
| **Super-admin** | Création d'un plan de type **Option**. Fiche restaurant › Abonnement : ajouter, redimensionner ou retirer une option |

## À faire sur le Mac

1. **Générer les migrations** :

   ```bash
   ./scripts/migrations-vente-en-ligne.sh
   ```

   Le script crée trois migrations :
   - Billing : `StorefrontAddOns` ;
   - Orders : `OrderChannelAndSchedule` ;
   - Storefront : `InitialCreate`.

2. **Compiler et tester** : `dotnet build`, puis `dotnet test`.
3. **Redémarrer l'API.** Au démarrage :
   - les migrations s'appliquent ;
   - les rôles Owner et Manager existants reçoivent `storefront.view` et `storefront.manage`.
4. **Dans le super-admin** :
   - créez une option, par exemple « Vente en ligne » de type **Option** avec les droits `storefront.api` et `storefront.mobile_app`, et « Borne » avec `storefront.kiosk=1` ;
   - ajoutez-la à un restaurant abonné.
5. **Dans la console**, ouvrez **Paramètres › Vente en ligne** :
   - créez l'application ;
   - copiez la clé ;
   - déclarez l'adresse du site.

## Routes

### Console (personnel, `storefront.view` / `storefront.manage`)

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/v1/online-sales` | Options, applications, réglages par établissement |
| POST | `/v1/online-sales/apps` | Créer une application, renvoie la clé une fois |
| PUT | `/v1/online-sales/apps/{id}` | Nom, origines, établissements, pages de retour |
| POST | `/v1/online-sales/apps/{id}/key` | Nouvelle clé (l'ancienne cesse aussitôt de fonctionner) |
| POST | `/v1/online-sales/apps/{id}/disable` · `/enable` | Couper ou rouvrir l'application |
| PUT | `/v1/online-sales/locations/{locationId}` | Réglages de vente en ligne de l'établissement |
| PUT | `/v1/online-sales/locations/{locationId}/pause` | Pause ou reprise immédiate |

### Applications de vente (clé publique)

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/v1/storefront` | Restaurant, application, établissements (ouverture, modes, paiements du canal, `acceptsOrdersNow`) |
| GET | `/v1/storefront/locations/{id}` | Un établissement servi par l'application |

Exemple :

```bash
curl -H "X-Storefront-Key: pk_live_…" https://api.exemple.com/v1/storefront
```

Documentation : `GET /openapi/storefront.json` (seulement les routes `/v1/storefront`) et la documentation développeurs
(`docs/developpeurs/`, publiée sur le site vitrine à `/developpeurs`).

## Sécurité

- **Restaurant** : il vient de la clé, jamais de la requête.
- **Clé publique** : seule son empreinte SHA-256 est gardée, avec ses 4 derniers caractères pour la reconnaître.
- **Clé refusée** dans tous ces cas, avec une seule réponse (401) :
  - clé inconnue ;
  - application désactivée ;
  - origine non déclarée ;
  - restaurant suspendu.
- **Origine** : une clé est acceptée sans en-tête `Origin` (serveur, application native, borne native) ou depuis une
  origine déclarée sur l'application. Un site web déclare ses adresses ; une application mobile en WebView ou une
  borne web peut aussi déclarer la sienne (`capacitor://localhost` et `ionic://localhost` pour une application mobile).
- **Établissements servis** : catalogue, commande, créneaux, QR de table, réservation et questionnaire refusent un
  établissement que l'application ne sert pas ; une borne ne réserve pas.
- **Portée de la clé** : elle n'ouvre que `/v1/storefront`, et un jeton du personnel n'ouvre pas ces routes.
- **Débit** : 120 requêtes par minute par application et par adresse.
- **Sans l'option**, ou établissement en pause :
  - la lecture reste possible ;
  - `acceptsOrdersNow` vaut `false`.

## Phase 2 : catalogue commandable

### Ce qui est en place

| Domaine | Ajout |
|---|---|
| **Catalog** | Référentiel des **14 allergènes UE** par code (`gluten`, `crustaceans`, `eggs`, `fish`, `peanuts`, `soybeans`, `milk`, `nuts`, `celery`, `mustard`, `sesame`, `sulphites`, `lupin`, `molluscs`), à la place des anciens `AllergenIds` jamais alimentés. **Visibilité par canal** : un produit peut être masqué sur le site, l'application ou la borne. Requête `GetOrderableCatalogQuery` : menus publiés de l'établissement, chaque produit et groupe d'options une seule fois, avec leurs ids |
| **Files** | **Liens publics stables** des photos : `/v1/media/{jeton}`, jeton signé (HMAC-SHA256) portant le restaurant et le fichier, réponse mise en cache un an (`immutable`). Seuls les photos de produits, d'établissements et le logo sont servis ; jamais un export |
| **Storefront** | `GET /v1/storefront/locations/{id}/catalog` avec **ETag** (`Version`) et réponse **304** si inchangé |
| **Webhooks** | Événement **`catalog.changed`** : émis une fois par enregistrement qui touche un produit, un groupe d'options ou un menu |
| **Console** | Fiche produit, onglet **En ligne** : allergènes et canaux où le produit est visible. La duplication d'un produit les recopie |

### À faire sur le Mac

1. **Générer la migration** :

   ```bash
   ./scripts/migrations-catalogue-en-ligne.sh
   ```

   Migration Catalog `OnlineCatalog` : supprime `products.allergen_ids` (toujours vide), ajoute `allergens` et `hidden_channels` (jsonb, `[]` par défaut).

2. **Configurer les liens publics** (sans clé, le catalogue est servi sans photos) :
   - `Files:PublicMedia:BaseUrl` : adresse publique de l'API, ou du CDN placé devant elle ;
   - `Files:PublicMedia:SigningKey` : 32 octets en Base64 (`openssl rand -base64 32`), en variable d'environnement ou coffre de secrets, jamais versionnée.

   Une clé de développement est déjà dans `appsettings.Development.json`. Changer la clé change tous les liens.

3. **Compiler et tester** : `dotnet build`, puis `dotnet test`.

### Routes ajoutées

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/v1/catalog/allergens` | Codes du référentiel |
| PUT | `/v1/catalog/products/{id}/allergens` | `{ "allergens": ["gluten", "milk"] }` |
| PUT | `/v1/catalog/products/{id}/hidden-channels` | `{ "channels": ["Kiosk"] }` (vide = visible partout) |
| GET | `/v1/storefront/locations/{id}/catalog` | Catalogue commandable, clé publique, ETag |
| GET | `/v1/media/{jeton}` | Photo publique, anonyme, 600 requêtes par minute par adresse |

### Forme du catalogue

```json
{
  "locationId": "…",
  "currencyCode": "XOF",
  "pricesIncludeTax": true,
  "menus": [{ "id": "…", "name": "Carte", "sections": [{ "id": "…", "name": "Plats", "productIds": ["…"] }] }],
  "products": [{
    "id": "…", "name": "Burger", "description": null, "price": 4000, "isAvailable": true,
    "variants": [{ "id": "…", "name": "Double", "price": 5500, "isAvailable": true }],
    "modifierGroupIds": ["…"], "allergens": ["gluten", "milk"],
    "images": ["https://api.exemple.com/v1/media/…"]
  }],
  "modifierGroups": [{ "id": "…", "name": "Accompagnement", "minSelections": 1, "maxSelections": 2,
    "modifiers": [{ "id": "…", "name": "Alloco", "priceDelta": 300, "isAvailable": true }] }],
  "version": "3f9a…"
}
```

Règles :

- **Produit commandable** (`isAvailable`) quand il est actif, qu'une variante est disponible s'il en a, et que chaque groupe d'options obligatoire peut encore être satisfait. Un produit indisponible reste affiché.
- **Absents** : brouillons, archivés, et produits masqués sur le canal de l'application (le canal vient de la clé, jamais de la requête).
- **Photos** : la plus ancienne est la photo principale.
- **Cache** : renvoyer `If-None-Match: "<version>"` ; la réponse est `304` tant que rien n'a changé.

### Limites connues

- **Photos redimensionnées** : pas encore de variantes (vignette, plein écran). Les photos sont servies telles qu'envoyées (5 Mo au plus) ; à prévoir avec une bibliothèque d'images ou le redimensionnement du CDN.
- **`catalog.changed`** n'est pas émis quand une photo est ajoutée ou supprimée (module Files) ; l'ETag, lui, change.

## Phase 3 : commande invité

### Ce qui est en place

| Domaine | Ajout |
|---|---|
| **Orders** | `QuoteOrderQuery` : devis d'un panier, calculé en mémoire avec les mêmes règles qu'une vraie commande (produit vendable, variante, options et leurs règles min/max, prix, taxe). Rien n'est enregistré |
| **Storefront** | Devis, validation, suivi et annulation d'une commande invité. Enregistrement `OnlineOrder` (application, canal, mode de paiement, échéance d'acceptation). **Jeton de suivi** signé (HMAC) sans donnée personnelle. **Job** toutes les minutes : une commande en validation manuelle non acceptée à l'échéance est refusée, et le client est prévenu |
| **Hôte** | Ponts vers Orders (création, lignes, validation, acceptation, annulation ; le brouillon est annulé si la validation échoue) et vers Customers (fiche retrouvée par e-mail puis téléphone, sinon créée ; consentement marketing source `online-ordering`) |
| **Console** | **Commandes** : badge du canal (Site web, Application, Borne), heure demandée, **sonnerie** et message à l'arrivée d'une commande en ligne |
| **Correction** | Le test `OrderImmutabilityTests` couvre maintenant `ApplyDeliveryFee`, `RemoveDeliveryFee` et `ChangeCustomer` (il échouait avant cette phase) |

### Déroulé d'une commande

1. **Devis** (`POST /cart/quote`) : lignes et totaux recalculés par le serveur, minimum de commande, paiement au comptoir possible ou non.
2. **Validation** (`POST /orders`, en-tête `Idempotency-Key` obligatoire). Le serveur vérifie, dans l'ordre :
   - l'option du canal, l'application active, le restaurant actif ;
   - l'établissement servi par l'application, pas en pause, ouvert maintenant (ou à l'heure demandée, au plus tôt après le délai de préparation) ;
   - le mode (sur place ou à emporter ; la livraison viendra en phase 7) ;
   - le paiement : **au comptoir seulement** pour l'instant (le paiement en ligne arrive en phase 4), plafond du comptoir compris ;
   - le minimum de commande, et `expectedTotal` s'il est envoyé (409 `storefront.checkout.price_changed` s'il diffère) ;
   - le client : nom, et téléphone ou e-mail (site et application). La borne ne demande rien.
3. **Acceptation** :
   - **automatique** : la commande est acceptée aussitôt et le ticket part en cuisine ;
   - **manuelle** : elle apparaît dans **Nouvelles** avec la sonnerie ; sans acceptation avant l'échéance (10 minutes par défaut), elle est annulée avec le motif « Non acceptée à temps ».
4. **Suivi** (`GET /orders/{jeton}`) : statut, lignes, total, reste à payer au comptoir, heure estimée, annulation possible ou non.
5. **Annulation par le client** (`POST /orders/{jeton}/cancel`) : seulement avant l'acceptation (409 ensuite).

Le client reçoit les notifications existantes : confirmation à la validation, commande prête, annulation.

### Routes ajoutées (clé publique)

| Méthode | Route | Rôle |
|---|---|---|
| POST | `/v1/storefront/cart/quote` | Devis du panier |
| POST | `/v1/storefront/orders` | Validation (Idempotency-Key) ; renvoie numéro, total et `trackingToken` |
| GET | `/v1/storefront/orders/{jeton}` | Suivi |
| POST | `/v1/storefront/orders/{jeton}/cancel` | Annulation avant acceptation |

Exemple de validation :

```json
{
  "locationId": "…",
  "mode": "Takeaway",
  "payment": "Counter",
  "lines": [{ "productId": "…", "variantId": "…", "modifierIds": ["…"], "quantity": 2, "note": "Sans piment" }],
  "customer": { "name": "Awa", "phone": "+229 90 00 00 01", "marketingConsent": true },
  "scheduledForUtc": null,
  "expectedTotal": 9000
}
```

### À faire sur le Mac

1. **Générer la migration** : `./scripts/migrations-commande-invite.sh` (table `storefront.online_orders`).
2. **Configurer la clé des jetons de suivi** : `Storefront:TrackingKey`, 32 octets en Base64 (`openssl rand -base64 32`). **Obligatoire** : sans elle, l'API ne démarre pas. Une clé de développement est dans `appsettings.Development.json` ; en production, variable d'environnement `Storefront__TrackingKey`.
3. **Compiler et tester** : `dotnet build`, puis `dotnet test`.

### Limites connues

- **Notification de confirmation** : en validation manuelle, elle part dès la validation, avant l'acceptation.
- **Borne** : pas de prénom demandé ; ticket imprimé depuis la phase 6.
- **Reprise** : si l'enregistrement de la commande en ligne échoue juste après sa validation dans Orders, la commande reste dans **Nouvelles** sans échéance ; le personnel la traite comme une commande au comptoir.

## Phase 4 : paiement en ligne

### Ce qui est en place

| Domaine | Ajout |
|---|---|
| **Payments** | `StartOnlinePaymentCommand` et `RetryPaymentCommand` acceptent une **page de retour propre à l'encaissement** (`ReturnUrl`), transmise aux prestataires (FedaPay, KkiaPay, CinetPay, Stripe, PayPal). Sans elle, la page de la plateforme (`Payments:CheckoutReturnUrl`). Aucune route du personnel ne la reçoit |
| **Storefront** | Commande « payée en ligne » : validée sans être acceptée, en attente de paiement jusqu'à l'échéance (`UnpaidExpiryMinutes`, 15 min par défaut). Paiement démarré par le jeton de suivi. À l'encaissement : acceptation automatique, ou délai d'acceptation manuelle qui part du paiement. Échéance dépassée : paiements en attente annulés, commande annulée. Refus ou annulation d'une commande payée : **remboursement** |
| **Hôte** | Pont vers Payments (prestataires de l'établissement, démarrage ou reprise d'un paiement échoué, état, annulation, remboursement). Flux `payments.PaymentSettlementChanged` → vente en ligne |
| **Console** | **Commandes** : « Paiement en ligne en attente » (pas de bouton Accepter tant que ce n'est pas payé), « À accepter avant HH:MM » ; la sonnerie ne retentit qu'une fois la commande payée |
| **Correction** | Le test `Cash_received_at_the_counter_settles_the_whole_order_by_default` vérifiait les appels après une lecture qui en ajoute un : l'ordre des vérifications est corrigé |

### Déroulé

1. `POST /orders` avec `"payment": "Online"` : la commande est créée (statut « reçue ») avec `payBeforeUtc`.
2. `POST /orders/{jeton}/payments` (Idempotency-Key) avec `provider`, `method`, `payerPhone` (mobile money) et `returnUrl`. La page de retour doit être **déclarée** pour l'application (comparée sans les paramètres : déclarez `https://site/commande`, envoyez `https://site/commande?jeton=…`). Réponse : `checkoutUrl` où envoyer le client, ou `null` quand la demande arrive sur son téléphone.
3. Le prestataire renvoie le client sur `returnUrl&paiement=<id>`. Le suivi (`GET /orders/{jeton}`) donne `paymentState` : `AwaitingPayment`, `Processing`, `Paid`, `Refunded` (ou `AtCounter`).
4. Échec du paiement : le client réessaie (le paiement échoué est repris chez le même prestataire, un paiement resté en attente est remplacé).

Les prestataires proposés sont ceux que le restaurant a configurés et activés dans **Paramètres › Paiements**, pour la devise et l'établissement (`onlinePaymentOptions` du devis, `paymentOptions` du suivi).

### Route ajoutée (clé publique)

| Méthode | Route | Rôle |
|---|---|---|
| POST | `/v1/storefront/orders/{jeton}/payments` | Démarre ou reprend le paiement en ligne |
| GET | `/v1/online-sales/locations/{id}/waiting-orders` | Console : commandes en attente de paiement ou d'acceptation |

## Phase 5 : site de commande (implémentation de référence)

Dans `restaurantos-site`, sans compte, aux couleurs du restaurant :

| Page | Rôle |
|---|---|
| `/commander` | Choix de l'établissement (redirection directe s'il n'y en a qu'un) |
| `/commander/{établissement}` | Menu par catégories, fiche produit (variante, options avec leurs règles, quantité, note, allergènes), panier gardé dans le navigateur, validation : devis du serveur, mode, « dès que possible » ou heure, coordonnées, consentement, paiement au comptoir ou en ligne |
| `/commande?jeton=…` | Suivi rafraîchi toutes les 5 s : étapes, numéro, heure estimée, paiement (payer, réessayer), annulation tant que la commande n'est pas acceptée. C'est aussi la page de retour du paiement |

Le code d'appel est dans `restaurantos-site/lib/storefront.ts` (clé, erreurs traduites, ETag du catalogue, idempotence), réutilisable par une agence.

### Mise en route

1. Console › **Paramètres › Vente en ligne** : créer l'application « Site web », déclarer l'origine du site (`http://localhost:3002` en développement) et la page de retour `<site>/commande`.
2. Dans `restaurantos-site/.env.local` : `NEXT_PUBLIC_STOREFRONT_KEY=pk_live_…` et `NEXT_PUBLIC_API_URL`.
3. `pnpm dev`, puis ouvrir `/commander`.

### À faire sur le Mac (phases 4 et 5)

1. **Migration** : `./scripts/migrations-paiement-en-ligne.sh` (colonnes de paiement de `online_orders`), après celles des phases 2 et 3 si elles ne sont pas encore générées.
2. **Compiler et tester** : `dotnet build`, puis `dotnet test`.
3. **Essai de bout en bout** : prestataire `sandbox` activé pour le restaurant ; sans `Payments:SandboxCheckoutBaseUrl`, le paiement reste « en cours » jusqu'au webhook signé du bac à sable.

### Limites connues

- **Paiement capturé après l'annulation** (client très en retard) : signalé par Payments (`PaymentCapturedAfterCancellation`), le remboursement se fait depuis la console.
- **Notification de confirmation** : envoyée à la validation, avant le paiement.
- **Créneaux** : l'heure choisie est seulement vérifiée contre l'ouverture et le délai de préparation (capacité par créneau en phase 7).

## Phase 6 : borne et impression

### Ce qui est en place

| Domaine | Ajout |
|---|---|
| **Printing** (nouveau module) | Imprimantes par établissement, de deux types : **Star CloudPRNT** (l'imprimante interroge l'API par Internet) et **ESC/POS par relais** (réseau local, port 9100). Jeton `prt_…` dont seule l'empreinte est gardée, placé dans l'URL de l'imprimante ; nouveau jeton à tout moment. File de tickets : renvoi après 2 minutes sans accusé, 5 tentatives au plus, expiration après 2 heures. Rendu StarPRNT, ESC/POS et texte brut (48 colonnes, accents en Windows-1252, code-barres Code 128, coupe). Page de test, réimpression |
| **Storefront** | **Appairage des bornes** : la tablette affiche un code à 6 chiffres (10 minutes), le personnel le saisit dans la console en choisissant l'établissement, la tablette retire alors son **jeton d'appareil** `kd_…` (une seule fois, empreinte seule gardée). Nombre de bornes limité par le droit `storefront.kiosk`. Une borne ne voit et ne commande que pour **son** établissement ; révoquée, elle s'arrête aussitôt. **Ticket client** : au comptoir dès la validation (« À PAYER AU COMPTOIR »), payée par QR à l'encaissement (« PAYÉE ») |
| **Hôte** | Schéma `PrinterToken` (jeton dans l'URL des routes d'imprimante), jamais par défaut, 120 requêtes par minute par imprimante. Le jeton est remplacé par `{printerToken}` dans les traces. En-tête `X-Kiosk-Device` lu par l'authentification `StorefrontKey` et autorisé par le CORS. Une imprimante absente ou en erreur ne fait jamais échouer une commande |
| **Console** | **Paramètres › Imprimantes** : création (adresse affichée une fois, avec la marche à suivre CloudPRNT ou relais), état en ligne, test, coupure, nouvelle adresse, derniers tickets et réimpression. **Paramètres › Vente en ligne › Bornes** : appairage par code, liste, révocation, compteur de l'option. Adresse de la borne affichée avec la clé d'une application « Borne » |
| **Site** | **`/borne`** : PWA plein écran (manifeste, écran gardé allumé). Accueil aux couleurs du restaurant avec choix du mode, menu par catégories en grandes tuiles, fiche produit, récapitulatif, paiement **au comptoir** ou **par téléphone** (QR code de la page du prestataire, suivi jusqu'au paiement, annulation), écran du numéro, retour à l'accueil après 20 s ou après 90 s d'inactivité (avec « Toujours là ? »). Aucune donnée personnelle demandée |
| **Relais** | `tools/print-relay/relay.mjs` (Node 20, sans dépendance) et son README (systemd) |

### Ticket client

```
                    Chez Awa
                    Cotonou

                    COMMANDE
                     A-042
                   SUR PLACE
------------------------------------------------
2 x Burger Awa · Double             11 000 F CFA
   Frites, Œuf
1 x Bissap                             500 F CFA
------------------------------------------------
TOTAL                               11 500 F CFA

              À PAYER AU COMPTOIR

               ||| || ||||| |||
                     A-042
                12/10/2026 18:47
```

Rendu texte ; sur l'imprimante, le nom du restaurant, le numéro et l'état du paiement sont en gras, le numéro et l'état en double taille, puis la coupe. Ni nom, ni téléphone, ni e-mail : seulement le numéro, le mode, les lignes, le total, l'état du paiement, le code-barres du numéro et l'heure locale de l'établissement.

### Mise en route

1. **Super-admin** : option « Borne » avec `storefront.kiosk=<nombre de bornes>`, ajoutée au restaurant.
2. **Console › Paramètres › Imprimantes** : créer l'imprimante du comptoir, cocher « Imprime les tickets des bornes ».
   - Star (mC-Print3, TSP143IV…) : coller l'adresse dans **CloudPRNT › Server URL**, intervalle 5 s.
   - Epson ou compatible : lancer le relais (`tools/print-relay/README.md`).
   - Puis « Test ».
3. **Console › Paramètres › Vente en ligne** : créer une application « Borne », copier l'**adresse de la borne** (`<site>/borne#cle=pk_live_…` ; la clé est dans le fragment, jamais envoyée au serveur du site).
4. **Sur la tablette** : ouvrir cette adresse, l'ajouter à l'écran d'accueil (plein écran), puis saisir dans la console (**Bornes › Appairer une borne**) le code affiché.
5. **Réglages de l'établissement** : modes proposés (sur place, à emporter) et règles du canal Borne (paiement au comptoir, plafond, paiement en ligne). Pour le paiement par QR, un prestataire doit être actif dans **Paramètres › Paiements**.

`NEXT_PUBLIC_SITE_URL` (console) donne l'adresse du site utilisée pour l'adresse de la borne.

### Routes ajoutées

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/v1/printing/printers` | Console (`printing.view`) : imprimantes, état en ligne |
| POST | `/v1/printing/printers` | Créer (`printing.manage`), renvoie le jeton une fois |
| PUT | `/v1/printing/printers/{id}` | Nom, tickets des bornes |
| POST | `/v1/printing/printers/{id}/token` · `/disable` · `/enable` · `/test` | Nouveau jeton, coupure, page de test |
| GET | `/v1/printing/jobs` | Derniers tickets |
| POST | `/v1/printing/jobs/{id}/reprint` | Réimpression |
| POST · GET · DELETE | `/v1/printing/cloudprnt/{jeton}` | Star CloudPRNT : annonce, ticket (`?type=&token=`), accusé (`?code=&token=`) |
| GET | `/v1/printing/relay/{jeton}/next` | Relais : ticket suivant en ESC/POS (204 sans ticket) |
| POST | `/v1/printing/relay/{jeton}/jobs/{id}` | Relais : accusé `{ "printed": true }` ou `{ "printed": false, "code": "printer_unreachable" }` |
| GET | `/v1/online-sales/kiosks` | Console : bornes, limite de l'option |
| POST | `/v1/online-sales/kiosks/pair` | Console : `{ "code": "123456", "locationId": "…", "name": "Borne entrée" }` |
| POST | `/v1/online-sales/kiosks/{id}/revoke` | Console : révoquer |
| POST | `/v1/storefront/kiosk/pairings` | Borne non appairée : code, secret, échéance |
| POST | `/v1/storefront/kiosk/pairings/{id}/claim` | Borne : `{ "secret": "…" }` → `Pending`, `Expired`, ou `Paired` avec le jeton d'appareil |

### Sécurité

- **Jetons** (`prt_`, `kd_`), **codes et secrets d'appairage** : seules leurs empreintes SHA-256 sont gardées ; ils n'apparaissent ni dans les journaux, ni dans les événements, ni dans les traces. Le code d'appairage est haché avec le restaurant : le même code ne vaut que pour l'application qui l'a demandé.
- **Restaurant** : celui de la clé de l'application ou du jeton de l'imprimante, jamais une valeur de la requête.
- **Jeton d'appareil** accepté seulement avec la clé de **son** application borne, si la borne est active et que l'application sert toujours son établissement ; sinon toute la requête est refusée (401).
- **Borne non appairée** : elle ne peut que demander un code (`storefront.kiosk.device_required` ailleurs).
- **Établissement** : celui de l'imprimante ou de la borne est vérifié contre le restaurant du contexte.

### À faire sur le Mac

1. **Migrations** : `./scripts/migrations-bornes-impression.sh` (Printing `InitialCreate` : `printing.printers`, `printing.print_jobs` ; Storefront `Kiosks` : `storefront.kiosk_devices`, `storefront.kiosk_pairings`).
2. **Dépendances du site** : `cd restaurantos-site && pnpm install` (ajout de `qrcode`).
3. **Compiler et tester** : `dotnet build`, puis `dotnet test`. Au démarrage, les rôles Owner et Manager reçoivent `printing.view` et `printing.manage`.

### Limites connues

- **Prénom sur le ticket** : non demandé à la borne (aucune donnée personnelle) ; le client est appelé par son numéro.
- **Paiement par carte sur terminal** : pas de TPE intégré ; « au comptoir » couvre espèces, mobile money ou carte encaissés par le personnel.
- **Ticket « PAYÉE »** : la borne ne sait pas s'il a été imprimé ; elle invite à retenir le numéro.
- **État détaillé de l'imprimante** (papier, capot) : seulement via le code d'accusé CloudPRNT ou l'échec TCP du relais.

## Phase 7 : livraison, coupons et créneaux

### Ce qui est en place

| Domaine | Ajout |
|---|---|
| **Orders** | Le devis (`QuoteOrderQuery`) applique une remise et des frais de livraison donnés, avec les règles de la commande (plafond, répartition, taxe). Rejouer une remise déjà posée répond « déjà posée » même après la validation : le flux `PromotionApplied` ne rend plus l'usage d'un coupon posé au checkout |
| **Promotions** | `PreviewCartCouponQuery` : ce que vaudrait un coupon sur un panier qui n'est pas encore une commande (coupon actif et non épuisé, promotion active, période, limite d'usage, établissement, mode, minimum, produits), sans rien compter |
| **Delivery** | Livraison programmée : `CreateDeliveryCommand` accepte l'heure demandée (`DeliverAt`, 7 jours au plus) ; l'heure promise est la plus tardive entre elle et préparation + trajet de la zone |
| **Storefront** | **Livraison** : la livraison n'est proposée que si les réglages en ligne la proposent ET que l'établissement livre (zones actives de Livraison & retrait) ; jamais à la borne. **Coupon** : accepté ou refusé (avec le motif) au devis, compté à la validation. **Créneaux** de 15 minutes sur 7 jours, dans les horaires et le fuseau de l'établissement, avec une **capacité par créneau** facultative. Le suivi montre la remise, la zone, les frais, l'étape de livraison et l'heure promise, sans adresse ni livreur |
| **Hôte** | À la validation, sur le brouillon et AVANT de le valider : coupon compté par Promotions puis posé (même provenance que le flux de l'outbox), fiche de livraison créée par Delivery puis frais posés. Le total validé, donc payé, est celui du devis. En cas d'échec, le brouillon est annulé : l'annulation rend le coupon et annule la fiche |
| **Console** | Réglages par établissement : « Commandes par créneau » (vide = sans limite) et rappel que la livraison en ligne suit les zones de Livraison & retrait |
| **Site** | Checkout : mode Livraison avec quartier (frais, minimum, durée), adresse, repère et consignes ; code promo (accepté avec sa remise, ou motif en clair) ; « Dès que possible » ou créneau (les créneaux complets sont grisés) ; sous-total, remise et frais détaillés ; paiement « à la livraison ». Suivi : étape de livraison et zone |

### Calcul du total

```
total = lignes − remise du coupon + frais de la zone
```

- Le **minimum** (établissement, et zone de livraison) se compare au total **hors frais**, après la remise ; c'est la règle de la fiche de livraison.
- Le **plafond du comptoir** se compare au total **avec** les frais.
- `expectedTotal` envoyé à la validation doit être ce total (409 `storefront.checkout.price_changed` sinon).

### Créneaux

- Une commande occupe le créneau de l'heure où elle doit être **prête** : dès que possible = maintenant + préparation ; à emporter programmée = l'heure demandée ; livraison programmée = l'heure demandée moins le trajet de la zone.
- `GET /slots` renvoie l'heure **proposée au client** : de retrait, ou de livraison (créneau de cuisine + trajet de la zone).
- Une commande refusée faute d'acceptation ou annulée par le client libère son créneau. Deux validations simultanées sur la dernière place peuvent dépasser la capacité d'une commande.

### Routes ajoutées ou modifiées (clé publique)

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/v1/storefront` | Chaque établissement porte `delivery` (préparation, zones : frais, minimum, durée) et `slotCapacity` ; `Delivery` n'est dans `serviceModes` que s'il livre vraiment |
| POST | `/v1/storefront/cart/quote` | Nouveaux champs : `couponCode`, `deliveryZoneId` ; réponse : `discountTotal`, `deliveryFee`, `deliveryZoneId`, `coupon` (`code`, `isValid`, `refusalCode`, `label`, `discount`) |
| GET | `/v1/storefront/locations/{id}/slots?mode=Delivery&zoneId=…` | Créneaux des 7 prochains jours, avec `isAvailable` |
| POST | `/v1/storefront/orders` | Nouveaux champs : `couponCode`, `delivery` (`zoneId`, `addressLine`, `landmark`, `instructions`) ; en livraison, `scheduledForUtc` est l'heure de livraison |
| GET | `/v1/storefront/orders/{jeton}` | Nouveaux champs : `mode`, `discountTotal`, `delivery` (`zoneName`, `fee`, `status`, `promisedAtUtc`) |
| PUT | `/v1/online-sales/locations/{id}` | Console : `slotCapacity` (1 à 500, null = sans limite) |

Exemple de validation en livraison :

```json
{
  "locationId": "…",
  "mode": "Delivery",
  "payment": "Counter",
  "lines": [{ "productId": "…", "quantity": 2 }],
  "customer": { "name": "Awa", "phone": "+229 90 00 00 01" },
  "delivery": { "zoneId": "…", "addressLine": "Rue 12, maison bleue", "landmark": "Face à la pharmacie" },
  "couponCode": "BIENVENUE",
  "scheduledForUtc": "2026-10-13T11:30:00Z",
  "expectedTotal": 7000
}
```

### Refus ajoutés

| Code | Quand |
|---|---|
| `storefront.checkout.delivery_zone_required` | Livraison sans zone |
| `storefront.checkout.delivery_zone_unavailable` | Zone inconnue ou désactivée |
| `storefront.checkout.delivery_address_required` | Livraison sans téléphone ou sans adresse |
| `storefront.checkout.slot_full` (409) | Le créneau est complet |
| `promotions.coupon.*`, `promotions.discount.*` | Coupon refusé à la validation (même motif qu'au devis) |
| `storefront.settings.slot_capacity_invalid` | Console : capacité hors de 1 à 500 |

`storefront.checkout.delivery_unavailable` n'existe plus : un établissement qui ne livre pas répond `storefront.checkout.service_mode_unavailable`.

### Sécurité

- **Adresse et téléphone de livraison** : seulement dans la fiche de Delivery (comme une livraison saisie au comptoir) ; jamais dans un journal, un événement, une trace ni le suivi public. Les DTO qui les portent masquent leur `ToString`.
- **Coupon** : la remise est toujours calculée par Promotions sur les lignes recalculées par le serveur ; le client n'envoie qu'un code.
- **Restaurant** : celui de la clé ; un coupon ou une zone d'un autre restaurant n'est jamais trouvé.

### À faire sur le Mac

1. **Migration** : `./scripts/migrations-livraison-coupons.sh` (Storefront `OrderSlots` : `online_ordering_settings.slot_capacity`, `online_orders.slot_start_utc` et son index). Aucun changement de schéma pour Orders, Promotions et Delivery.
2. **Compiler et tester** : `dotnet build`, puis `dotnet test`.
3. **Essai** : dans Livraison & retrait, activer la livraison avec au moins une zone ; dans Paramètres › Vente en ligne, cocher Livraison ; dans Marketing, créer une promotion « avec coupon » et un code.

### Limites connues

- **Promotions automatiques** (sans coupon) : pas appliquées en ligne ; seul un coupon saisi l'est. Un seul coupon par commande.
- **Adresse** : texte libre + quartier choisi dans une liste ; pas de géolocalisation ni de calcul de distance.
- **Heure de la commande programmée** : en livraison, `ScheduledForUtc` de la commande est l'heure de livraison ; la cuisine voit cette heure.
- **Capacité** : comptée par créneau sur les commandes en ligne seulement (pas les commandes du comptoir).
- **Fuseau du suivi** : les heures du suivi s'affichent dans le fuseau du téléphone du client ; les créneaux, dans celui de l'établissement.

## Phase 8 : comptes clients

### Ce qui est en place

| Domaine | Ajout |
|---|---|
| **Storefront** | Connexion **sans mot de passe** par code à usage unique (SMS, WhatsApp ou e-mail) sur un site ou une application, jamais à la borne. Le code prouvé rattache le client à **sa fiche** Customers (même e-mail ou même téléphone) ; sans fiche, une fiche est créée avec le nom donné. Une **session client** (`customer_sessions`) est ouverte pour l'application : jeton `cst_…` remis une seule fois, empreinte SHA-256 seule gardée, 60 jours, révoquée à la déconnexion |
| **Checkout** | Client connecté : la commande est passée sur **sa fiche** (coordonnées du profil si non saisies, téléphone du profil en livraison) ; consentement marketing accordé sur sa fiche s'il le coche. Une **récompense fidélité** peut être choisie au devis et à la validation |
| **Hôte** | Codes envoyés par Notifications (objet `customer_sign_in`, limites de Notifications). À la validation, sur le brouillon et AVANT de le valider : points débités par Loyalty (référence = la commande), puis remise posée avec la même provenance que le flux `RewardRedeemed`. En cas d'échec, le brouillon est annulé (comme pour le coupon) |
| **Site** | Page `/compte` : connexion (canal, numéro ou e-mail, code, nom si nouveau, consentement), profil, déconnexion, points et récompenses, dernières commandes avec lien de suivi. Checkout : lien « Mon compte », coordonnées pré-remplies, choix « Garder mes points » ou d'une récompense ; un invité voit « Connectez-vous pour retrouver vos points » |

### Déroulé de la connexion

1. `POST /customer/code` (`channel`, `address`) : Notifications envoie le code ; la réponse est un **ticket** chiffré (AES-GCM : restaurant, application, défi, canal, adresse, échéance) et la destination masquée. La réponse est la même que le contact soit connu ou non.
2. `POST /customer/code/verify` (`ticket`, `code`, `marketingConsent`) :
   - fiche trouvée : `SignedIn` avec `token`, `expiresAtUtc` et le compte ;
   - aucune fiche : `NameRequired` avec un ticket **déjà vérifié** (10 minutes), à renvoyer avec `name` et sans code.
3. Ensuite, chaque appel envoie l'en-tête `X-Customer-Token`. Un jeton inconnu, expiré ou révoqué laisse la requête **en invité** ; seules les routes du compte répondent alors 403 `storefront.customer.sign_in_required` (le site oublie alors le jeton).

### Calcul du total

```
total = lignes − remise du coupon − remise de la récompense + frais de la zone
```

La récompense doit être active, dans la devise de l'établissement, et le solde suffisant ; le débit des points se fait à la validation seulement (le devis ne débite rien).

### Routes ajoutées ou modifiées (clé publique)

| Méthode | Route | Rôle |
|---|---|---|
| POST | `/v1/storefront/customer/code` | Demande d'un code (`channel` : `Sms`, `WhatsApp`, `Email` ; `address` ; `language`). Limitée à 30 demandes par 10 minutes par application et adresse IP |
| POST | `/v1/storefront/customer/code/verify` | Vérification (`ticket`, `code`, `name`, `marketingConsent`) ; même limite |
| GET | `/v1/storefront/customer` | Compte : `name`, `email`, `phone`, `currencyCode`, `loyalty` (`enabled`, `balance`, `pointsPerCurrencyUnit`, `rewards` avec `affordable`) |
| GET | `/v1/storefront/customer/orders?take=20` | Dernières commandes (50 au plus) dans les établissements de l'application, avec `trackingToken` pour une commande passée en ligne |
| POST | `/v1/storefront/customer/sign-out` | Révoque la session (204) |
| POST | `/v1/storefront/cart/quote` | Nouveau champ `rewardId` ; réponse : `reward` (`rewardId`, `name`, `pointCost`, `discount`) |
| POST | `/v1/storefront/orders` | Nouveau champ `rewardId` (client connecté seulement) ; `customer` facultatif pour un client connecté |

### Refus ajoutés

| Code | Quand |
|---|---|
| `storefront.customer.sign_in_required` (403) | Route du compte ou récompense sans session valide |
| `storefront.customer.ticket_invalid` | Ticket expiré, falsifié ou d'une autre application |
| `storefront.customer.address_invalid` | Canal et adresse incohérents (e-mail pour `Email`, téléphone sinon) |
| `storefront.customer.code_required`, `storefront.customer.name_required` | Code ou nom manquant |
| `storefront.customer.kiosk_not_allowed` | Connexion depuis une borne |
| `storefront.checkout.reward_unavailable` | Récompense inconnue, désactivée ou dans une autre devise |
| `storefront.checkout.reward_not_affordable` | Solde de points insuffisant |
| `notifications.otp.*` | Code incorrect, expiré, déjà utilisé, trop d'essais ou de demandes, envoi impossible |

### Sécurité

- **Jeton client** : seule l'empreinte est stockée ; jamais journalisé, ni dans un événement, une trace ou une exception. Une session est liée à UN restaurant et UNE application : le jeton d'un autre site ne fonctionne pas.
- **Code** : jamais journalisé ; vérifié par Notifications (essais limités). Le ticket ne révèle ni l'adresse ni le défi au client.
- **Énumération** : la demande de code répond pareil pour un contact connu ou inconnu.
- **Restaurant** : celui de la clé ; une fiche, un compte fidélité ou une récompense d'un autre restaurant n'est jamais trouvé. Une fiche anonymisée ne se connecte plus.
- **Borne** : aucun compte client (le site n'envoie jamais le jeton depuis une borne).

### À faire sur le Mac

1. **Migration** : `./scripts/migrations-comptes-clients.sh` (Storefront `CustomerSessions` : table `customer_sessions`, index unique `ux_customer_sessions_token_hash`). Aucun changement de schéma pour Customers, Loyalty et Notifications.
2. **Compiler et tester** : `dotnet build`, puis `dotnet test`.
3. **Essai** : un fournisseur SMS, WhatsApp ou e-mail configuré dans Notifications ; dans Marketing › Fidélité, activer le programme et créer une récompense ; sur le site, `/compte`.

### Limites connues

- **Points gagnés** : crédités par Loyalty selon ses règles existantes (commande remise), pas au moment de la validation.
- **Une récompense** par commande ; pas de cumul de plusieurs récompenses.
- **Modifier son profil** (nom, e-mail, téléphone) : pas depuis le site ; le restaurant le fait dans la console.
- **Sessions** : pas de liste des appareils connectés ni de déconnexion de tous les appareils.
- **Historique** : les commandes passées au comptoir sur la fiche s'affichent sans lien de suivi.

## Options de vente en ligne : souscription et facturation

### Ce qui est en place

| Où | Ajout |
|---|---|
| **Catalogue** | `scripts/plans/catalogue.json` contient maintenant 6 options mensuelles (`"kind": "AddOn"`) : **Site web** 3 000 XOF / 5 € (`storefront.web`), **Application mobile** 10 000 XOF / 15 € (`storefront.mobile_app`), **Borne** 7 500 XOF / 12 € par borne (`storefront.kiosk`, limite 1 multipliée par la quantité), **API** 15 000 XOF / 25 € (`storefront.api` : obligatoire pour créer une clé d'API, `integrations.api_credential.option_required` sinon ; les clés existantes restent valides). `scripts/seed-plans.py` les crée (le type fait partie de la clé de comparaison) |
| **Billing, restaurant** | Le restaurateur souscrit une option, change sa quantité ou la résilie lui-même. Activation **immédiate**, pendant l'essai ou une période payée (en retard ou suspendu : régulariser d'abord). Résiliation immédiate, possible même suspendu |
| **Billing, facturation** | Le paiement enregistré par la plateforme pour une période = **plan + options actives × quantité**, recopiés ligne par ligne (`subscription_payment_lines`). Une option mensuelle sur un plan annuel compte 12 mois ; une option annuelle sur un plan mensuel, un douzième. `nextCharge` donne le montant de la prochaine période |
| **Billing, résiliation** | La résiliation de l'abonnement (par la plateforme, ou programmée puis échue) résilie ses options |
| **Reporting** | Le MRR de la plateforme compte les options (montant mensuel de l'abonnement = plan + options), recalculé à chaque souscription, changement ou résiliation d'option |
| **Console** | **Abonnement** : section **Options** (souscrire, quantité pour les bornes, résilier) ; détail de la prochaine période (plan et options) ; historique des paiements ligne par ligne. **Paramètres › Vente en ligne** : « Ajouter cette option » renvoie vers la section Options |
| **Super-admin** | Fiche restaurant, onglet Abonnement : options (ajouter, quantité, retirer), message quand le catalogue n'en contient pas, **prochaine échéance** ligne par ligne ; onglet Vente en ligne : options souscrites ; paiement et reçu PDF détaillés |

### Routes ajoutées

| Méthode | Route | Permission | Rôle |
|---|---|---|---|
| POST | `/v1/billing/add-ons` | `billing.manage` | Souscrire (`planId`, `quantity`) |
| PUT | `/v1/billing/add-ons/{id}/quantity` | `billing.manage` | Changer la quantité |
| POST | `/v1/billing/add-ons/{id}/cancel` | `billing.manage` | Résilier |
| GET | `/v1/admin/billing/subscriptions/{id}/charge` | administrateur | Prochaine période : lignes, total, montant mensuel |
| GET | `/v1/admin/billing/add-ons?page&pageSize` | administrateur | Options actives de tous les restaurants (écran Vente en ligne) |

`GET /v1/billing/subscription` renvoie aussi `nextCharge` ; les paiements (`/billing/payments`, `/admin/billing/payments`) portent leurs `lines`.

### Refus ajoutés

| Code | Quand |
|---|---|
| `billing.add_on.subscription_not_open` | Souscription ou quantité pendant un impayé ou une suspension |
| `billing.subscription_payment.add_on_of_another_tenant` | Garde-fou : une option d'un autre restaurant ne peut pas être facturée |

### À faire sur le Mac

1. **Migration** : `./scripts/migrations-options.sh` (Billing `SubscriptionPaymentLines` : table `subscription_payment_lines`).
2. **Compiler et tester** : `dotnet build`, puis `dotnet test`.
3. **Catalogue** : `python3 scripts/seed-plans.py --dry-run`, puis sans `--dry-run` pour créer les 8 options.

### Limites connues

- **Pas de prorata** : une option souscrite en cours de période est active tout de suite et facturée à la prochaine échéance ; résiliée, elle n'est pas remboursée.
- **Paiement** : toujours enregistré par la plateforme (pas encore de paiement en ligne de l'abonnement par le restaurant).
- **Bornes** : réduire la quantité sous le nombre de bornes appairées ne les déconnecte pas ; aucune nouvelle borne ne peut être appairée tant qu'il y en a trop.
- **Paiements antérieurs** : enregistrés sans détail (le plan seul).

## Suite

Phase 9 : application marque blanche.
