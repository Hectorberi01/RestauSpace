# Modèle « Maquis » : site de restaurant branché sur RestauSpace

Site statique (Next.js, `output: 'export'`) pour un restaurant : accueil, carte, commande (sur place, à emporter,
livraison), paiement au comptoir ou en ligne, suivi de commande, réservation de table (avec acompte), compte client.

Tout ce qui concerne le restaurant vient de l'API de vente en ligne RestauSpace : nom, logo, accroche, adresse,
horaires, carte, photos, avis. Une modification dans la console RestauSpace apparaît sur le site sans le reconstruire.

## Prérequis

- Node.js 22 ou plus récent, npm.
- Une application de vente de type **Site web** dans la console RestauSpace, et sa clé publique `pk_live_…`.
  L'API est incluse dans les offres Pro et Multi-sites (option « API et webhooks » sur Essentiel).

## Installation

```bash
npm install
npm run dev      # http://localhost:3005
npm run build    # site prêt à publier dans out/
```

## Configuration : `site.config.json`

C'est le seul fichier à modifier.

| Clé | Rôle |
|---|---|
| `apiUrl` | Adresse de l'API (`https://api.restauspace.com`). |
| `publishableKey` | Clé publique de l'application « Site web » (`pk_live_…`). Ce n'est pas un secret. |
| `theme` | Couleurs hexadécimales : `primary` (boutons), `accent` (étoiles, motif), `dark` (bloc réservation), `background` (fond). |
| `texts` | Titre d'accueil (`heroTitle`, `heroAccent` en italique), texte affiché si le restaurant n'a pas d'accroche (`heroText`), titre et sous-titre des plats mis en avant. |
| `sections` | Blocs de l'accueil, dans l'ordre : `hero`, `featured`, `booking`, `hours`, `reviews`. Retirez ceux dont vous ne voulez pas. |
| `featured` | Plats mis en avant : `section` (nom d'une catégorie de la carte, ou `null` pour les premiers plats) et `count`. |
| `links` | Instagram, Facebook, WhatsApp (adresse `https://…` ou numéro). S'ajoutent aux liens saisis dans la console. |
| `legal` | Mentions légales : raison sociale, immatriculation, adresse, directeur de la publication, hébergeur. |

Variables de build qui remplacent le fichier si elles sont définies : `NEXT_PUBLIC_API_URL`,
`NEXT_PUBLIC_STOREFRONT_KEY`, `NEXT_PUBLIC_TURNSTILE_SITE_KEY` (contrôle anti-robot
Cloudflare Turnstile, sinon celui demandé par l'API). `TEMPLATE_BASE_PATH` publie le site dans un sous-dossier
(par exemple `/commande`).

## À déclarer dans la console RestauSpace

Sur l'application « Site web » (Vente en ligne › Applications) :

1. **Origine autorisée** : l'adresse du site, par exemple `https://www.chezawa.bj` (et `http://localhost:3005` pour
   tester). Sans elle, l'API répond `401`.
2. **Pages de retour du paiement** :
   - `https://www.chezawa.bj/commande/` (paiement d'une commande) ;
   - `https://www.chezawa.bj/reservation/` (acompte d'une réservation).

## Déploiement

`npm run build` produit des fichiers statiques dans `out/`, à publier tels quels.

- **Cloudflare Pages** : commande `npm run build`, dossier `out`.
- **Netlify** : commande `npm run build`, dossier de publication `out`.
- **Vercel** : préréglage Next.js ; l'export statique est détecté.
- **Tout hébergement de fichiers** (OVH, o2switch, Nginx, Apache, S3…) : copiez le contenu de `out/` à la racine du site.

Les adresses se terminent par `/` (`/carte/`) ; les paramètres passent dans l'adresse (`/commande/?jeton=…`). Aucune
règle de réécriture n'est nécessaire.

## Pages

| Adresse | Contenu |
|---|---|
| `/` | Accueil : accroche, plats mis en avant, réservation, horaires, avis. |
| `/carte/` | Carte : établissement, mode, catégories, options des plats, panier. `?mode=Delivery`, `?produit=<id>`. |
| `/commander/` | Récapitulatif, devis, créneau, coordonnées, livraison, code promo, points, paiement. |
| `/commande/?jeton=…` | Suivi de la commande, paiement, annulation, avis. |
| `/reserver/` | Réservation de table. |
| `/reservation/?jeton=…` | Ma réservation : acompte, annulation. |
| `/compte/` | Connexion par code, points, historique. |
| `/mentions-legales/` | Mentions légales et confidentialité. |

## Bon à savoir

- Le contenu est chargé dans le navigateur : les moteurs de recherche voient la structure des pages, pas la carte.
- Le panier et les dernières commandes sont gardés dans le navigateur ; la connexion au compte client dure le temps
  de l'onglet.
- Le site ne voit jamais de données bancaires : le client paie sur la page du prestataire.
