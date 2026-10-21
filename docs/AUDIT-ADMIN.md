# Audit du tableau de bord d'administration (`restaurantos-super-admin`)

Date : 11 octobre 2026. Périmètre : le front Next.js (authentification, client API, store, écrans), sa configuration
et ses dépendances. Le backend n'est revu que là où le front en dépend. Méthode : relecture du code, `npm audit`,
parcours dans Chromium contre une API simulée.

## Synthèse

| # | Gravité | Domaine | Constat |
|---|---|---|---|
| 1 | ÉLEVÉE | Dépendances | Next.js 16.3.4 et sharp concernés par des avis de sécurité publiés (dont un critique) |
| 2 | ÉLEVÉE | Sécurité | Jetons d'accès ET de rafraîchissement lisibles par JavaScript (sessionStorage), sans CSP |
| 3 | ÉLEVÉE | Sécurité | Aucun en-tête de sécurité : la console peut être affichée dans une iframe (clickjacking) |
| 4 | ÉLEVÉE | Exactitude | Listes tronquées en silence (1 000 éléments, 300 entrées d'audit) : indicateurs et alertes faux à l'échelle |
| 5 | ÉLEVÉE | Sécurité | Actions irréversibles (effacer un compte, fermer un restaurant) derrière une simple confirmation |
| 6 | MOYENNE | Exactitude | Deux calculs différents du MRR (Tableau de bord et Statistiques) |
| 7 | MOYENNE | Sécurité | Pas de déconnexion après inactivité |
| 8 | MOYENNE | Sécurité | Un compte non administrateur entre dans la console (403 partout au lieu d'un refus clair) |
| 9 | MOYENNE | Fonctionnel | « Démarrer un abonnement » (écran Abonnements) propose les plans de toutes les devises |
| 10 | MOYENNE | Fonctionnel | Période personnalisée : dates de départ codées en dur (septembre-octobre 2026) |
| 11 | MOYENNE | Exactitude | Courbes du tableau de bord sans les périodes vides : la ligne relie des points éloignés |
| 12 | MOYENNE | Performance | Rechargement complet (6 listes, jusqu'à 10 pages chacune) après chaque action |
| 13 | MOYENNE | Exactitude | Le churn peut dépasser 100 % |
| 14 | FAIBLE | Maintenance | Code mort et dépendances inutiles, dont `app/chatgpt-auth.ts` qui lit des en-têtes `oai-*` |
| 15 | FAIBLE | Accessibilité | Textes gris de 9 à 11 px sous le contraste minimal, onglets sans rôles ARIA, graphiques sans alternative |
| 16 | FAIBLE | Fonctionnel | Saisie des prix au centime (pas de 0,01) même en XOF, devise sans décimale |
| 17 | FAIBLE | Données | Export CSV des comptes (emails) sans trace dans le journal d'audit |
| 18 | FAIBLE | Industrialisation | Aucun test automatisé du front, pas d'image de déploiement, deux fichiers de verrouillage |

Ce qui est solide : le `TenantId` n'est jamais envoyé par le front pour agir sur un restaurant autrement que par les
routes `/admin/*` protégées côté serveur ; aucun secret n'est affiché ni stocké (identifiants des prestataires envoyés
au coffre puis effacés du formulaire) ; renouvellement du jeton sans appel concurrent ; session limitée à l'onglet ;
`Idempotency-Key` sur l'enregistrement d'un paiement ; aucun `innerHTML` dans le code de l'application ; erreurs de
l'API affichées en français pour les codes connus ; chaque chargement est indépendant (un module en panne n'empêche
pas les autres de s'afficher).

## Corrections appliquées

| # | État | Ce qui a été fait |
|---|---|---|
| 1 | CORRIGÉ | `next` et `eslint-config-next` 16.4.0, `sharp` 0.35.5 ; dépendances indirectes corrigées par `overrides` (`browserslist`, `source-map-js`, `baseline-browser-mapping`, `fast-uri`, `@babel/core`). `pnpm audit --prod` : aucune vulnérabilité. Restent 4 avis sur l'outillage ESLint (développement seulement, non livré). |
| 2 | PARTIEL | CSP stricte avec nonce par requête (`proxy.ts`) : aucun script externe ni injecté, connexions limitées à l'API. Reste le cookie `HttpOnly` pour le refresh token (backend). |
| 3 | CORRIGÉ | `X-Frame-Options: DENY`, `frame-ancestors 'none'`, `X-Content-Type-Options`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, `Cross-Origin-Opener-Policy`, HSTS en production, en-tête `X-Powered-By` retiré. Vérifié : la console ne s'affiche plus dans une iframe. |
| 14 | CORRIGÉ | Supprimés : `app/chatgpt-auth.ts`, composants et données de démonstration, fichiers `hooks/services/components/types` des domaines, écran Support, 13 dépendances inutiles, `package-lock.json` (pnpm seul). |
| 4 | CORRIGÉ | Chaque liste signale sa troncature (`truncated`, d'après `hasMore` de la dernière page) : bandeau sur l'écran concerné, « + » sur le nombre de comptes, avertissement sur les Statistiques. Le MRR vient de l'agrégat serveur. Reste : pagination et recherche côté serveur des tableaux. |
| 5 | CORRIGÉ | Effacer ou désactiver un compte, fermer un restaurant, résilier un abonnement, archiver un plan : saisie du nom ou de l'email pour confirmer. La suspension en masse rappelle le nombre d'éléments. Reste (backend) : ré-authentification récente pour l'effacement. |
| 6 | CORRIGÉ | Statistiques et Tableau de bord lisent le même MRR (`/admin/reporting/dashboard`). |
| 7 | CORRIGÉ | Déconnexion après 30 minutes sans activité, avertissement à 25, réglable (`NEXT_PUBLIC_IDLE_MINUTES`). |
| 8 | CORRIGÉ | Après connexion, appel de contrôle à `/admin/reporting/tenants` ; sur 403, déconnexion et message « Ce compte n'est pas administrateur de la plateforme ». |
| 9 | CORRIGÉ | « Démarrer un abonnement » : choix du restaurant, chargement de sa devise, puis seulement les plans de base actifs de cette devise. |
| 10 | CORRIGÉ | Le sélecteur affiche les dates de l'écran ; en passant à « Personnalisé », il reprend la période affichée. |
| 11 | CORRIGÉ | Courbes du tableau de bord sur toutes les périodes de l'intervalle, à zéro quand il n'y a rien. |
| 12 | CORRIGÉ | Après une action, seuls les domaines touchés (et le journal d'audit) sont rechargés ; aucune liste pour les prestataires, demandes de démo et options. |
| 13 | CORRIGÉ | Churn : résiliations parmi les abonnements vivants au début de la période (jamais plus de 100 %). |
| 15 | PARTIEL | Textes secondaires en `#475569` et 12 px ; onglets du tiroir avec `role="tab"` et `aria-selected`, filtres avec `aria-pressed`. Reste : alternative textuelle des graphiques. |
| 16 | CORRIGÉ | Pas de saisie selon la devise (1 pour XOF, 0,01 pour EUR) et refus d'un prix avec trop de décimales avant l'envoi. |

Restent ouverts : 2 (refresh token en cookie `HttpOnly`, backend), 17 (journaliser l'export CSV des comptes,
backend) et 18 (tests Playwright du front dans la CI, image de déploiement).

Vérification (11 octobre) : typecheck, build de production, parcours de tous les écrans dans Chromium avec la CSP active (aucune
violation, aucune erreur, reçu PDF téléchargé), et serveur de développement fonctionnel avec la CSP.

## Détail

### 1. Dépendances vulnérables (ÉLEVÉE)

`npm audit --omit=dev` : `next` 16.0.0 à 16.3.7 (avis critique : exécution de code dans `next/og` ; aussi SSRF de
l'optimiseur d'images, empoisonnement du cache SSG/ISR, fuite d'informations du serveur de développement) et `sharp`
avant 0.35.5 (élevée, librsvg).

Exposition réelle limitée : l'application n'utilise ni `next/og`, ni `next/image`, ni ISR, ni `use cache`, et aucun
domaine d'image distant n'est autorisé. Le correctif reste à appliquer : ce sont des failles connues, détectées par
tout scanner.

Correctif : `next` et `eslint-config-next` en 16.4.0, `sharp` en 0.35.5 (via `overrides`), puis typecheck et build.

### 2. Jetons lisibles par JavaScript, sans CSP (ÉLEVÉE)

`src/services/api/client.ts` range la session (access token ET refresh token) dans `sessionStorage`. Une seule faille
XSS (dépendance compromise, extension, contenu mal échappé) suffit à voler une session de super administrateur et à la
prolonger avec le refresh token. Aucune Content-Security-Policy ne limite ce risque.

Correctif :
- court terme : CSP stricte (`default-src 'self'`, `connect-src` limité à l'URL de l'API, `frame-ancestors 'none'`,
  `object-src 'none'`, `base-uri 'self'`) ;
- moyen terme (backend) : refresh token dans un cookie `HttpOnly; Secure; SameSite=Strict` posé par l'API pour le
  back-office, access token en mémoire seulement.

### 3. Aucun en-tête de sécurité (ÉLEVÉE)

`next.config.ts` est vide : ni `X-Frame-Options`/`frame-ancestors`, ni `Referrer-Policy`, ni
`X-Content-Type-Options`, ni `Permissions-Policy`, ni HSTS. La console peut être chargée dans une iframe d'un autre
site et un administrateur piégé à cliquer sur « Effacer le compte ».

Correctif : `headers()` dans `next.config.ts` pour toutes les routes, avec la CSP du point 2.

### 4. Listes tronquées en silence (ÉLEVÉE)

`all()` arrête à 1 000 éléments (comptes, restaurants, abonnements, paiements) et l'audit à 300 entrées. Au-delà :
- « Comptes utilisateurs » du tableau de bord, MRR et churn des Statistiques, alertes des Notifications, plans
  « restaurants » et « MRR » de la page Plans sont FAUX sans aucun signal ;
- les tableaux affichent « N élément(s) chargé(s) » sans dire qu'il en manque.

Correctif : indiquer la troncature (`hasMore` de la dernière page) ; passer les indicateurs sur des agrégats serveur
(la reporting API fournit déjà restaurants et abonnements par statut et le MRR) ; pagination et recherche côté serveur
pour les tableaux (les routes acceptent déjà `page`, `pageSize`, `search`, `status`).

### 5. Actions irréversibles mal protégées (ÉLEVÉE)

Effacer un compte (RGPD), fermer un restaurant, résilier un abonnement, archiver un plan : un seul clic sur
« Confirmer ». La suspension en masse s'applique à toute la sélection.

Correctif : saisie du nom (ou de l'email) pour confirmer les actions définitives ; rappel du nombre d'éléments pour les
actions en masse ; à terme, ré-authentification récente exigée par l'API pour l'effacement.

### 6. Deux MRR différents (MOYENNE)

Le tableau de bord lit le MRR de la projection de la plateforme (montant recopié par Billing à chaque changement),
les Statistiques le recalculent avec le prix ACTUEL des plans. Après un changement de prix, les deux chiffres
divergent.

Correctif : une seule source, la reporting API, sur les deux écrans.

### 7. Pas de déconnexion après inactivité (MOYENNE)

Le jeton se renouvelle tant que l'onglet est ouvert, jusqu'à l'expiration du refresh token. Un poste laissé ouvert
reste administrateur.

Correctif : déconnexion après 30 minutes sans interaction (avertissement à 25), réglable.

### 8. Compte non administrateur dans la console (MOYENNE)

`AuthGate` ne vérifie que `/v1/me`. Un compte restaurant se connecte et voit la console vide avec « Accès refusé »
partout. Le serveur protège bien les données ; c'est l'expérience et l'information qui posent problème.

Correctif : après connexion, un appel léger à une route `/admin/*` ; sur 403, déconnexion et message clair.

### 9. Plans de toutes les devises proposés (MOYENNE)

Dans l'écran Abonnements, « Démarrer un abonnement » propose tous les plans actifs ; un plan d'une autre devise est
refusé par l'API (`billing.plan.currency_mismatch`). Le tiroir d'un restaurant, lui, filtre déjà par devise.

Correctif : après le choix du restaurant, charger sa devise et ne proposer que ses plans.

### 10. Période personnalisée (MOYENNE)

`DateRangePicker` démarre sur des dates fixes (7 septembre au 7 octobre 2026) alors que l'écran calcule autre chose :
les dates affichées ne sont pas celles utilisées tant qu'on ne les modifie pas.

Correctif : dates initiales passées par l'écran (30 derniers jours).

### 11. Courbes sans les périodes vides (MOYENNE)

Le tableau de bord ne trace que les jours présents dans la réponse : un mois avec deux ventes affiche une droite qui
relie les deux points. Les Statistiques complètent déjà les périodes vides par zéro.

Correctif : même découpage (`buckets`) sur le tableau de bord.

### 12. Rechargement complet après chaque action (MOYENNE)

Chaque action relance les six listes complètes. Avec 1 000 restaurants, une suspension déclenche plus de 50 requêtes.

Correctif : recharger seulement le domaine touché ; plus tard, cache par requête (TanStack Query).

### 13. Churn au-delà de 100 % (MOYENNE)

Numérateur : résiliations de la période ; dénominateur : abonnements vivants AU DÉBUT. Un abonnement démarré puis
résilié dans la période compte en haut mais pas en bas.

Correctif : ne compter au numérateur que les abonnements vivants au début, ou calculer sur la moyenne de la période.

### 14. Code mort et dépendances inutiles (FAIBLE)

- `app/chatgpt-auth.ts` : reste de l'environnement de génération ; il fait confiance à des en-têtes `oai-*` pour
  identifier l'utilisateur. Inutilisé, mais dangereux s'il était branché : à supprimer.
- Démonstration devenue inutile : `entity-page.tsx`, `entity-drawer.tsx`, `src/admin/support/`, `seed.ts`,
  `admin-service.ts` (sauf l'export CSV), les `hooks.ts`/`services.ts`/`components.ts`/`types.ts` de chaque domaine,
  `update`/`add`/`remove`/`reply`/`settings`/`simulate` du store.
- Dépendances sans usage : `drizzle-orm`, `drizzle-kit`, `wrangler`, `vinext`, `@cloudflare/*`, `@vitejs/*`, `vite`,
  `react-server-dom-webpack`, `json-rpc-2.0`, `raw-body`, `zod`. Chacune élargit la surface d'attaque (chaîne
  d'approvisionnement) et ralentit l'installation.
- `package-lock.json` et `pnpm-lock.yaml` coexistent : garder pnpm (indiqué par `packageManager`).

### 15. Accessibilité (FAIBLE)

- Textes secondaires en `#94A3B8` sur blanc à 9-11 px (`.stat-bottom`, `.table-note`, `.tiny-note`) : contraste
  d'environ 2,6:1, sous le minimum WCAG AA de 4,5:1.
- Onglets du tiroir et des pages : boutons sans `role="tab"` ni `aria-selected`.
- Graphiques sans alternative textuelle (valeurs lisibles seulement au survol).

### 16. Saisie des prix (FAIBLE)

Les champs numériques acceptent deux décimales. En XOF (aucune décimale), 9 900,50 est refusé par l'API après envoi.

Correctif : pas de saisie selon la devise (1 pour XOF, 0,01 pour EUR).

### 17. Export des données personnelles (FAIBLE)

L'export CSV des comptes contient les emails ; rien ne trace qui l'a fait. Recommandation : journaliser l'export via
une route d'audit, ou limiter l'export aux colonnes non personnelles.

### 18. Industrialisation (FAIBLE)

- Aucun test automatisé du front (les parcours ont été vérifiés à la main contre une API simulée) : ajouter des tests
  Playwright avec cette API simulée dans la CI.
- Pas d'image Docker ni de `output: 'standalone'` ; `NEXT_PUBLIC_API_URL` est figée au build (une image par
  environnement).

## Ordre de correction proposé

1. Points 1, 3, 2 (CSP), 14 (fichier `chatgpt-auth.ts` et dépendances) : quelques heures, front seulement.
2. Points 5, 7, 8, 9, 10, 11, 13, 16 : front seulement.
3. Points 4, 6, 12 : front, en s'appuyant davantage sur la reporting API et la pagination serveur.
4. Point 2 (cookie HttpOnly) et 17 : backend et front.
5. Points 15 et 18.

## Mise à jour du 12 octobre 2026

Corrections 4 à 13, 15 et 16 appliquées (tableau ci-dessus), avec l'écran Vente en ligne. Vérification : typecheck,
`next build`, parcours Chromium à 390 et 1440 px contre une API simulée (voir `docs/ADMIN.md`), aucune erreur.
