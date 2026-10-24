#!/usr/bin/env bash
# Déploiement sur le serveur applicatif : met le code à jour, récupère ou construit les images, migre, redémarre.
#   ./deploy.sh              branche courante
#   ./deploy.sh 1a2b3c4      un commit ou une étiquette (Jenkins passe le commit construit)
# IMAGE_REGISTRY renseigné dans .env : images tirées du registre. Vide : construites ici.
set -euo pipefail
cd "$(dirname "$0")"

[ -f .env ] || { echo ".env manquant : cp .env.example .env" >&2; exit 1; }
[ -f api.env ] || { echo "api.env manquant : cp api.env.example api.env" >&2; exit 1; }
for f in .env api.env; do
  [ "$(stat -c %a "$f")" = "600" ] || { chmod 600 "$f"; echo "$f : droits ramenés à 600"; }
done
# Chaque clé de chiffrement ou de signature doit être unique (generate-secrets.sh) : une valeur partagée ferait
# tomber toutes les autres avec elle.
duplicates="$(grep -E '^(Identity__AccessTokens__SigningKey|Identity__TwoFactor__SecretKey|Payments__CredentialsKey|Notifications__CredentialsKey|Notifications__OtpKey|Integrations__SecretsKey|Fiscal__TokenKey|Storefront__TrackingKey|Files__PublicMedia__SigningKey)=' api.env \
  | cut -d= -f2- | sort | uniq -d | wc -l)"
[ "$duplicates" -eq 0 ] || { echo "api.env : plusieurs clés ont la même valeur, régénérez-les avec ./generate-secrets.sh" >&2; exit 1; }
# Réseau edge : propre à RestauSpace, avec le sous-réseau déclaré (EDGE_SUBNET : seule source de X-Forwarded-For).
edge="$(grep -E '^TRAEFIK_NETWORK=' .env | cut -d= -f2- || true)"; edge="${edge:-restauspace-edge}"
docker network inspect "$edge" >/dev/null 2>&1 \
  || { echo "Réseau $edge introuvable : voir README, « Réseaux »" >&2; exit 1; }
subnet="$(docker network inspect "$edge" -f '{{range .IPAM.Config}}{{.Subnet}}{{end}}')"
[ "$subnet" = "$(grep -E '^EDGE_SUBNET=' .env | cut -d= -f2- || true)" ] \
  || { echo "EDGE_SUBNET doit valoir le sous-réseau de $edge ($subnet)" >&2; exit 1; }
others="$(docker network inspect "$edge" -f '{{range .Containers}}{{.Name}} {{end}}' | tr ' ' '\n' | grep -v -e '^$' -e '^restauspace-' -e '^traefik$' || true)"
[ -z "$others" ] || echo "Attention : conteneurs étrangers sur $edge : $others" >&2

git -C ../.. fetch --tags --quiet
if [ -n "${1:-}" ]; then
  git -C ../.. checkout --quiet "$1"
else
  git -C ../.. checkout --quiet "${DEPLOY_BRANCH:-main}" && git -C ../.. pull --ff-only --quiet
fi
# 12 caractères : la même étiquette que celle des images poussées par Jenkins.
release="$(git -C ../.. rev-parse --short=12 HEAD)"
sed -i "s/^RELEASE=.*/RELEASE=${release}/" .env
echo "Version ${release}"

registry="$(grep -E '^IMAGE_REGISTRY=' .env | cut -d= -f2- || true)"
if [ -n "$registry" ]; then
  docker compose pull --quiet
else
  docker compose -f docker-compose.yml -f docker-compose.build.yml build --pull
fi
docker compose up -d --remove-orphans
docker compose ps

# Images des versions précédentes : on garde les 3 dernières de chaque application.
for image in restauspace-api restauspace-console restauspace-site restauspace-admin; do
  docker image ls "${registry}${image}" --format '{{.Tag}}' | grep -v -e '^latest$' -e "^${release}$" | tail -n +3 \
    | xargs -r -I{} docker image rm "${registry}${image}:{}" >/dev/null 2>&1 || true
done
echo "Déployé : ${release}"
