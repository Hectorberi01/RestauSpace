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

### TLS vérifié (VerifyFull)

`Require` chiffre mais ne vérifie pas le serveur : une machine qui prendrait l'IP `10.10.0.2` recevrait les mots de
passe. `VerifyFull` vérifie le certificat avec une autorité propre au projet.

Sur le serveur de base (PostgreSQL natif), avec le script recopié s'il n'y a pas le dépôt
(`scp deploy/database/tls-ca.sh <serveur de base>:`) :

```bash
chmod +x tls-ca.sh
sudo ./tls-ca.sh 10.10.0.2 18      # autorité (une fois) + certificat pour l'IP privée, ssl activé, rechargement
```

L'autorité est créée dans `/root/restauspace-db-ca/` : sauvegarder `ca.key` hors du serveur (coffre), puis l'effacer du
serveur ; il faut la remettre seulement pour `sudo ./tls-ca.sh --renew` (certificat serveur valable 3 ans).

Sur le serveur applicatif, recopier le certificat de l'autorité affiché à la fin (fichier public) :

```bash
mkdir -p deploy/production/tls && nano deploy/production/tls/db-ca.crt && chmod 644 deploy/production/tls/db-ca.crt
# .env : DATABASE_SSL_MODE=VerifyFull et DATABASE_PGSSLMODE=verify-full
docker run --rm -v "$PWD/deploy/production/tls:/tls:ro" postgres:16-alpine psql \
  "postgresql://<utilisateur>:<mdp>@10.10.0.2:5432/<base>?sslmode=verify-full&sslrootcert=/tls/db-ca.crt" -c "select 1"
```

`deploy.sh` refuse de démarrer en `VerifyFull` sans `tls/db-ca.crt` et prévient tant que le mode est `Require`.
`DATABASE_HOST` doit rester l'IP du certificat (`10.10.0.2`), pas un nom.

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

`Jenkinsfile` à la racine, utilisé par deux jobs. Le job Multibranch construit n'importe quelle branche, donc le code
de n'importe qui ayant le droit de pousser : il ne doit voir AUCUN identifiant. Seul le job de livraison, limité à
`main`, les voit.

| Job | Type | Fait | Identifiants |
|---|---|---|---|
| `ci/restauspace-ci` | Multibranch Pipeline | tests .NET, typecheck et build des 3 fronts, toutes les branches | aucun |
| `release/restauspace-release` | Pipeline | mêmes vérifications, images `linux/amd64` sur `ghcr.io/hectorberi01/`, validation (24 h), `ssh … ./deploy.sh <commit>` | ceux du dossier `release` |

À configurer :
- Docker Desktop lancé (images et Testcontainers) ; plugins Pipeline, Git, Credentials Binding, Folders.
- Deux dossiers : `ci` et `release`.
- `ci/restauspace-ci` : Multibranch sur `https://github.com/Hectorberi01/RestauSpace.git`, « Scan périodique » 5 min
  (GitHub ne peut pas joindre le Mac). Si les demandes de fusion sont découvertes : « Trust » = **Nobody** (le
  Jenkinsfile d'un fork n'est jamais exécuté).
- `release/restauspace-release` : Pipeline › « Pipeline script from SCM », même dépôt, branche `*/main` seulement,
  script `Jenkinsfile`, déclencheur « Scrutation de l'outil de gestion de version » `H/5 * * * *`. Le nom du job et le
  type Pipeline comptent : le Jenkinsfile ne pousse et ne déploie que dans ce job (pas de `BRANCH_NAME`, `main`).
- Identifiants **dans le dossier `release`** (dossier › Credentials), jamais au niveau global : `ghcr` (compte GitHub +
  jeton `write:packages`), `restauspace-build-env` (Secret file, copie remplie de `build.env.example`),
  `restauspace-vps-ssh` (clé SSH du compte de déploiement). Supprimer ceux qui existent au niveau global.
- GitHub : protéger `main` (pas de push direct, revue obligatoire), puisque tout ce qui arrive sur `main` est livré.
- Administrer Jenkins › Système › variables : `RESTAUSPACE_DEPLOY_HOST=deploy@79.137.35.129` ;
  `RESTAUSPACE_DEPLOY_DIR=/opt/restauspace/deploy/production` ; `RESTAUSPACE_DEPLOY_KNOWN_HOSTS` = clé du VPS, lue
  avec `ssh-keyscan -t ed25519 79.137.35.129` et comparée à `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` sur le
  VPS (sinon la clé est acceptée à la première connexion).

VPS, compte `deploy` (membre du groupe `docker`, donc équivalent root : sa clé doit être bridée). Dans
`~deploy/.ssh/authorized_keys`, une seule ligne pour Jenkins :

```
restrict,command="/opt/restauspace/deploy/production/deploy-ssh.sh" ssh-ed25519 AAAA… jenkins-restauspace
```

`restrict` coupe terminal, redirections de ports et agent ; `deploy-ssh.sh` n'accepte que `./deploy.sh <commit>`
(hexadécimal) et refuse tout le reste. Vérifier depuis le Mac : `ssh deploy@79.137.35.129 id` doit répondre
« Commande refusée ».

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
- Traefik partagé : voir « Traefik partagé » ci-dessous.
- L'API lit l'adresse du client dans `X-Forwarded-For`, accepté seulement des réseaux privés (`ReverseProxy__Enabled`).
- Back-office et routes `/v1/admin` de l'API : restreindre `ADMIN_ALLOWED_IPS` dès que possible ; validation en deux
  étapes obligatoire pour les administrateurs.
- PostgreSQL : l'API n'a que la lecture et l'écriture des données (`DATABASE_APP_USER`) ; le superutilisateur n'est
  jamais joignable à distance.
- PostgreSQL en `VerifyFull` (« TLS vérifié »).
- Jenkins : identifiants dans le seul job de livraison, clé SSH bridée (« Jenkins »).
- `.env` et `api.env` ne sont jamais versionnés (`.gitignore`).

### Traefik partagé

Traefik sert aussi un autre projet. Un conteneur de cet autre projet qui porterait des labels ``Host(`api.restauspace.com`)``
pourrait capter le trafic de RestauSpace. Dans la configuration statique de Traefik (son compose ou `traefik.yml`) :

```yaml
providers:
  docker:
    exposedByDefault: false            # seuls les conteneurs avec traefik.enable=true
    network: proxy                     # réseau par défaut de l'autre projet
    # Option : n'accepter que les conteneurs étiquetés, par projet.
    # constraints: "Label(`traefik.project`, `restauspace`) || Label(`traefik.project`, `autre`)"
```

et dans son compose :

```yaml
services:
  traefik:
    networks: [proxy, restauspace-edge]
networks:
  restauspace-edge:
    external: true
```

À vérifier après chaque changement : `docker network inspect restauspace-edge` ne liste que `traefik` et les
conteneurs `restauspace-*` (`deploy.sh` le signale aussi) ; aucun autre conteneur ne déclare un routeur sur
`*.restauspace.com` (`docker ps -q | xargs docker inspect -f '{{.Name}} {{json .Config.Labels}}' | grep restauspace`).
Le tableau de bord de Traefik (`api.dashboard`) reste fermé ou protégé, et le socket Docker n'est monté qu'en lecture
seule (`/var/run/docker.sock:/var/run/docker.sock:ro`).

## 5. Sauvegardes

Demandées depuis le back-office (ou chaque jour à 02:30 par l'API), faites par un agent sur le serveur de base :
`deploy/database/backup-agent.sh`, lancé chaque minute par un timer systemd. L'agent lit la file `backups.*` en local
(`postgres`, socket), fait `pg_dump -Fc`, vérifie, chiffre avec `age`, envoie dans OVH Object Storage (`rclone`) et
écrit le statut. L'API n'a qu'une clé S3 en lecture (liens de téléchargement).

**Installation (serveur de base, une fois)** :

1. OVH : un bucket privé `restauspace-backups` et deux utilisateurs S3 : l'agent (lecture, écriture, suppression sur ce
   bucket) et l'API (lecture seule, `Backups__Storage__*` dans `api.env`).
2. Sur le serveur :

```bash
sudo apt install -y age rclone
sudo install -d -m 700 /root/restauspace-backup
sudo age-keygen -o /root/restauspace-backup/age-identity.txt     # affiche « public key: age1... »
sudo chmod 600 /root/restauspace-backup/age-identity.txt
sudo ./deploy/database/backup-agent.sh --install                  # script, unités systemd, /etc/restauspace-backup.env
sudo nano /etc/restauspace-backup.env                             # age1..., clés S3 de l'agent, endpoint, région
sudo systemctl enable --now restauspace-backup.timer
sudo restauspace-backup-agent --status                            # agent, dernières sauvegardes
systemctl status restauspace-backup.timer
journalctl -t restauspace-backup -f
```

Copier `age-identity.txt` hors ligne (coffre) : sans elle, aucune sauvegarde n'est lisible. Elle reste aussi sur le
serveur pour restaurer : le chiffrement protège la copie dans le stockage, pas le serveur.
`--install` n'écrase jamais un `/etc/restauspace-backup.env` existant.

**Back-office** : demander une sauvegarde, la télécharger (code 2FA, lien de 5 minutes, fichier chiffré :
`age -d -i age-identity.txt <fichier>.dump.age > restore.dump`), la supprimer, la restaurer.

**Restauration** : refusée par l'agent (`disabled`) tant que `RESTORE_ENABLED=true` n'est pas dans
`/etc/restauspace-backup.env`. Une fois activée : demande dans le back-office (code 2FA, nom de la base), 5 minutes
pour annuler, puis l'agent restaure dans une base temporaire, garde l'historique `backups`, coupe les connexions et
échange les noms. L'ancienne base reste sous `es_restaurant_before_<date>` (jamais supprimée automatiquement). Il faut
la place libre de la base actuelle plus une marge, sinon `restore_failed`.

Après une restauration, sur le serveur applicatif : `./deploy.sh <version actuelle>` (migrations et droits).

Retour arrière (API arrêtée), sur le serveur de base :

```sql
-- sudo -u postgres psql -d postgres
ALTER DATABASE es_restaurant RENAME TO es_restaurant_failed;
ALTER DATABASE es_restaurant_before_<date> RENAME TO es_restaurant;
GRANT CONNECT ON DATABASE es_restaurant TO es_restaurant_user, es_restaurant_app;   -- et monitoring s'il existe
```

puis `./deploy.sh <version actuelle>`. Supprimer les bases `_before_` à la main quand elles ne servent plus
(`DROP DATABASE ...`).

Tester une restauration avant l'ouverture, puis une fois par trimestre (`RESTORE_ENABLED=true` le temps du test).

**Avec `deploy/database` (Docker)** : `backup.sh` reste utilisé par le conteneur `backup` (variables `BACKUP_*` du
`.env`, `rclone.conf` dans `deploy/database/rclone/`). Il n'est pas relié au back-office.

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
