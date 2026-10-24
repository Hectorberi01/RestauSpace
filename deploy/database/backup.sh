#!/bin/sh
# Sauvegarde PostgreSQL : dump de la base (format custom, pg_restore) + rôles, fichiers lisibles par le seul propriétaire.
# Conteneur « backup » : une sauvegarde toutes les INTERVAL_SECONDS. Serveur sans Docker (cron) : ./backup.sh --once.
# Variables : PG* (connexion), BACKUP_DIR (/backups), RETENTION_DAYS (14), KEEP_MIN (3),
#   BACKUP_AGE_RECIPIENT : clé publique age (age1...) ; dumps chiffrés, la clé privée reste HORS du serveur ;
#   BACKUP_RCLONE_REMOTE : copie hors du serveur (ex. r2:restauspace-backups) ;
#   BACKUP_PING_URL : surveillance (ex. healthchecks.io) ; appelée en cas de succès, /fail en cas d'échec.
set -eu
umask 077
dir="${BACKUP_DIR:-/backups}"
mkdir -p "$dir"

ping() {
  [ -n "${BACKUP_PING_URL:-}" ] || return 0
  wget -q -O /dev/null -T 10 "$BACKUP_PING_URL$1" 2>/dev/null || curl -fsS -m 10 -o /dev/null "$BACKUP_PING_URL$1" 2>/dev/null || true
}

# Chiffre si une clé publique est donnée ; sinon garde le fichier tel quel.
seal() {
  if [ -n "${BACKUP_AGE_RECIPIENT:-}" ]; then
    command -v age >/dev/null || { echo "age introuvable (BACKUP_AGE_RECIPIENT défini)" >&2; return 1; }
    age -r "$BACKUP_AGE_RECIPIENT" -o "$1.age" "$1" && rm -f "$1" && echo "$1.age"
  else
    echo "$1"
  fi
}

once() {
  stamp="$(date -u +%Y%m%d-%H%M%S)"
  base="$dir/${PGDATABASE}-$stamp"
  pg_dump --format=custom --compress=6 --file="$base.dump.tmp" || return 1
  mv "$base.dump.tmp" "$base.dump"
  # Rôles et droits (pg_dump ne les contient pas) ; demande un superutilisateur, sinon ignoré.
  pg_dumpall --globals-only --no-role-passwords --file="$base.globals.sql" 2>/dev/null || rm -f "$base.globals.sql"
  dump="$(seal "$base.dump")" || return 1
  [ -f "$base.globals.sql" ] && { seal "$base.globals.sql" >/dev/null || return 1; }
  if [ -n "${BACKUP_RCLONE_REMOTE:-}" ]; then
    rclone copy "$dir" "$BACKUP_RCLONE_REMOTE" --include "${PGDATABASE}-$stamp.*" || return 1
  fi
  echo "sauvegarde OK : $dump ($(du -h "$dump" | cut -f1))"
}

# Rétention : seulement après un succès, et toujours au moins KEEP_MIN sauvegardes.
purge() {
  keep="${KEEP_MIN:-3}"
  # shellcheck disable=SC2012
  ls -1t "$dir"/*.dump "$dir"/*.dump.age 2>/dev/null | tail -n "+$((keep + 1))" | while read -r old; do
    if [ -n "$(find "$old" -mtime "+${RETENTION_DAYS:-14}")" ]; then
      rm -f "$old" "${old%.dump*}.globals.sql" "${old%.dump*}.globals.sql.age"
    fi
  done
}

run() {
  if once; then
    purge
    ping ""
  else
    rm -f "$dir"/*.tmp
    echo "sauvegarde ÉCHOUÉE : anciennes sauvegardes conservées" >&2
    ping "/fail"
    return 1
  fi
}

if [ "${1:-}" = "--once" ]; then
  run
  exit $?
fi

while true; do
  run || true
  sleep "${INTERVAL_SECONDS:-86400}"
done
