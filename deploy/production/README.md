# Mise en production (restauspace.com)

Trois serveurs. Ce dossier ne concerne que le serveur applicatif : l'API et les trois sites web, derrière le Traefik
déjà installé (réseau Docker `proxy`, entrypoint `websecure`, certresolver `letsencrypt`).

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
cp .env.example .env && chmod 600 .env    # POSTGRES_*, BIND_ADDRESS=10.10.0.2
./make-cert.sh                             # certificat TLS auto-signé
docker compose up -d                       # postgres + sauvegarde quotidienne dans backups/
```

Sinon, reprendre seulement : TLS (`ssl = on`), `pg_hba.conf` limité à l'IP du serveur applicatif, la base et le compte
de l'application.

Réseau privé entre les deux serveurs : la base écoute seulement sur `10.10.0.2` (`BIND_ADDRESS`), jamais sur l'IP
publique, et `pg_hba.conf` n'accepte que `10.10.0.0/24` en TLS. Docker publie ses ports avant UFW : ne pas mettre
`BIND_ADDRESS=0.0.0.0`.

Vérifier depuis le serveur applicatif :

```bash
docker run --rm postgres:16-alpine psql \
  "postgresql://<utilisateur>:<mdp>@10.10.0.2:5432/<base>?sslmode=require" -c "select 1"
```

`DATABASE_SSL_MODE=Require` reste conseillé, même sur le réseau privé. `Disable` seulement pour un PostgreSQL déjà
installé sans TLS (et `host` au lieu de `hostssl` dans son `pg_hba.conf`).

## 3. Serveur applicatif

```bash
cd deploy/production
./generate-secrets.sh                       # clés à recopier (une seule fois)
cp .env.example .env && cp api.env.example api.env && chmod 600 .env api.env
#   .env    : base, OBSERVABILITY_OTLP_URL + OBSERVABILITY_TOKEN, FILES_ORIGINS, contacts du site
#   api.env : clés générées, SMTP, R2, administrateurs
./deploy.sh
```

`deploy.sh` : `git pull`, build des 4 images (étiquetées par le commit), migrations, démarrage, nettoyage des vieilles
images. `./deploy.sh <commit>` déploie une version précise (retour arrière si les migrations sont compatibles).

Les variables `NEXT_PUBLIC_*` sont figées au build : après un changement de `DOMAIN`, des contacts ou de
`STOREFRONT_KEY`, relancer `./deploy.sh`.

### Observabilité

L'API envoie traces, métriques et logs en OTLP/HTTP à `OBSERVABILITY_OTLP_URL`, avec l'en-tête
`Authorization: Bearer OBSERVABILITY_TOKEN`. Le serveur d'observabilité doit accepter ce jeton. URL vide : aucun envoi.

### Premier démarrage

1. Créer le compte administrateur (email déclaré dans `Authorization__PlatformAdminEmails__0`), valider le lien reçu,
   se connecter sur `https://admin.restauspace.com` et activer la validation en deux étapes :
   ```bash
   curl -X POST https://api.restauspace.com/v1/auth/register -H 'Content-Type: application/json' \
        -d '{"email":"<admin>","password":"<mot de passe solide>"}'
   ```
2. Back-office › Paramètres : prestataires de paiement, notifications, plans.
3. Console › Vente en ligne : créer l'application « Site web », recopier sa clé dans `STOREFRONT_KEY`, redéployer.

## 4. Sécurité

- Seul Traefik est exposé ; les conteneurs n'ouvrent aucun port.
- L'API lit l'adresse du client dans `X-Forwarded-For`, accepté seulement des réseaux privés (`ReverseProxy__Enabled`).
- Back-office : restreindre `ADMIN_ALLOWED_IPS` dès que possible.
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
