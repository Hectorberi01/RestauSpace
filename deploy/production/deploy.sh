#!/usr/bin/env bash
# Déploiement sur le serveur applicatif : met le code à jour, construit les images, applique les migrations, redémarre.
#   ./deploy.sh              branche courante
#   ./deploy.sh v1.4.0       une étiquette ou un commit précis
set -euo pipefail
cd "$(dirname "$0")"

[ -f .env ] || { echo ".env manquant : cp .env.example .env" >&2; exit 1; }
[ -f api.env ] || { echo "api.env manquant : cp api.env.example api.env" >&2; exit 1; }
for f in .env api.env; do
  [ "$(stat -c %a "$f")" = "600" ] || { chmod 600 "$f"; echo "$f : droits ramenés à 600"; }
done
docker network inspect "$(grep -E '^TRAEFIK_NETWORK=' .env | cut -d= -f2 || echo proxy)" >/dev/null 2>&1 \
  || { echo "Réseau Traefik introuvable (TRAEFIK_NETWORK)" >&2; exit 1; }

git -C ../.. fetch --tags --quiet
if [ -n "${1:-}" ]; then git -C ../.. checkout --quiet "$1"; else git -C ../.. pull --ff-only --quiet; fi
release="$(git -C ../.. rev-parse --short HEAD)"
sed -i "s/^RELEASE=.*/RELEASE=${release}/" .env
echo "Version ${release}"

docker compose build --pull
docker compose up -d --remove-orphans
docker compose ps

# Images des versions précédentes : on garde les 3 dernières de chaque application.
for image in restauspace-api restauspace-console restauspace-site restauspace-admin; do
  docker image ls "$image" --format '{{.Tag}}' | grep -v -e '^latest$' -e "^${release}$" | tail -n +3 \
    | xargs -r -I{} docker image rm "$image:{}" >/dev/null 2>&1 || true
done
echo "Déployé : ${release}"
