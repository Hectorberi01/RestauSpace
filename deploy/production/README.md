# Mise en production (restauspace.com)

Trois serveurs. Ce dossier ne concerne que le serveur applicatif : l'API et les trois sites web, derrière le Traefik
déjà installé (entrypoint `websecure`, certresolver `letsencrypt`), sur un réseau propre à RestauSpace (« Réseaux »).

| Serveur | Dossier | Conteneurs |
|---|---|---|
| Applicatif | `deploy/production` | api, console, site, admin (4) ; `migrations` à chaque déploiement |
| Base de données | `deploy/database` (facultatif si PostgreSQL y est déjà installé) | postgres, backup |
| Observabilité | géré à part | reçoit l'OTLP de l'API |

```
navigateurs ──HTTPS──► Traefik ──► site · console · admin · api ──5432 (10.10.0.2)──► PostgreSQL (serveur de base)
                                                         │
                                                         └── OTLP/HTTP + jeton ──► serveur d'observabilité
```

## 1. DNS

| Nom | Service |
|---|---|
| `restauspace.com`, `www.restauspace.com` | site (www redirigé vers la racine) |
| `app.restauspace.com` | console des restaurateurs |
| `api.restauspace.com` | API |
| `admin.restauspace.com` | back-office |

Tous pointent vers le serveur applicatif. La base n'a pas de nom public.

## 2. Serveur de base de données

Si PostgreSQL n'y est pas encore installé :

```bash
cd deploy/database
cp .env.example .env && chmod 600 .env    # POSTGRES_*, APP_*, BIND_ADDRESS=10.10.0.2
./make-cert.sh                             # certificat TLS auto-signé
docker compose up -d                       # postgres + sauvegarde quotidienne dans backups/
```

Sinon, reprendre seulement : TLS (`ssl = on`), `pg_hba.conf` (la base de l'application, ses deux comptes, l'IP du
serveur applicatif), et les comptes ci-dessous.

### Comptes PostgreSQL

Trois comptes, jamais le superutilisateur pour l'application :

| Compte | Rôle | Dans `deploy/production/.env` |
|---|---|---|
| `postgres` | superutilisateur : local et sauvegarde seulement (`pg_hba.conf` refuse le reste) | non |
| `es_restaurant_user` | propriétaire du schéma : migrations | `POSTGRES_USER` |
| `es_restaurant_app` | API : lecture et écriture des données seulement | `DATABASE_APP_USER` |

Nouvelle installation : `roles.sh` les crée (variables `APP_*` du `.env`). Base déjà installée, en superutilisateur :

```bash
psql -U postgres -d es_restaurant -v owner=es_restaurant_user -v app=es_restaurant_app -v app_pw='<mdp>' -f app-roles.sql
```

Le script retire au propriétaire tout droit d'administration et crée le compte de l'API. S'il répond que le propriétaire
est le superutilisateur d'installation (base créée avec `POSTGRES_USER=es_restaurant_user`), API arrêtée :

```sql
CREATE ROLE es_restaurant_owner LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '<mdp>';
REASSIGN OWNED BY es_restaurant_user TO es_restaurant_owner;   -- dans la base es_restaurant
ALTER DATABASE es_restaurant OWNER TO es_restaurant_owner;
```

puis `POSTGRES_USER=es_restaurant_owner` côté serveur applicatif, le même nom dans `pg_hba.conf`, et relancer
`app-roles.sql` avec `owner=es_restaurant_owner`. Les droits du compte de l'API sont posés à chaque déploiement
(`db-grants.sql`, service `db-grants`).

Réseau privé entre les deux serveurs : la base écoute seulement sur `10.10.0.2` (`BIND_ADDRESS`), jamais sur l'IP
publique, et `pg_hba.conf` n'accepte que `10.10.0.0/24` en TLS, pour la base `es_restaurant` et ses deux comptes
(mieux : l'IP du serveur applicatif en `/32`). Docker publie ses ports avant UFW : ne pas mettre
`BIND_ADDRESS=0.0.0.0`.

Vérifier depuis le serveur applicatif :

```bash
docker run --rm postgres:16-alpine psql \
  "postgresql://<utilisateur>:<mdp>@10.10.0.2:5432/<base>?sslmode=require" -c "select 1"
```

`DATABASE_SSL_MODE=Require` reste conseillé, même sur le réseau privé. `Disable` seulement pour un PostgreSQL déjà
installé sans TLS (et `host` au lieu de `hostssl` dans son `pg_hba.conf`).

## 3. Serveur applicatif

### Réseaux (une seule fois)

Traefik est partagé avec un autre projet : les conteneurs RestauSpace ne vont jamais sur son réseau (`proxy`). Un réseau
propre, auquel on ajoute Traefik :

```bash
docker network create --subnet 172.30.250.0/28 restauspace-edge     # = EDGE_SUBNET du .env
docker network connect restauspace-edge traefik
```

Ajouter aussi `restauspace-edge` (externe) aux réseaux du service Traefik dans SON compose, sinon il le perd à sa
prochaine recréation. `deploy.sh` vérifie le sous-réseau et signale tout conteneur étranger sur ce réseau. L'API n'accepte
`X-Forwarded-For` que depuis `EDGE_SUBNET`. Le site appelle l'API par le réseau `internal`, propre au projet.

```bash
cd deploy/production
./generate-secrets.sh                       # clés à recopier (une seule fois)
cp .env.example .env && cp api.env.example api.env && chmod 600 .env api.env
#   .env    : base, OBSERVABILITY_OTLP_URL + OBSERVABILITY_TOKEN (FILES_ORIGINS, contacts : build.env de Jenkins)
#   api.env : clés générées, SMTP, R2, administrateurs
./deploy.sh
```

Une seule fois sur le VPS, pour tirer les images privées de GHCR (jeton GitHub `read:packages`) :
`docker login ghcr.io -u Hectorberi01`.

`deploy.sh [commit]` : met le dépôt sur ce commit, tire les 4 images du registre (`IMAGE_REGISTRY`), migrations,
démarrage, nettoyage des vieilles images. `IMAGE_REGISTRY` vide : les images sont construites sur le VPS
(`docker-compose.build.yml`). Retour arrière : `./deploy.sh <commit précédent>`, si les migrations sont compatibles.

Les variables `NEXT_PUBLIC_*` sont figées au build (dans `build.env` de Jenkins) : après un changement de `DOMAIN`, des
contacts ou de `STOREFRONT_KEY`, relancer le pipeline.

### Jenkins (sur le Mac)

`Jenkinsfile` à la racine. Toutes les branches : tests .NET, typecheck et build des 3 fronts. `main` : build des images
en `linux/amd64` et push sur `ghcr.io/hectorberi01/`, validation manuelle (24 h), puis `ssh … ./deploy.sh <commit>`.

À configurer :
- Docker Desktop lancé (images et Testcontainers) ; plugins Pipeline, Git, Credentials Binding.
- Job **Multibranch Pipeline** sur `https://github.com/Hectorberi01/RestauSpace.git`. GitHub ne peut pas joindre le
  Mac : déclencheur « Scan périodique » toutes les 5 minutes.
- Identifiants : `ghcr` (compte GitHub + jeton `write:packages`), `restauspace-build-env` (Secret file, copie remplie de
  `build.env.example`), `restauspace-vps-ssh` (clé SSH du compte de déploiement).
- Administrer Jenkins › Système › variables : `RESTAUSPACE_DEPLOY_HOST=deploy@<ip du VPS>` ;
  `RESTAUSPACE_DEPLOY_DIR` si le dépôt n'est pas dans `~/restauspace`.
- VPS : la clé publique dans `~deploy/.ssh/authorized_keys`, `deploy` membre du groupe `docker`, dépôt cloné.

### Observabilité

L'API envoie traces, métriques et logs en OTLP/HTTP à `OBSERVABILITY_OTLP_URL`, avec l'en-tête
`Authorization: Bearer OBSERVABILITY_TOKEN`. Le serveur d'observabilité doit accepter ce jeton. URL vide : aucun envoi.

### Photos (Cloudflare R2)

La console envoie et lit les photos directement sur R2. Deux réglages, sinon l'envoi est bloqué par le navigateur :
- `FILES_ORIGINS=https://<AccountId>.r2.cloudflarestorage.com` dans `build.env` (Jenkins), puis relancer le pipeline ;
- règle CORS du bucket (Cloudflare › R2 › bucket › Settings › CORS Policy) :
  ```json
  [{"AllowedOrigins": ["https://app.restauspace.com"], "AllowedMethods": ["PUT", "GET"],
    "AllowedHeaders": ["Content-Type"], "MaxAgeSeconds": 3600}]
  ```

### Premier démarrage

1. Créer le compte administrateur (email déclaré dans `Authorization__PlatformAdminEmails__0`), valider le lien reçu,
   se connecter sur `https://admin.restauspace.com` : la validation en deux étapes est demandée tout de suite
   (`Authorization__PlatformAdminRequireTwoFactor=true`), le back-office reste fermé sans elle :
   ```bash
   curl -X POST https://api.restauspace.com/v1/auth/register -H 'Content-Type: application/json' \
        -d '{"email":"<admin>","password":"<mot de passe solide>"}'
   ```
2. Back-office › Paramètres : prestataires de paiement, notifications, plans.
3. Console › Vente en ligne : créer l'application « Site web » avec l'origine `https://restauspace.com` et la page de
   retour `https://restauspace.com/commande` et `https://restauspace.com/reservation` (sinon le paiement en ligne et
   l'acompte sont refusés), recopier sa clé dans
   `STOREFRONT_KEY` de `build.env` (Jenkins), relancer le pipeline.

## 4. Sécurité

- Seul Traefik est exposé ; les conteneurs n'ouvrent aucun port.
- L'API lit l'adresse du client dans `X-Forwarded-For`, accepté seulement des réseaux privés (`ReverseProxy__Enabled`).
- Back-office et routes `/v1/admin` de l'API : restreindre `ADMIN_ALLOWED_IPS` dès que possible ; validation en deux
  étapes obligatoire pour les administrateurs.
- PostgreSQL : l'API n'a que la lecture et l'écriture des données (`DATABASE_APP_USER`) ; le superutilisateur n'est
  jamais joignable à distance.
- `.env` et `api.env` ne sont jamais versionnés (`.gitignore`).

## 5. Sauvegardes

Faites sur le serveur de base (`deploy/database`, conteneur `backup`) : un dump par jour dans `backups/`, 14 jours.
En garder une copie hors du serveur (par exemple `rclone copy backups r2:restauspace-backups`).

Restaurer (sur le serveur de base, API arrêtée côté applicatif) :

```bash
docker compose run --rm -T --entrypoint pg_restore backup --clean --if-exists -d <base> < backups/<fichier>.dump
```

Tester une restauration avant l'ouverture.

## 6. Avant d'ouvrir au public

- File d'e-mails Identity en mémoire : perdue à chaque redémarrage de l'API (voir `docs/AUDIT-CONTENEURS-PRODUCTION.md`).
- Une seule instance d'API.
- `Identity__Passwords__RejectCompromised=true` : l'API doit pouvoir sortir sur Internet.

## 7. Commandes utiles

```bash
docker compose ps
docker compose logs -f api
docker compose run --rm migrations    # migrations seules
```

Ancien projet `saasrestaurant` déjà déployé : `docker compose -p saasrestaurant down` avant le premier `deploy.sh`.
