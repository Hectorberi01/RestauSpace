# Construire le site d'un restaurant

> Les pages qu'un client attend du site d'un restaurant, les routes à appeler pour chacune, et ce qu'il faut prévoir
> pour le référencement, la conformité et la mise en ligne.

Ce guide assemble les pages de l'API de vente en ligne. Il suppose une application de vente de type **Site web**
(voir [Prise en main](01-prise-en-main.md)) et sa clé `pk_live_…`.

## Où appeler l'API

La clé publique peut être utilisée côté serveur (rendu des pages) **et** dans le navigateur :

- **Côté serveur** (Next.js, Nuxt, Astro, PHP…) : un appel sans en-tête `Origin` est accepté. Utilisez-le pour tout ce
  qui doit être lu par les moteurs de recherche : accueil, carte, horaires, avis, établissements.
- **Dans le navigateur** : panier, devis, commande, paiement, suivi, compte client, réservation. L'adresse du site doit
  être déclarée sur l'application, sinon `401`.

Mettez en cache côté serveur : `GET /` et `GET /reviews` quelques minutes, la carte avec son `ETag`
([Cache de la carte](02-conventions.md#cache-de-la-carte)). Ne mettez jamais en cache une réponse qui dépend du client
(`X-Customer-Token`, jeton de suivi, jeton de réservation).

## Les pages et leurs routes

| Page | Ce qu'elle montre | Routes |
|---|---|---|
| Accueil | Nom, logo, couverture, accroche, présentation, ouvert ou fermé, note moyenne, quelques avis, boutons « Commander » et « Réserver » | `GET /`, `GET /reviews?take=3` |
| Établissements et contact | Adresse, plan, itinéraire, téléphone, WhatsApp, e-mail, horaires, jours exceptionnels | `GET /` (ou `GET /locations/{id}`) |
| Carte | Menus, sections, produits, photos, allergènes, prix, disponibilité | `GET /locations/{id}/catalog` |
| Produit | Déclinaisons, compléments, quantité, note pour la cuisine | Carte déjà chargée |
| Panier | Lignes, code promo, récompense, total calculé par le serveur | `POST /cart/quote` |
| Commande | Mode (à emporter, livraison, sur place), créneau, coordonnées, consentement, paiement | `GET /locations/{id}/slots`, `POST /orders` |
| Paiement | Choix du prestataire, retour du prestataire | `POST /orders/{token}/payments` |
| Suivi | Étape, heure prévue, paiement, annulation, puis avis | `GET /orders/{token}`, `POST /orders/{token}/cancel`, `POST /reviews` |
| Compte | Connexion par code, points, récompenses, historique | `/customer/…` |
| Réservation | Date, nombre de personnes, heure, coordonnées | `GET /locations/{id}/booking`, `POST /reservations` |
| Ma réservation | Détail, annulation | `GET /reservations/{token}`, `POST /reservations/{token}/cancel` |
| Avis | Note, répartition, avis publiés | `GET /reviews` |
| Menu à table | Carte et commande depuis le QR code d'une table | `GET /tables/{token}`, puis la carte et `POST /orders` |
| Mentions légales, confidentialité, conditions de vente | Textes du restaurant | Aucune : rédigés par le restaurant |

Un restaurant à plusieurs établissements : une page par établissement (`/cotonou-haie-vive`), et un choix
d'établissement mémorisé avant la carte. Un seul établissement : sautez ce choix.

## Accueil

```ts
const {restaurant, locations, orderingEnabled} = await storefront.bootstrap();
const reviews = await storefront.reviews({take: 3});
```

- **Couverture et accroche** : `restaurant.coverImageUrl`, `restaurant.tagline`. Sans photo, utilisez la couleur
  `primaryColor` en fond.
- **Présentation** : `restaurant.description` est du **texte brut**. Coupez-le sur les lignes vides pour faire des
  paragraphes ; ne l'insérez jamais comme du HTML (pas de `innerHTML` ni de `dangerouslySetInnerHTML`).
- **Ouvert / fermé** : `acceptsOrdersNow` décide du bouton « Commander maintenant » ; sinon « Ouvre à 11 h 30 »
  (`nextOpeningUtc`, affiché dans le fuseau `timeZoneId`), ou « Fermé : travaux » (`closureReason`).
- **Commande en ligne indisponible** (`orderingEnabled: false`) : montrez la carte sans bouton d'ajout, et le
  téléphone.
- **Note** : « 4,5 / 5 sur 128 avis » à partir de `averageRating` et `count` ; masquez le bloc tant que `count` vaut 0.
- **Réseaux** : `restaurant.links`, une icône par `network`.

## Contact, plan et horaires

```ts
function contactLinks(place: Place) {
  const {phone, whatsApp, email} = place.contact;
  return {
    call: phone ? `tel:${phone}` : null,
    whatsApp: whatsApp ? `https://wa.me/${whatsApp.replace('+', '')}` : null,
    mail: email ? `mailto:${email}` : null,
  };
}

function directions(location: Location) {
  const destination = location.place?.coordinates
    ? `${location.place.coordinates.latitude},${location.place.coordinates.longitude}`
    : `${location.street}, ${location.city}`;
  return `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(destination)}`;
}
```

- **Plan** : avec `place.coordinates`, affichez une carte (OpenStreetMap, Google Maps, Mapbox) centrée sur
  l'établissement ; sans coordonnées, l'adresse et le bouton « Itinéraire » suffisent. Si `restaurant.links` contient
  `GoogleMaps`, proposez aussi ce lien (fiche et avis Google).
- **Horaires** : regroupez `weeklyHours` par jour (Lundi… Dimanche, « Fermé » pour un jour absent) ; une plage avec
  `endsNextDay` s'affiche « 19:00 – 02:00 ».
- **Jours exceptionnels** : listez `specialDays` sous les horaires et affichez un bandeau quand l'un d'eux tombe dans
  les sept jours (« Fermé samedi 1er août : Fête de l'indépendance »). Voir
  [Horaires et fuseau](03-vente-catalogue.md#horaires-et-fuseau).

## Carte, commande et paiement

Suivez [Restaurant et carte](03-vente-catalogue.md), [Panier et commande](04-vente-commande.md) et
[Paiement en ligne](05-vente-paiement.md). Les points qui font un site fiable :

- le total affiché est **toujours** celui du devis, renvoyé dans `expectedTotal` ;
- une clé `Idempotency-Key` par clic sur « Commander », gardée tant que vous réessayez ;
- le jeton de suivi gardé dans l'appareil (pour rouvrir le suivi) et envoyé dans l'adresse de la page de suivi, pas
  dans vos journaux ;
- les allergènes affichés sur chaque produit, avec la mention « Liste des allergènes sur demande » si un produit n'en a
  aucun renseigné.

## Avis

- Accueil et page Avis : `GET /reviews` (voir [Avis clients](07-vente-reservations-questionnaires.md#avis-clients)).
  Les avis affichés sont ceux que le restaurant a **publiés** ; la note moyenne porte sur tous ses avis non archivés.
- Après une commande terminée (`status: "Completed"` sur la page de suivi) : demandez une note de 1 à 5 et un
  commentaire, puis `POST /reviews` avec le jeton de suivi. Précisez « Votre avis pourra être publié sur notre site,
  sans votre nom ».
- N'affichez jamais d'avis qui ne viennent pas de l'API comme s'il s'agissait d'avis de clients.

## Référencement

- **Rendu serveur** des pages publiques (accueil, établissements, carte, avis) : les moteurs de recherche lisent le
  contenu sans exécuter de JavaScript.
- **Une adresse stable** par établissement et pour la carte ; un `<title>` et une `<meta name="description">` par page
  (nom, ville, accroche).
- **Partage sur les réseaux** : `og:title` (nom), `og:description` (accroche), `og:image` (`coverImageUrl`).
- **`sitemap.xml`** avec l'accueil, les établissements, la carte, les avis et la réservation ; `robots.txt` qui exclut
  le panier, la commande, le suivi et le compte.
- **Données structurées** schema.org en JSON-LD, une par établissement :

```ts
export function restaurantJsonLd(info: StorefrontInfo, location: Location, siteUrl: string) {
  const {restaurant} = info;
  return {
    '@context': 'https://schema.org',
    '@type': 'Restaurant',
    name: `${restaurant.name} · ${location.name}`,
    description: restaurant.tagline ?? undefined,
    image: restaurant.coverImageUrl ?? restaurant.logoUrl ?? undefined,
    logo: restaurant.logoUrl ?? undefined,
    url: `${siteUrl}/${location.id}`,
    telephone: location.place?.contact.phone ?? undefined,
    email: location.place?.contact.email ?? undefined,
    address: {
      '@type': 'PostalAddress',
      streetAddress: location.street,
      addressLocality: location.city,
      postalCode: location.place?.postalCode ?? undefined,
      addressCountry: location.place?.countryCode,
    },
    geo: location.place?.coordinates
      ? {'@type': 'GeoCoordinates', latitude: location.place.coordinates.latitude, longitude: location.place.coordinates.longitude}
      : undefined,
    openingHoursSpecification: [
      ...location.weeklyHours.map(slot => ({'@type': 'OpeningHoursSpecification', dayOfWeek: slot.day, opens: slot.start, closes: slot.end})),
      ...(location.specialDays ?? []).map(day => day.isClosed
        ? {'@type': 'OpeningHoursSpecification', validFrom: day.date, validThrough: day.date, opens: '00:00', closes: '00:00'}
        : day.ranges.map(range => ({'@type': 'OpeningHoursSpecification', validFrom: day.date, validThrough: day.date, opens: range.start, closes: range.end}))).flat(),
    ],
    hasMenu: `${siteUrl}/${location.id}/carte`,
    acceptsReservations: true,
    sameAs: restaurant.links.filter(link => link.network !== 'Website').map(link => link.url),
  };
}
```

Insérez-le dans `<script type="application/ld+json">` (sérialisé avec `JSON.stringify`, en échappant `<` en
`<`). Retirez `acceptsReservations` si l'établissement ne prend pas de réservations en ligne. N'ajoutez pas
`aggregateRating` à partir de `GET /reviews` : les moteurs de recherche n'affichent pas les étoiles d'avis recueillis
par l'établissement lui-même et peuvent sanctionner ce balisage.

## Conformité et confiance

Ces textes ne viennent pas de l'API : le restaurant les fournit, vous les publiez.

- **Mentions légales** : l'identité de l'exploitant (raison sociale, adresse, immatriculation), un contact, l'hébergeur
  du site, selon les règles du pays.
- **Conditions de vente** : prix TTC ou HT (`pricesIncludeTax`), frais et zones de livraison (`delivery.zones`),
  minimum de commande, moyens de paiement, annulation (possible tant que `canCancel` vaut `true`), réclamations.
- **Confidentialité** : les données demandées (nom, téléphone ou e-mail, adresse de livraison), pourquoi (préparer et
  livrer la commande, confirmer une réservation), qui les reçoit (le restaurant, par RestauSpace, et le prestataire de
  paiement), la durée de conservation et la façon d'exercer ses droits (contacter le restaurant).
- **Consentement marketing** : une case **non cochée** par défaut, avec un texte clair (« Recevoir les offres de Chez
  Awa par SMS, WhatsApp ou e-mail »). Envoyez `marketingConsent: true` seulement si elle est cochée.
- **Cookies** : l'API n'en dépose aucun. Le contrôle anti-robot Cloudflare Turnstile, s'il est activé, et vos outils de
  mesure d'audience peuvent en déposer : informez-en le visiteur et demandez son accord quand la loi l'exige.
- **Paiement** : le client paie sur la page du prestataire ; votre site ne voit et ne stocke jamais de numéro de carte.
- **Données personnelles** : pas de nom, téléphone, e-mail, adresse, jeton ni code de connexion dans vos journaux, vos
  outils d'analyse ou les adresses des pages (sauf le jeton de suivi de la page de suivi).

## Accessibilité et performance

- Textes alternatifs sur les photos de produits (le nom du produit) ; boutons et champs avec un libellé.
- `thumbnails` dans les listes, `images` dans la fiche produit ; dimensions fixées pour éviter les décalages,
  chargement différé sous la ligne de flottaison.
- Contraste suffisant si vous utilisez `primaryColor` pour du texte ; à défaut, gardez-la pour les fonds et les
  boutons.
- Prix et heures formatés dans la langue du site (`Intl.NumberFormat` avec `currencyCode`, `Intl.DateTimeFormat` avec
  `timeZoneId`).

## Liste de contrôle avant la mise en ligne

1. L'adresse du site est déclarée sur l'application, et les pages de retour du paiement aussi.
2. La clé de production est dans la configuration du site, pas dans le dépôt Git.
3. Une commande à emporter, une livraison, un paiement en ligne et une annulation passent de bout en bout.
4. La page de suivi propose l'avis une fois la commande terminée.
5. Les horaires, les jours exceptionnels, le téléphone et le plan sont justes pour chaque établissement.
6. Mentions légales, conditions de vente et confidentialité sont en ligne et liées en pied de page.
7. Les erreurs s'affichent avec un message tiré de `code` ([Codes d'erreur](12-reference-erreurs.md)).
8. `sitemap.xml`, `robots.txt`, titres, descriptions et données structurées sont en place.
