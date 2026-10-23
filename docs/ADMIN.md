# Documentation de l'API et back-office

## Documentation interactive de l'API

En développement (ou avec `Api:ExposeOpenApi=true`), l'API publie :

| | |
|---|---|
| Interface interactive (Scalar) | http://localhost:5080/docs |
| Document OpenAPI 3 | http://localhost:5080/openapi/v1.json |

Les deux modes d'authentification sont déclarés : `Bearer` (access token de `/v1/auth/login`) et `ApiKey`
(en-tête `X-Api-Key`). Dans Scalar : « Authentication », coller l'access token, puis « Send » sur n'importe quelle route.

Toujours publié, même en production : http://localhost:5080/openapi/storefront.json, limité aux routes
`/v1/storefront` (vente en ligne). Le document `v1` n'inclut pas ces routes. La documentation destinée aux développeurs
des restaurants est dans `docs/developpeurs/` et sur le site vitrine (`/developpeurs`).

## Back-office (`restaurantos-super-admin`)

Front Next.js relié à l'API. Toutes les données passent par les routes `/v1/admin/*`, protégées par la policy
d'administration : seul un compte dont l'email figure dans `Authorization:PlatformAdminEmails` y accède (en
développement : `admin@example.com`).

### Premier lancement

1. Démarrer l'API (`docker compose --profile api up -d --build` à la racine, ou `dotnet run`). En développement,
   `Cors:AllowedOrigins` autorise déjà `http://localhost:3000`.
2. Créer le compte administrateur, une seule fois :

   ```bash
   curl -H 'Content-Type: application/json' -d '{"email":"admin@example.com","password":"<mot de passe>"}' \
     localhost:5080/v1/auth/register
   # l'email de vérification est écrit dans les logs de l'API ("[DEV EMAIL] ...") : récupérer le token du lien
   docker compose logs api | grep "DEV EMAIL"
   curl -H 'Content-Type: application/json' -d '{"token":"<token>","password":"<mot de passe>"}' \
     localhost:5080/v1/auth/verify-email
   ```

3. Lancer le front :

   ```bash
   cd restaurantos-super-admin
   cp .env.example .env.local        # NEXT_PUBLIC_API_URL=http://localhost:5080
   pnpm install && pnpm dev
   ```

4. Ouvrir http://localhost:3000/admin/dashboard et se connecter avec `admin@example.com`.

### Restaurant de test

`scripts/seed-restaurant.py` crée par l'API un restaurant complet, prêt pour la console du restaurant
(`restaurantos-app`, port 3001) : « Chez Awa (test) » en XOF, un établissement ouvert à Cotonou, TVA 18 %, 11 produits
avec variantes et options, une carte publiée, trois postes de cuisine avec routage, deux salles et dix tables. Le
contenu vient de `scripts/restaurants/chez-awa.json`. Rejouable : seul ce qui manque est créé.

```bash
SAASRESTAURANT_ADMIN_PASSWORD='<mot de passe>' python3 scripts/seed-restaurant.py
# propriétaire différent du compte administrateur (compte existant et vérifié) :
python3 scripts/seed-restaurant.py --owner-email gerant@example.com
```

### Ce qui est relié à l'API

Tous les écrans du menu sont reliés à l'API.

| Écran | Lecture | Actions |
|---|---|---|
| Tableau de bord | `/admin/billing/overview`, `/admin/reporting/dashboard`, `/admin/reporting/activity`, `/admin/reporting/tenant-activity` | revenus récurrents (MRR, encaissé du mois et évolution, revenu moyen, encaissements sur 6 mois), santé des abonnements (conversion des essais, impayés, résiliations, offres), à traiter (impayés, fins d'essai, d'offre et résiliations sous 7 jours, paiements en ligne échoués), activité des restaurants (plus actifs, inactifs depuis 14 jours) ; période 7 j, 30 j, 3 mois, 12 mois ou personnalisée ; bloc Vente en ligne |
| Utilisateurs | `/admin/users` | inviter, modifier l'email, suspendre, réactiver, désactiver, fermer les sessions, effacer |
| Restaurants | `/admin/reporting/tenants`, `/admin/tenants/{id}`, `/admin/online-sales/tenants/{id}` | créer, renommer, activer, suspendre (motif), réactiver, fermer, démarrer un abonnement, options ; onglet Vente en ligne |
| Vente en ligne | `/admin/online-sales/overview`, `/admin/online-sales/tenants`, `/admin/billing/add-ons`, `/admin/reporting/activity` | restaurants avec une option (y compris sans application, onglet « Sans application »), options souscrites par restaurant, commandes en ligne par canal, CA en ligne et part du CA, taux de refus, bornes, clients connectés, établissements en pause ; détail par restaurant (applications, bornes, réglages par établissement) |
| Demandes de démo | `/admin/demo-requests` | demandes du formulaire du site vitrine : coordonnées (email, appel, WhatsApp), suivi (nouvelle, contactée, convertie, sans suite), note interne, suppression définitive |
| Abonnements | `/admin/billing/subscriptions`, `/admin/billing/payments` | démarrer, offrir le plan (jusqu'à une date ou sans limite), mettre fin à l'offre, enregistrer un paiement, marquer en retard, suspendre (motif), résilier ; historique des paiements |
| Facturation | `/admin/billing/payments` | historique des paiements d'abonnement, export CSV, reçu PDF (non comptable, ce n'est pas une facture) |
| Plans | `/admin/billing/plans` | créer, changer le prix, ajouter ou modifier un droit, archiver |
| Statistiques | `/admin/reporting/dashboard` (MRR), `/admin/reporting/activity` et données chargées | MRR, ARR, ARPU, churn, nouveaux restaurants, encaissements, CA, panier moyen, taux d'annulation, graphiques |
| Notifications | calculées depuis abonnements, restaurants et comptes ; `/admin/notification-dead-letters` | alertes (retards, suspensions, fins d'essai, résiliations, restaurants sans abonnement, emails non vérifiés) ; renvoyer ou abandonner un envoi en échec |
| Paramètres | `/admin/billing/online-payment`, `/admin/payment-providers`, `/admin/notification-providers` | prestataire qui encaisse les abonnements pour chaque devise (état « Prêt » ou « Incomplet ») ; ajouter, modifier, activer, désactiver un prestataire ; identifiants du compte plateforme (envoyés au coffre, jamais réaffichés) |
| Journaux d'audit | `/admin/audit` (300 entrées les plus récentes) | consultation, détail et métadonnées |

Plans : un restaurant ne voit et ne choisit que les plans de sa devise. Les droits `orders.monthly`, `locations.count`
et `staff.count` sont appliqués par Orders, Locations et Staff (voir `src/Modules/Billing/README.md`, section 11).

Support : retiré du menu, aucun module backend n'existe encore.

Options (vente en ligne, bornes) : fiche du restaurant, onglet **Abonnement** (ajouter, quantité, retirer) ; le
restaurant peut aussi les souscrire lui-même depuis sa console. Le paiement enregistré compte le plan et les options
actives, ligne par ligne ; la **prochaine échéance** est affichée sur la fiche et sur l'abonnement. Détails :
`docs/VENTE-EN-LIGNE.md`, section « Options de vente en ligne ».

### Vente en ligne

Routes d'administration du module Storefront (politique `PlatformAdmin`), en lecture seule :

| Route | Contenu |
|---|---|
| `GET /v1/admin/online-sales/overview?from&to` | restaurants équipés et actifs, applications par canal (actives, désactivées), bornes (actives, révoquées), établissements réglés et en pause, commandes par canal (passées, prises en charge, en attente, refusées faute d'acceptation, annulées par le client, payées en ligne), clients connectés et connexions |
| `GET /v1/admin/online-sales/tenants?from&to&page&pageSize` | une ligne par restaurant ayant une application de vente, du plus actif au moins actif |
| `GET /v1/admin/online-sales/tenants/{tenantId}?from&to` | applications (nom, canal, état), bornes (nom, établissement, appairage, révocation), réglages par établissement (pause, modes, capacité par créneau), commandes par canal |

Période de 366 jours au plus (`storefront.platform.period_invalid`). Aucune clé publique, aucun jeton (de borne ou de
client), aucune empreinte ni aucune donnée de client n'en sort : des comptages et des noms d'applications et de bornes.

Le chiffre d'affaires en ligne vient de Reporting : deux mesures nouvelles, `sales.online_orders_completed` et
`sales.online_revenue` (dimension : `web`, `mobile_app`, `kiosk`), enregistrées quand une commande du site, de
l'application ou d'une borne est terminée. Elles ne comptent que les commandes terminées après le déploiement (pas de
reprise de l'historique). Aucun changement de schéma : ni migration Storefront ni migration Reporting.

Facturation : nouvelle table `billing.subscription_payments` (chaque paiement enregistré, avec le prix du plan au
moment du paiement). Générer la migration avant de lancer l'API :

```bash
dotnet ef migrations add SubscriptionPayments --project src/Modules/Billing/Billing.Infrastructure --output-dir Persistence/Migrations
```

Les paiements enregistrés avant cette version n'apparaissent pas dans l'historique.

### Créer le catalogue des plans

`scripts/plans/catalogue.json` décrit 14 plans : Découverte, Essentiel, Pro et Groupe, en XOF et en EUR, mensuels, et
annuels pour les trois payants (2 mois offerts), et 8 options mensuelles (Site web, Application mobile, Borne, API, en XOF
et en EUR). `scripts/seed-plans.py` les crée par l'API d'administration (règles du
domaine, outbox et journal d'audit conservés), sans SQL direct. Python 3 suffit, sans dépendance.

```bash
python3 scripts/seed-plans.py --dry-run     # ce qui serait créé
python3 scripts/seed-plans.py               # crée ce qui manque (rejouable)
python3 scripts/seed-plans.py --update      # aligne aussi prix et droits des plans existants sur le fichier
```

Options : `--api` (défaut `http://localhost:5080`, ou `SAASRESTAURANT_API`), `--email` (défaut `admin@example.com`),
`--currency XOF`. Mot de passe : `SAASRESTAURANT_ADMIN_PASSWORD` ou saisie masquée. Un plan actif de même nom, devise et
intervalle n'est jamais recréé ; un changement de prix ne s'applique qu'aux périodes suivantes.

### Sécurité

- Jetons conservés en `sessionStorage` (perdus à la fermeture de l'onglet), jamais dans un cookie ni dans les logs.
- Access token renouvelé automatiquement 30 s avant expiration ou sur réponse 401 ; refus du renouvellement : retour
  à l'écran de connexion.
- Déconnexion : `POST /v1/auth/logout` révoque la session côté serveur.
- Un compte connecté mais absent de `PlatformAdminEmails` est refusé dès la connexion (appel de contrôle à une route
  `/admin/*` ; sur 403, déconnexion et message clair).
- Déconnexion après 30 minutes sans activité (avertissement 5 minutes avant), réglable par `NEXT_PUBLIC_IDLE_MINUTES`.
- Actions définitives (effacer ou désactiver un compte, fermer un restaurant, résilier un abonnement, archiver un plan) :
  le nom ou l'email doit être saisi pour confirmer.
- `Enregistrer un paiement` envoie un `Idempotency-Key` : un double clic n'enregistre pas deux paiements.
- Production : renseigner `Cors:AllowedOrigins` avec l'URL exacte du back-office (aucune origine par défaut) et
  `NEXT_PUBLIC_API_URL` avec l'URL publique de l'API.

### Vérifications faites

Typecheck et `next build` sans erreur. Parcours testé dans Chromium (390 et 1440 px) contre une API simulée de même
contrat : connexion, tableau de bord, Vente en ligne (indicateurs, tableau, tiroir), onglet Vente en ligne d'un
restaurant, démarrage d'un abonnement limité à la devise, confirmation par saisie, prix en XOF sans décimale, période
personnalisée, rechargement ciblé après une action, refus d'un compte non administrateur, déconnexion après inactivité ;
aucune erreur JavaScript. Backend : tests Storefront (63) et Reporting (141) passants ; l'API .NET n'a pas pu être
lancée dans cet environnement (Infrastructure non compilable sans les paquets NuGet).
