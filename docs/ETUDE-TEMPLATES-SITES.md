# Étude : modèles de sites vendables pour les restaurants

Version du 25/10/2026. Elle remplace la version précédente, qui proposait d'héberger les sites. Les prix sont des hypothèses à tester. Aucun chiffre de marché n'est avancé.

## 1. Décisions

- **RestauSpace n'héberge pas les sites des restaurants.** Le restaurant (ou son prestataire) héberge son site ; RestauSpace fournit l'API.
- **L'option Site web (`storefront.web`) est supprimée.** Un site de commande passe par l'API : `storefront.api`, inclus dans Pro et Multi-sites, option « API et webhooks » sur Essentiel (39 € / 11 900 XOF, prix inchangé).
- **Les souscripteurs actuels de l'option Site web** passent sur l'API au prix qu'ils payaient, sans coupure : l'ancienne option donne désormais `storefront.api` et `integrations.webhooks`, puis elle est archivée (plus vendue).
- **Les pages de commande du site public** (`/commander`, `/menu`, `/reserver`…) restent, comme démonstration et comme base du premier modèle.
- **Site sur mesure** : réalisé par RestauSpace, sur devis.

## 2. Le modèle économique

| Offre | Contenu | Facturation (hypothèse) |
|---|---|---|
| API | Accès du site du restaurant (clé publique) et clés d'API serveur, webhooks | Inclus dans Pro et Multi-sites, option mensuelle sur Essentiel |
| Modèle de site | Code prêt à l'emploi, branché sur l'API | Achat unique, par exemple 25 000 XOF / 49 € ; premiers modèles gratuits possibles pour lancer l'offre |
| Mise en ligne | L'équipe déploie le modèle, configure la clé, la carte et les photos | Forfait unique, à fixer |
| Site sur mesure | Conception et développement à la demande | Devis |

L'accès à l'API protège le revenu : un modèle copié ou partagé ne fonctionne qu'avec une clé d'un restaurant qui a l'API active. Le contrôle est déjà fait à chaque appel (`EnsureStorefrontAccessCommand`).

## 3. Ce qu'est un modèle

- Un projet Next.js en **export statique**, sans serveur : il s'héberge gratuitement ou presque (Cloudflare Pages, Netlify, Vercel, ou n'importe quel hébergement de fichiers).
- Branché sur l'API storefront : carte, panier, devis, commande, paiement en ligne, réservation, suivi, comptes clients, avis. Les données viennent toutes de la console.
- **Configuration par un seul fichier** (`site.config.json`) : clé publique, adresse de l'API, couleurs, polices, textes, sections affichées et leur ordre. Le restaurateur ne touche pas au code.
- **Un moteur, plusieurs thèmes** : les modèles partagent les mêmes sections (accueil, carte, à propos, galerie, avis, horaires et adresses, réservation, commande, contact). Corriger un bug une fois le corrige partout.
- Bouton « Déployer » vers un hébergeur, avec la clé à saisir, pour les restaurateurs non techniques.

## 4. Livraison et licence

| Point | Proposition |
|---|---|
| Livraison | Archive téléchargeable depuis la console (Abonnement › Modèles), ou accès en lecture à un dépôt privé |
| Licence | Un restaurant (un tenant), usage pour ses propres établissements ; revente interdite |
| Mises à jour | Versions publiées (journal des changements) ; le restaurant les applique quand il veut |
| Support | Documentation d'installation ; le forfait Mise en ligne pour ceux qui ne veulent pas s'en occuper |

## 5. Points d'attention techniques

1. **Origines autorisées** : le restaurant déclare l'adresse de son site dans sa SalesApp (10 au plus, avec et sans `www`). La console et le guide d'installation doivent le dire clairement.
2. **Compatibilité de l'API** : les sites ne sont plus sous le contrôle de RestauSpace. L'API storefront `/v1` ne doit jamais casser un contrat existant ; un changement incompatible passe par `/v2`, avec une période de coexistence.
3. **Pages de retour du paiement** : chaque site déclare ses pages de retour dans sa SalesApp.
4. **Anti-robot** : le modèle intègre Turnstile, activable par réglage.
5. **Charge** : l'API reçoit les mêmes appels que si RestauSpace hébergeait les sites ; seul le rendu des pages passe chez le restaurant. Le cache des lectures publiques (carte, horaires) reste utile.
6. **Légal** : le modèle prévoit des pages Mentions légales et Confidentialité, remplies par le restaurant, qui reste l'éditeur de son site.

## 6. La boutique sur restauspace.com

| Page | Contenu |
|---|---|
| `/modeles` | Grille des modèles : aperçu, style, prix ou « Gratuit » ; filtres par style (maquis, fast-food, pizzeria, café, gastronomique, traiteur) |
| `/modeles/[slug]` | Démo en direct, sections incluses, ce qui se met à jour seul, prix, « Obtenir ce modèle » |
| Démos | Une démo par modèle, hébergée par RestauSpace, branchée sur un restaurant de démonstration clairement marqué |
| Site sur mesure | Encart sur `/modeles` et sujet « Site sur mesure » dans le formulaire de contact (`/contact?sujet=site`, déjà ajouté) |

Parcours : « Obtenir ce modèle » ouvre l'inscription (visiteur) ou la console (restaurateur connecté), qui vérifie que l'API est active, encaisse l'achat et donne le téléchargement.

## 7. Ce qu'il faut développer

| Lot | Contenu | Taille |
|---|---|---|
| Fait (25/10) | Suppression de `storefront.web` ; le canal Web exige `storefront.api` ; ancienne option archivée avec bascule ; textes du site, de la console et de la documentation ; sujet « Site sur mesure » | — |
| 1. Premier modèle | Extraire les pages de commande de `restaurantos-site` dans un projet autonome, export statique, `site.config.json`, thème | Moyenne |
| 2. Deux autres modèles | Thèmes et variantes de mise en page sur le même moteur | Moyenne |
| 3. Catalogue des modèles | Métadonnées dans Billing (produit à achat unique) ou en fichier ; téléchargement réservé aux acheteurs | Faible à moyenne |
| 4. Paiement unique | Billing ne gère que des options mensuelles : ajouter l'achat ponctuel, ou commencer par des modèles gratuits | Moyenne |
| 5. Boutique | Pages `/modeles`, fiche, démos, lien depuis Tarifs | Faible à moyenne |
| 6. Guide d'installation | Page développeurs « Installer un modèle » : clé, origines, pages de retour, déploiement | Faible |

## 8. Déploiement de la suppression de `storefront.web`

1. Déployer la release (API, console, site).
2. `python3 scripts/seed-plans.py --update --dry-run` : la ligne « à archiver » doit apparaître pour les deux options Site web (EUR et XOF).
3. `python3 scripts/seed-plans.py --update` : droits alignés, puis options archivées.
4. Vérifier dans le back-office : les restaurants qui avaient Site web gardent leur option, et leur site fonctionne.

## 9. Décisions encore ouvertes

1. Premiers modèles gratuits ou payants.
2. Prix du forfait Mise en ligne.
3. Styles des trois premiers modèles.
4. Livraison par archive ou par dépôt privé.
