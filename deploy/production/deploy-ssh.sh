#!/usr/bin/env bash
# Seule commande permise à la clé SSH de Jenkins (authorized_keys : restrict,command="…/deploy-ssh.sh").
# Accepte « ./deploy.sh <commit> » (avec ou sans « cd … && » devant) et rien d'autre.
set -euo pipefail
cmd="${SSH_ORIGINAL_COMMAND:-}"
if [[ "$cmd" =~ ^(cd[[:space:]]+[A-Za-z0-9_./~-]+[[:space:]]+\&\&[[:space:]]+)?\./deploy\.sh[[:space:]]+([0-9a-f]{7,40})$ ]]; then
  commit="${BASH_REMATCH[2]}"
  cd "$(dirname "$0")"
  exec ./deploy.sh "$commit"
fi
echo "Commande refusée" >&2
logger -t restauspace-deploy "commande SSH refusée" 2>/dev/null || true
exit 1
