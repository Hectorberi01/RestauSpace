# restaurantos-app

Console web du restaurant (gérant, salle, cuisine), branchée sur l'API SaasRestaurant. Application Next.js séparée
de la console d'administration (`restaurantos-super-admin`) et du site vitrine (`restaurantos-site`).

## Démarrer

```bash
cp .env.example .env.local   # NEXT_PUBLIC_API_URL=http://localhost:5080
pnpm install
pnpm dev                     # http://localhost:3001
```

L'origine `http://localhost:3001` doit figurer dans `Cors:AllowedOrigins` de l'API (déjà ajoutée dans
`appsettings.Development.json`). En production, ajoutez l'URL publique de la console.

## Parcours

1. `/connexion` : `POST /v1/auth/login`.
2. `/restaurants` : `GET /v1/me/memberships`, acceptation d'une invitation, puis `POST /v1/me/tenant` qui renvoie un
   jeton portant le claim `tid`. Le tenant n'est JAMAIS envoyé dans les autres requêtes : il vient du jeton.
   Au rafraîchissement, `POST /v1/auth/refresh` reçoit le tenant sélectionné pour le conserver (revérifié par l'API).
3. Console : `GET /v1/tenant`, `GET /v1/staff/me/permissions`, `GET /v1/locations`. L'établissement choisi en haut de
   page est mémorisé par navigateur ; l'API vérifie qu'il appartient bien au restaurant.

## Écrans (premier lot)

| Écran | Contenu | Permissions |
|---|---|---|
| Tableau de bord | Commandes en cours, tables occupées, tickets cuisine ; chiffres du jour et 7 jours | `reporting.view` pour les chiffres |
| Commandes | Tableau par statut (nouvelles, acceptées, en préparation, prêtes), historique, création, détail, remise, annulation | `orders.take`, `orders.process`, `orders.discount` |
| Cuisine | Tickets ouverts par poste, plat par plat, priorité ; postes et poste par défaut | `kitchen.operate`, `kitchen.configure` |
| Salle | Plan de salle, ouverture et libération des tables, commandes d'une table, édition du plan | `floor.serve`, `floor.manage` |
| Menu | Produits, variantes, options, taxes, cartes par établissement et publication | `catalog.manage` |
| Établissements | Création, ouverture, fermeture temporaire | `locations.manage` |

Les boutons d'action ne s'affichent que si la permission est présente ; l'API reste seule juge (403 sinon).

## Temps réel

L'API n'expose ni SignalR ni SSE : les écrans interrogent l'API à intervalle régulier, seulement quand l'onglet est
visible (cuisine 5 s, commandes 10 s, salle et tableau de bord 15 s).

## Sécurité

- CSP à nonce posée par `proxy.ts` (`connect-src` limité à l'origine de l'API), en-têtes de sécurité dans
  `next.config.ts`, rendu dynamique (`await connection()`).
- Session (jetons) en `sessionStorage` : elle se ferme avec l'onglet. Rien de sensible n'est écrit dans la console
  du navigateur.
- Chaque `POST` porte un en-tête `Idempotency-Key`, conservé si la requête est rejouée après un rafraîchissement du jeton.

## Pas encore couvert

Réservations, paiements et encaissement, stocks, clients et fidélité, promotions, équipe et rôles, rapports
détaillés, horaires jour par jour, routage d'un produit vers un poste précis.
