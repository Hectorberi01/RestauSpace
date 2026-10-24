# Audit de sécurité RestauSpace

24 octobre 2026. Lecture seule : aucun fichier de code n'a été modifié.

## Périmètre

- Backend .NET :
  - authentification, isolation des tenants, autorisations ;
  - paiements et webhooks, vente en ligne, fichiers, notifications, exports.
- Les trois fronts : console, site public, back-office.
- Infrastructure : Docker, Traefik, PostgreSQL, sauvegardes, Jenkins, scripts.

## Méthode

- Lecture du code et de la configuration.
- `pnpm audit` sur les trois fronts.
- Chaque constat haut ou critique a été revérifié dans le code appelant et appelé.

## Limites

- **Aucun contrôle de la prod en ligne.** Le contrôle passif (en-têtes, TLS, routes exposées) n'a pas pu se faire : le réseau de cette session et celui du Mac bloquent `*.restauspace.com`. Les commandes à lancer soi-même sont en fin de rapport (section « À vérifier en prod »).
- **Aucun test d'intrusion actif.**
- **Copies locales des fichiers `.env`.** Les fichiers `deploy/production/.env` et `api.env` lus ici sont les copies du Mac. Elles peuvent différer de celles du VPS.

## Synthèse

| Gravité | Nombre | À traiter |
|---|---|---|
| Critique | 1 | Immédiatement, avant toute ouverture au public |
| Haute | 6 | Avant l'ouverture |
| Moyenne | 19 | Dans les semaines qui suivent |
| Basse | 18 | Au fil de l'eau |

Les quatre premiers points à corriger sont C1, H1, H2 et H4. Les deux premiers sont exploitables sans compte (C1) ou avec un simple compte d'employé (H1).

---

## Critique

### C1. Next.js 16.4.0 vulnérable à une exécution de code à distance (les 3 fronts)

- **Preuve :** les trois `package.json` déclarent `"next": "16.4.0"`. `pnpm audit` remonte :
  - `GHSA-2464-fhr4-w9xw` (critique) : exécution de code sans authentification via l'API d'optimisation d'images (`/_next/image`) avec des SVG ;
  - `GHSA-673q-mjm6-r5xr` (critique) : RCE via `use cache`, que le projet n'utilise pas ;
  - `GHSA-p3m5-59mp-jv5r` (haute) : déni de service via les Server Actions.

  Versions touchées : 16.0.0 à 16.4.0 ; version corrigée : 16.4.1.
- **Exposition :**
  - `/_next/image` est actif dans les trois apps : aucun `images.unoptimized`, et le site utilise `next/image`.
  - Le conteneur Next voit le réseau Docker et `API_INTERNAL_URL`.
  - Conditions exactes d'exploitation : à confirmer, le texte des avis n'est pas lisible depuis cette session.
- **Recommandation :**
  - Passer `next` (et `eslint-config-next`) en 16.4.1 ou plus, idéalement la dernière 16.4.x, dans les trois apps.
  - Régénérer `pnpm-lock.yaml`, puis relancer le pipeline.

---

## Haute

### H1. Le `locationId` de la requête choisit le rôle évalué (élévation et contournement)

- **Origine :** ce défaut vient de la correction H1 de l'audit de cohérence (rôle d'établissement), que j'ai écrite.
- **Preuve :**
  - `src/BuildingBlocks/SaasRestaurant.BuildingBlocks.Api/Permissions.cs:123-144` : dès qu'un `locationId` est présent dans la route **ou dans la query string**, c'est le rôle de cet établissement qui est évalué.
  - Cette valeur n'est jamais rapprochée de l'entité réellement visée.
  - `StaffMember.EffectiveRoleAt` (`Staff.Domain/StaffMembers/StaffMember.cs:260-263`) : le rôle d'établissement l'emporte sur le rôle de tenant.
- **Scénarios :**
  - **Élévation.** Un membre est « Serveur » au niveau du restaurant mais « Manager » dans l'établissement A.
    - `POST /v1/orders/{id}/discounts?locationId=A` sur une commande de l'établissement B : la remise passe.
    - Le même ajout de `?locationId=A` marche sur les routes de niveau restaurant : catalogue, paiements, intégrations, personnel.
  - **Contournement.** Un membre volontairement restreint dans l'établissement B omet `locationId` (ou en met un faux) : c'est son rôle de restaurant, plus large, qui s'applique.
- **Recommandation :**
  - Ne jamais décider d'une autorisation sur un paramètre de la query string.
  - Déduire l'établissement de l'entité chargée côté serveur (commande, table, caisse), puis vérifier la permission pour cet établissement.
  - Sur les routes de niveau restaurant, exiger le rôle de restaurant.

### H2. Code 2FA attaquable par force brute : un mot de passe juste remet le compteur à zéro

- **Preuve :**
  - `Identity.Application/PasswordVerifier.cs:34` : `attempts.ResetAsync(...)` est appelé à chaque mot de passe juste.
  - `Identity.Application/Commands/TwoFactor.cs` : les échecs du code 2FA alimentent le **même** compteur, limité à 10 par 15 min.
  - Le challenge 2FA reste réutilisable pendant 5 min.
- **Scénario :** avec le mot de passe (fuite, hameçonnage), un attaquant boucle sur « 1 connexion + 9 codes ». Le blocage n'arrive jamais. Seule la limite par IP (60 par min) reste, soit environ 1 h avec 100 IP pour trouver un code à 6 chiffres.
- **Recommandation :**
  - Un compteur 2FA séparé, qu'un mot de passe juste ne vide pas.
  - Un challenge à usage unique, ou qui compte ses propres échecs.
  - Une alerte par e-mail après plusieurs échecs.

### H3. Un membre avec `staff.manage` ou `roles.manage` peut se donner le rôle Propriétaire

- **Preuve :**
  - `Staff.Application/Commands/StaffMembers.cs:50-58` (`AssignTenantWideRoleHandler`) : n'importe quel rôle est affecté, Owner compris, sans vérifier les droits de l'appelant. Même chose pour les invitations (`Invitations.cs`) et l'affectation à un établissement.
  - `Staff.Application/Commands/Roles.cs:50` : `role.Grant(...)` accepte toute permission, même une que l'auteur n'a pas.
- **Scénario :**
  - Un rôle personnalisé « RH » avec `staff.manage` s'attribue Owner, puis peut retirer le propriétaire d'origine.
  - Avec `roles.manage` seul : il ajoute `staff.manage` à son propre rôle, puis fait de même.
- **Recommandation :**
  - Refuser d'attribuer un rôle, ou d'accorder une permission, que l'appelant ne détient pas lui-même.
  - Réserver le rôle Owner aux Owners.
  - Interdire de modifier son propre rôle.

### H4. Prise de contrôle d'un compte client via une commande invité non vérifiée (site)

- **Preuve :**
  - `src/Host/SaasRestaurant.Host/StorefrontBridges.cs:440-447` : une commande invité crée une fiche client avec l'e-mail **et** le téléphone saisis, sans vérifier ni l'un ni l'autre.
  - `Storefront.Application/Customers/CustomerAccounts.cs:287` : à la connexion par code, la fiche est retrouvée par e-mail **ou** par téléphone.
- **Scénario :**
  1. L'attaquant commande avec son propre e-mail et le numéro d'un futur client.
  2. Le client se connecte plus tard par SMS : sa session s'ouvre sur cette fiche.
  3. L'attaquant se connecte par son e-mail sur la même fiche.
  4. Il obtient alors les commandes du client (avec leurs jetons de suivi), peut les annuler, et peut utiliser ses points de fidélité.
- **Recommandation :**
  - Garder les contacts non prouvés à part.
  - Ne rattacher une session qu'à une fiche dont le contact prouvé est marqué vérifié.

### H5. Réseau Docker `proxy` partagé : accès direct à l'API et IP client falsifiable

- **Preuve :**
  - `deploy/production/docker-compose.yml` : api, console, site, admin et migrations ne sont reliés qu'au réseau externe `proxy`, partagé avec l'autre application (marketplace).
  - L'API écoute en HTTP sur 8080.
  - `ReverseProxy__Enabled=true`, mais sans `ReverseProxy__KnownNetworks` : `HostComposition.Platform.cs:84-88` fait alors confiance à `X-Forwarded-For` venant de tout `10/8`, `172.16/12` et `192.168/16`.
- **Scénario :** un conteneur compromis de l'autre projet appelle `http://<ip api>:8080/v1/auth/login` en changeant `X-Forwarded-For` à chaque requête. Les limites par IP sautent (`/auth`, menu public, storefront), ce qui permet le bourrage d'identifiants et fausse les IP du journal d'audit. Il atteint aussi admin, console et site sans passer par Traefik.
- **Recommandation :**
  - Un réseau dédié RestauSpace sur lequel Traefik est aussi branché, plus un réseau interne pour l'appel site vers API.
  - `ReverseProxy__KnownNetworks` limité à l'adresse de Traefik.

### H6. PostgreSQL : l'application utilise le superutilisateur, et `pg_hba` est trop large

- **Preuve :**
  - L'API et les migrations utilisent `POSTGRES_USER` (`deploy/production/docker-compose.yml:32`).
  - Sur le serveur de base, c'est le compte créé par l'image `postgres`, donc un superutilisateur, sauf s'il a été créé à part. Dans notre cas, `es_restaurant_user` a été créé à la main : vérifie qu'il n'est pas `SUPERUSER`.
  - `deploy/database/pg_hba.conf:7` : `hostssl all all 10.10.0.0/24` ouvre toutes les bases et tous les comptes à tout le réseau privé.
- **Scénario :** une injection SQL ou une fuite de la chaîne de connexion donnerait `COPY ... TO PROGRAM` (exécution de code sur le serveur de base) et un accès à tous les restaurants. Toute autre machine du réseau privé, comme le serveur d'observabilité, peut tenter une connexion.
- **Recommandation :**
  - Un rôle `NOSUPERUSER` propriétaire du schéma pour les migrations, et un rôle limité à la lecture et l'écriture des données pour l'API.
  - `pg_hba` restreint à `hostssl es_restaurant <rôle> 10.10.0.1/32` (l'adresse du serveur applicatif).

---

## Moyenne

| # | Constat | Preuve | Recommandation |
|---|---|---|---|
| M1 | La rotation d'une clé d'API ne vérifie pas que l'auteur détient ses scopes. Un membre avec `integrations.manage` récupère ainsi une clé qui rembourse. | `Integrations.Application/Commands/CredentialCommands.cs:99-103` | Appliquer le même `RequireHeldAsync` qu'à l'émission. |
| M2 | La détection de réutilisation des refresh tokens ne couvre qu'une génération. Un jeton volé qui a tourné deux fois reste valable 30 jours, sans alerte. | `Identity.Domain/Users/User.cs:449-452`, `RefreshSession.cs:41-45` | Garder les empreintes par famille ; révoquer toute la famille quand un ancien jeton revient. |
| M3 | Le PIN de caisse (4 à 6 chiffres) ouvre une session complète de 30 jours, avec tous les droits du rôle (Owner compris), sans mot de passe ni 2FA. | `Staff.Application/Commands/Tills.cs:176`, `StartOperatorSession.cs:341-351` | Jeton de caisse court, sans refresh, limité à l'exploitation et à l'établissement de la caisse. |
| M4 | SSRF possible via l'hôte et le port SMTP saisis par un restaurant (`10.10.0.2`, `127.0.0.1`…) : scan de ports interne. À confirmer : seulement si le SMTP est ouvert aux restaurants. | `Notifications.Infrastructure/Senders/SmtpSender.cs:150,168` | Refuser les adresses non publiques après résolution DNS ; limiter les ports aux ports SMTP standards. |
| M5 | Les webhooks de notification ne sont liés ni à la configuration ni au tenant. Un restaurant qui a son propre secret peut marquer en échec les envois d'un autre restaurant (avec relances facturées). | `NotificationWebhooks.cs:165-168,226`, `NotificationsPersistence.cs:265-268` | Stocker configuration et tenant avec l'événement ; rapprocher les messages uniquement dans ce périmètre. |
| M6 | Pompage de SMS et WhatsApp depuis les routes publiques (code de connexion, réservation, liste d'attente, commande) vers des numéros arbitraires, y compris surtaxés. L'anti-robot est désactivé par défaut. | `StorefrontEndpoints.cs:23` (`RequireHumanCheck = false`), `NotificationRouting.cs:32` | Liste blanche d'indicatifs pays, plafond par restaurant et par heure, Turnstile activé par défaut. |
| M7 | Une commande invité peut accorder le consentement marketing sur la fiche d'un client existant (risque RGPD). | `StorefrontBridges.cs:446-455` | Consentement seulement sur une fiche créée par la commande, ou après preuve du contact. |
| M8 | Le passage du mode « compte plateforme » au mode « compte du restaurant » est possible avec des paiements en cours. Le restaurant peut alors forger des webhooks « payé » avec son propre secret. | `RestaurantPaymentConfiguration.cs:125-143`, `PaymentsWorkspace.cs:119-121` | Refuser le changement de mode si des paiements sont en cours ; garder le mode sur chaque paiement. |
| M9 | Montée de plan sans paiement pendant une période payée : on paie le moins cher, on passe au plus cher jusqu'à l'échéance, puis on redescend. | `SubscriptionCommands.cs` (ChangePlan), `SubscriptionDues.cs:24` | Prorata encaissé tout de suite, ou nouveau plan appliqué seulement au renouvellement. |
| M10 | Injection HTML dans les e-mails : nom ou notes du client insérés sans échappement, ce qui permet un lien d'hameçonnage envoyé depuis le domaine du restaurant. | `TemplateRenderer.cs:21`, `SmtpSender.cs:54-56`, `ResendSender.cs:46` | Échapper chaque valeur quand le modèle est en HTML. |
| M11 | Abus anonymes de la vente en ligne : créneaux bloqués par des commandes non payées, coupons épuisés, énumération des codes promo. | `StorefrontPersistence.cs:74-77`, `StorefrontBridges.cs:277-285` | Anti-robot par défaut, limite par IP et par contact, libération rapide des créneaux et coupons non payés, réponse uniforme sur les coupons. |
| M12 | CSP du site public avec `script-src 'unsafe-inline'`, alors que le site garde des jetons (client, borne, suivi). | `restaurantos-site/next.config.ts:13` | Nonce avec `'strict-dynamic'` (comme la console), ou CSP par empreintes. |
| M13 | L'alias `restauspace-api` peut être pris par un autre projet sur le réseau partagé, qui servirait alors le contenu des pages du site (le rendu côté serveur appelle l'API par cet alias). Ce défaut vient de la correction M17, que j'ai écrite. | `deploy/production/docker-compose.yml` (alias, `API_INTERNAL_URL`) | Réseau interne propre au projet pour l'appel site vers API (voir H5). |
| M14 | Back-office ouvert à tout Internet (`ADMIN_ALLOWED_IPS=0.0.0.0/0`), et la liste blanche ne protège pas les routes `/v1/admin` de l'API. La 2FA n'est pas exigée pour les administrateurs. | `docker-compose.yml:105`, `PlatformAdmins.cs:15-18` | Restreindre les IP, appliquer la même liste à `api…/v1/admin`, rendre la 2FA obligatoire pour les administrateurs. |
| M15 | TLS PostgreSQL en `Require` : le certificat n'est pas vérifié (interception possible sur le réseau privé). Le certificat n'a pas de SAN, donc `verify-full` échouerait. | `docker-compose.yml:32`, `deploy/database/make-cert.sh:7-8` | Petite autorité de certification + certificat avec `IP:10.10.0.2`, puis `VerifyFull`. |
| M16 | Sauvegardes en clair, lisibles par tous (0644), sur le même serveur que la base. `deploy/database/backups/` n'est pas ignoré par git. | `deploy/database/backup.sh:7`, `.gitignore` | `umask 077`, chiffrement (`age` ou `gpg`), copie hors du serveur, ligne dans `.gitignore`. |
| M17 | La rétention supprime les anciennes sauvegardes même quand la nouvelle échoue, et rien ne signale l'échec. Les rôles ne sont pas sauvegardés. | `backup.sh:12,15` | Purger seulement après un dump réussi, alerter, ajouter `pg_dumpall --globals-only`. |
| M18 | Jenkins multibranche : n'importe quelle branche peut lire les identifiants de production (clé SSH du VPS, GHCR en écriture). À confirmer selon qui peut pousser et la gestion des forks. | `Jenkinsfile:95,120,129` | Identifiants limités à un job qui ne construit que `main`, forks non approuvés, clé SSH restreinte à `deploy.sh`. |
| M19 | Traefik partagé : l'autre projet peut déclarer des routeurs sur les domaines RestauSpace et capter le trafic. | Configuration du provider Docker | `exposedByDefault=false` et contraintes de labels par projet. |

---

## Basse

- **Énumération de comptes à l'inscription.** `/auth/register` et `/public/sign-up` renvoient un 409 « email déjà utilisé » (`User.cs:77`, `SignUp.cs:106`).
- **Bombardement d'e-mails.** Pas de limite par adresse sur l'oubli de mot de passe, le renvoi de vérification ou l'inscription. La file d'e-mails (1000, `DropOldest`) jette les plus anciens (`EmailQueue.cs:15`).
- **Lectures sans permission.** Certaines lectures sont ouvertes à tout membre, cuisine comprise : commandes, paiements, coûts. Un membre retiré garde l'accès jusqu'à 15 min (durée de l'access token).
- **Sessions sans durée maximale absolue.** Chaque renouvellement repart pour 30 jours (`User.cs:407`).
- **Abonnement réglé sans montant ni devise.** Un montant absent compte comme payé (`CheckoutCommands.cs:111`) et la devise n'est pas vérifiée (`BillingBridges.cs`).
- **Capture partielle KKiaPay.** Le montant du widget est modifiable par le payeur ; le paiement apparaît « encaissé » alors que la commande reste due (`KkiaPay.cs:70-79`).
- **Webhooks entrants non limités en débit.** Pour Mollie, CinetPay et MTN, chaque appel anonyme déclenche une relecture chez le prestataire (`HostComposition.Routes.cs:146`).
- **Appairage de borne détournable.** Code à 6 chiffres non unique et appairages en attente illimités (`Kiosks.cs:24`).
- **URL d'envoi de fichier réutilisable.** Elle reste valable 15 min, y compris après validation, et permet d'écrire un contenu non analysé (`UploadCommands.cs:55`).
- **Jetons invités sans expiration.** Ils sont placés dans l'URL et peuvent donc apparaître dans les journaux du proxy (`OrderTrackingTokens.cs:34-41`).
- **Corps bruts des webhooks de paiement conservés sans limite de durée.** Ils contiennent nom, e-mail et téléphone du payeur.
- **Neutralisation incomplète des formules dans les CSV.** Les tabulations et retours en début de cellule ne sont pas traités (`ExportModels.cs:91`).
- **Jetons de la console et de l'admin en sessionStorage**, donc lisibles par un script en cas de XSS. La CSP stricte limite ce risque.
- **Le service worker de la console met en cache les URL avec `?token=`** (réinitialisation, invitation) (`public/sw.js:42-52`).
- **Données de la console non effacées à la déconnexion.** Le cliché du restaurant reste en localStorage (`components/console.tsx:54-70`).
- **`checkoutUrl` suivie sans vérifier `https:`** (`ordering.tsx:755`, `abonnement/page.tsx:312`).
- **Dépendances de l'admin.** `dompurify` vulnérable via jsPDF (le code vulnérable n'est pas appelé), et `zod`/`fast-uri` via `@hookform/resolvers` (non utilisé).
- **Infrastructure :**
  - `StrictHostKeyChecking=accept-new` dans le Jenkinsfile ;
  - Node.js téléchargé sans vérification d'empreinte ;
  - conteneurs sans `cap_drop`, `no-new-privileges` ni limites de ressources ;
  - pas d'en-têtes de sécurité côté API ni côté Traefik ;
  - conteneur `backup` sans rotation des journaux ;
  - paramètres SQL écrits dans les journaux des requêtes lentes (`log_parameter_max_length`) ;
  - images en tags flottants ;
  - observabilité vide dans la copie locale du `.env`.

## Informations

- **Copies locales de production sur le Mac** (`deploy/production/.env`, `api.env`) :
  - elles contiennent de vraies valeurs ;
  - 8 des 9 clés de `api.env` y sont identiques, et `IMAGE_REGISTRY` est absent du `.env` ;
  - ces copies sont anciennes : le VPS a été corrigé depuis, et `deploy.sh` refuse les doublons ;
  - aucune n'est suivie par git ;
  - recommandation : les supprimer du Mac, ou les tenir à jour, pour qu'elles ne fuient pas et ne trompent pas.
- **`appsettings.Development.json`** contient des clés de développement versionnées. C'est acceptable seulement si elles ne sont jamais reprises ailleurs.
- **Ordre des middlewares.** `UseRateLimiter` est appelé après l'authentification. C'est voulu pour la limite par clé, mais une clé invalide coûte une requête en base avant toute limite.
- **Clés d'API sans expiration par défaut.**
- **Vérification des mots de passe compromis.** Si le service est injoignable, le mot de passe est accepté (fail-open).

---

## Contrôles vérifiés et corrects

- **Mots de passe et jetons :**
  - PBKDF2-SHA512 à 210 000 itérations, même coût quand le compte n'existe pas ;
  - réponses neutres sur la connexion et l'oubli de mot de passe ;
  - jetons de 256 bits stockés en SHA-256 ;
  - réinitialisation et changement de mot de passe révoquent les sessions.
- **2FA :** TOTP en temps constant sans réutilisation, secrets chiffrés en AES-256-GCM, codes de secours hachés et à usage unique.
- **JWT :** HS256 seul, clé d'au moins 32 octets, issuer, audience et durée vérifiés, durée de 15 min, session revérifiée à chaque requête.
- **Isolation des tenants :**
  - le tenant vient toujours du jeton ;
  - les filtres EF refusent par défaut quand aucun tenant n'est défini ;
  - chaque `IgnoreQueryFilters` examiné a un prédicat de tenant explicite ;
  - les établissements sont vérifiés par tenant.
- **Back-office et clés d'API :**
  - routes `/admin/*` sous `PlatformAdmin`, qui exige un e-mail vérifié ;
  - clés d'API restreintes aux routes autorisées, sans les scopes sensibles ;
  - révocation immédiate des clés, limite de 600 par minute.
- **Webhooks de paiement :**
  - signature en temps constant, tolérance de 5 min ;
  - relecture chez le prestataire ;
  - dédoublonnage par configuration ;
  - paiement cherché uniquement dans le tenant de la configuration ;
  - devise vérifiée et montant au plus égal à l'attendu ;
  - commande acceptée seulement si elle est entièrement payée.
- **Coffre des secrets :** AES-256-GCM, la référence sert de données authentifiées, et aucune valeur n'apparaît dans les erreurs, les logs ou les réponses.
- **Vente en ligne :**
  - prix recalculés côté serveur ;
  - remises bornées ;
  - codes à usage unique stockés en HMAC, 5 essais, 5 par heure, réponse neutre.
- **Fichiers :**
  - clé de stockage aléatoire, préfixée par le tenant ;
  - type et taille signés ;
  - détection du type par signature (ni SVG ni HTML) ;
  - téléchargement forcé en pièce jointe.
- **Webhooks sortants :** protection contre la SSRF (résolution DNS vérifiée, adresses publiques seulement), HTTPS seulement, sans redirection.
- **Fronts :**
  - CSP à nonce et `strict-dynamic` dans la console et l'admin ;
  - HSTS, `X-Frame-Options: DENY`, `nosniff` ;
  - aucun XSS trouvé (aucun HTML brut issu de données) ;
  - aucune redirection ouverte ;
  - aucun secret dans les `NEXT_PUBLIC_*` ;
  - conteneurs non root, `.env*` exclus des images.
- **Infrastructure :**
  - fichiers de secrets ignorés par git et en droits 600 ;
  - aucun port publié en production ;
  - PostgreSQL en SCRAM et `hostssl`, écoutant seulement sur l'adresse privée ;
  - migrations dans un job séparé ;
  - Swagger masqué en production ;
  - CORS sur liste explicite ;
  - secrets Jenkins jamais interpolés.

---

## À vérifier en prod

Commandes en lecture seule, à lancer depuis le Mac ou le VPS :

```bash
# En-têtes et redirections
for h in restauspace.com www.restauspace.com app.restauspace.com admin.restauspace.com api.restauspace.com; do
  echo "== $h"; curl -sSI "https://$h/" | grep -iE "^HTTP|strict-transport|content-security|x-frame|x-content-type|referrer|^server|x-powered|^location"
done
curl -sSI http://restauspace.com/ | head -3                 # doit rediriger vers https
# Routes qui ne doivent pas être publiques
for p in /openapi/v1.json /scalar /swagger /metrics /v1/admin/tenants /.env; do
  printf '%-22s %s\n' "$p" "$(curl -s -o /dev/null -w '%{http_code}' https://api.restauspace.com$p)"
done                                                         # attendu : 404 ou 401
# Version de Next exposée et optimiseur d'images
curl -s -o /dev/null -w '%{http_code}\n' "https://restauspace.com/_next/image?url=%2Ffavicon.svg&w=64&q=75"
# TLS
echo | openssl s_client -connect api.restauspace.com:443 -servername api.restauspace.com 2>/dev/null | openssl x509 -noout -dates -issuer
```

Sur le VPS :

```bash
# Le compte de l'application ne doit pas être superutilisateur (à lancer sur le serveur de base)
psql -U postgres -c "select rolname, rolsuper, rolcreaterole from pg_roles where rolname='es_restaurant_user';"
# Clés distinctes dans api.env (affiche seulement le nombre de doublons)
cut -d= -f2- /opt/restauspace/deploy/production/api.env | grep -v '^$' | sort | uniq -d | wc -l
# Qui partage le réseau proxy
docker network inspect proxy --format '{{range .Containers}}{{.Name}} {{end}}'
```

## Ordre de correction proposé

1. **Immédiat :** C1 (mise à jour de Next.js).
2. **Avant l'ouverture au public :**
   - H1, H2, H3 et H4 ;
   - H5 et M13 (réseau dédié) ;
   - H6 (rôles PostgreSQL) ;
   - M14 (back-office : IP et 2FA).
3. **Ensuite :**
   - M6, M5, M8 et M9 (fraude et coût) ;
   - M16 et M17 (sauvegardes) ;
   - M1 à M3 ;
   - le reste des constats moyens.
4. **Enfin :** les constats bas, au fil des évolutions.
