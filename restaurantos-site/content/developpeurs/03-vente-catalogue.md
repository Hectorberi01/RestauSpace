# Restaurant, établissements et carte

> Charger la marque du restaurant, ses établissements, leurs horaires et modes de service, puis la carte commandable.

Toutes les routes de cette page sont sous `{{API_URL}}/v1/storefront` et exigent `X-Storefront-Key`.

## Démarrage : `GET /`

Appelez-la une fois au lancement (et au retour au premier plan d'une application).

```bash
curl -s {{API_URL}}/v1/storefront/ -H "X-Storefront-Key: $KEY" -H "Accept: application/json"
```

```json
{
  "restaurant": {
    "name": "Chez Awa", "logoUrl": "https://…", "primaryColor": "#5b3df5", "currencyCode": "XOF", "pricesIncludeTax": true,
    "tagline": "Cuisine béninoise de quartier, depuis 2012",
    "description": "Chez Awa, c'est la cuisine de maman.\n\nAttiéké, poisson braisé et bissap maison.",
    "coverImageUrl": "{{API_URL}}/v1/media/Y292ZXI…",
    "links": [{"network": "Website", "url": "https://chezawa.bj/"}, {"network": "Instagram", "url": "https://instagram.com/chezawa"}]
  },
  "app": {"id": "0199…", "name": "Appli Chez Awa", "kind": "MobileApp", "humanCheckSiteKey": null},
  "orderingEnabled": true,
  "locations": [
    {
      "id": "0199…",
      "name": "Chez Awa Cotonou",
      "street": "Rue 12.150, Haie Vive",
      "city": "Cotonou",
      "timeZoneId": "Africa/Porto-Novo",
      "isOpenNow": true,
      "nextOpeningUtc": null,
      "weeklyHours": [{"day": "Monday", "start": "11:30", "end": "23:00", "endsNextDay": false}],
      "isPaused": false,
      "acceptsOrdersNow": true,
      "serviceModes": ["Takeaway", "Delivery"],
      "preparationMinutes": 20,
      "minimumOrderAmount": 3000,
      "payment": {"payOnline": true, "payAtCounter": true, "counterLimit": 50000},
      "delivery": {
        "leadMinutes": 10,
        "zones": [{"id": "0199…", "name": "Haie Vive", "fee": 1000, "minimumOrder": 5000, "estimatedMinutes": 30}]
      },
      "slotCapacity": 6,
      "place": {
        "postalCode": null,
        "countryCode": "BJ",
        "coordinates": {"latitude": 6.365536, "longitude": 2.418378},
        "contact": {"phone": "+2290197001122", "whatsApp": "+2290197001122", "email": "contact@chezawa.bj"}
      },
      "specialDays": [
        {"date": "2026-08-01", "isClosed": true, "ranges": [], "reason": "Fête de l'indépendance"},
        {"date": "2026-12-31", "isClosed": false, "ranges": [{"start": "19:00", "end": "02:00", "endsNextDay": true}], "reason": "Réveillon"}
      ],
      "closureReason": null
    }
  ]
}
```
| Champ | Utilisation |
|---|---|
| `restaurant.primaryColor`, `logoUrl` | Habiller l'interface aux couleurs du restaurant. |
| `restaurant.tagline`, `description`, `coverImageUrl` | Page d'accueil : accroche (120 caractères au plus), présentation en **texte brut** (une ligne vide sépare deux paragraphes : n'interprétez jamais ce texte comme du HTML), photo de couverture. Chacun peut être `null`. |
| `restaurant.links` | Site et réseaux sociaux, au plus un par réseau : `Website`, `Facebook`, `Instagram`, `TikTok`, `X`, `YouTube`, `GoogleMaps`. Ouvrez-les avec `rel="noopener"`. |
| `orderingEnabled` | `false` : le restaurant n'a pas souscrit l'option de ce canal (site, application ou borne). Affichez la carte en lecture seule. Une application désactivée ou un restaurant suspendu ne reçoit, lui, que des `401`. |
| `locations` | Seulement les établissements servis par l'application (une borne : le sien), triés par nom. |
| `isOpenNow`, `nextOpeningUtc` | « Ouvert » / « Ouvre à 11 h 30 ». |
| `isPaused` | Le restaurant a mis la commande en ligne en pause (rush, panne). |
| `acceptsOrdersNow` | La seule valeur à tester pour autoriser une commande « dès que possible » : canal actif, établissement ouvert, opérationnel et non en pause. |
| `serviceModes` | `DineIn` (sur place), `Takeaway` (à emporter), `Delivery` (livraison, seulement si l'établissement a des zones actives et jamais sur une borne). |
| `preparationMinutes` | Délai de préparation annoncé. |
| `minimumOrderAmount` | Minimum de commande (hors frais de livraison). |
| `payment` | Règles **de ce canal** : paiement en ligne, au comptoir, plafond du paiement au comptoir. |
| `delivery.zones` | Zones de livraison : frais, minimum, durée estimée. Pas de géolocalisation : le client choisit sa zone. |
| `slotCapacity` | Nombre maximum de commandes par créneau de 15 minutes (`null` : illimité). |
| `place.coordinates` | Position (degrés décimaux) pour un plan et un bouton « Itinéraire » ; `null` si le restaurant ne l'a pas renseignée : utilisez alors l'adresse. |
| `place.contact` | Téléphone et WhatsApp au format international (`tel:+229…`, `https://wa.me/229…`), e-mail ; chacun peut être `null`. |
| `place.countryCode`, `postalCode` | Pays (ISO 3166-1 alpha-2) et code postal, pour l'adresse complète. |
| `specialDays` | Jours aux horaires exceptionnels des 60 prochains jours (voir ci-dessous). |
| `closureReason` | Motif d'une fermeture temporaire en cours (travaux, congés) ; `null` sinon. |

Valeurs par défaut d'un établissement que le restaurant n'a pas réglé : pas de pause, « à emporter » seulement,
préparation 20 minutes, paiement en ligne et au comptoir, acceptation automatique.

Un seul établissement : `GET /locations/{locationId}` renvoie le même objet (`404 storefront.location.not_found` s'il
n'est pas servi par l'application).

## La carte : `GET /locations/{locationId}/catalog`

```bash
curl -s {{API_URL}}/v1/storefront/locations/$LOCATION/catalog \
  -H "X-Storefront-Key: $KEY" -H 'If-None-Match: "3f1c…"' -i
```

```json
{
  "locationId": "0199…",
  "currencyCode": "XOF",
  "pricesIncludeTax": true,
  "menus": [
    {"id": "0199…", "name": "Carte du midi", "sections": [{"id": "0199…", "name": "Plats", "productIds": ["0199…", "0199…"]}]}
  ],
  "products": [
    {
      "id": "0199…",
      "name": "Poulet braisé",
      "description": "Servi avec alloco",
      "price": 4000,
      "isAvailable": true,
      "variants": [{"id": "0199…", "name": "Grande portion", "price": 5500, "isAvailable": true}],
      "modifierGroupIds": ["0199…"],
      "allergens": ["gluten"],
      "images": ["{{API_URL}}/v1/media/aGVsbG8…"],
      "thumbnails": ["{{API_URL}}/v1/media/dGh1bWI…"]
    }
  ],
  "modifierGroups": [
    {
      "id": "0199…", "name": "Sauce", "minSelections": 0, "maxSelections": 2,
      "modifiers": [{"id": "0199…", "name": "Piment", "priceDelta": 0, "isAvailable": true}, {"id": "0199…", "name": "Moyo", "priceDelta": 300, "isAvailable": true}]
    }
  ],
  "version": "3f1c9a0b5d2e4f60718293a4b5c6d7e8"
}
```

Comment l'afficher :

- **Structure** : `menus[].sections[].productIds` donne l'ordre ; les détails sont dans `products` et `modifierGroups`
  (référencés par id, sans doublon).
- **Prix** : `price` est le prix de l'établissement pour ce canal. Une déclinaison (`variants`) a son **prix complet**
  (pas un supplément). Un complément ajoute `priceDelta`.
- **Déclinaisons** : si un produit en a, le client doit en choisir une (`catalog.product.variant_required` sinon).
- **Compléments** : respectez `minSelections` / `maxSelections` de chaque groupe ; un complément ne se choisit qu'une
  fois.
- **Disponibilité** : `isAvailable: false` = épuisé aujourd'hui. Affichez-le grisé, sans permettre l'ajout.
- **Allergènes** : codes des 14 allergènes réglementaires : `gluten`, `crustaceans`, `eggs`, `fish`, `peanuts`,
  `soybeans`, `milk`, `nuts`, `celery`, `mustard`, `sesame`, `sulphites`, `lupin`, `molluscs`.
- **Images** : liens publics stables (mis en cache un an), la photo principale en premier ; `thumbnails` suit le même
  ordre (une miniature peut être `null` : prenez alors l'image).
- **Cache** : gardez la carte et son `ETag` ; rechargez-la avec `If-None-Match` à l'ouverture de l'écran et après un
  refus de commande lié à un produit.

Les produits en brouillon, archivés ou masqués sur ce canal n'apparaissent pas.

## Horaires et fuseau

`weeklyHours` liste les plages de chaque jour (`day` : `Monday` … `Sunday`), en heure locale de l'établissement
(`timeZoneId`). `endsNextDay: true` signale une plage qui se termine après minuit. Pour savoir si l'établissement est
ouvert, utilisez `isOpenNow` et `nextOpeningUtc`, calculés par la plateforme (fermetures temporaires comprises) plutôt
que de recalculer à partir de `weeklyHours`. Les créneaux de commande, eux, sont donnés par la route `/slots`.

`specialDays` annonce les jours qui ne suivent pas la semaine habituelle, dans les 60 prochains jours, triés par date
(date locale de l'établissement) :

- `isClosed: true` : fermé toute la journée (jour férié, congés) ;
- sinon `ranges` **remplace** les plages habituelles de ce jour-là (`endsNextDay` : la plage finit après minuit) ;
- `reason` : le motif saisi par le restaurant, à afficher tel quel (« Fermé le 1er août : Fête de l'indépendance »).

Affichez-les à côté des horaires (« Horaires exceptionnels ») et dans un bandeau quand l'un d'eux tombe dans les sept
prochains jours. Un établissement fermé temporairement n'a plus de `nextOpeningUtc` et porte son motif dans
`closureReason`.

## Menu à table (QR code)

Le QR code collé sur une table mène à votre page avec un jeton (`/table/{jeton}` sur le site de référence).

```bash
curl -s {{API_URL}}/v1/storefront/tables/$TABLE_TOKEN -H "X-Storefront-Key: $KEY"
```

```json
{"locationId": "0199…", "label": "T4", "areaName": "Terrasse"}
```

`404 storefront.table.not_found` : jeton inconnu, table hors service, ou établissement non servi par l'application. La
commande se passe ensuite avec `tableToken` (voir [Commande](04-vente-commande.md#commande-à-table)).
