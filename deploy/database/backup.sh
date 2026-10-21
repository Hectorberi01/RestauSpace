#!/bin/sh
# Sauvegarde PostgreSQL : un dump compressé (format custom, restaurable avec pg_restore) toutes les INTERVAL_SECONDS,
# les plus anciens que RETENTION_DAYS jours supprimés. Variables PG* fournies par docker compose.
set -eu
while true; do
  file="/backups/${PGDATABASE}-$(date -u +%Y%m%d-%H%M%S).dump"
  if pg_dump --format=custom --compress=6 --file="$file.tmp"; then
    mv "$file.tmp" "$file"
    echo "sauvegarde OK : $file ($(du -h "$file" | cut -f1))"
  else
    rm -f "$file.tmp"
    echo "sauvegarde ÉCHOUÉE" >&2
  fi
  find /backups -name "*.dump" -type f -mtime "+${RETENTION_DAYS}" -delete
  sleep "${INTERVAL_SECONDS}"
done
