#!/usr/bin/env bash
# Agent de sauvegarde et de restauration RestauSpace, sur le serveur de base (PostgreSQL natif), en root.
# Lit la file backups.* de la base en local (postgres, socket), lancé chaque minute par un timer systemd.
# Usage : backup-agent.sh [--once (défaut) | --status | --install]
set -euo pipefail
umask 077
SELF=$(readlink -f "$0")
cd /

VERSION="1.0.0"
CONFIG_FILE="${BACKUP_AGENT_CONFIG:-/etc/restauspace-backup.env}"
UUID_RE='^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
KEY_RE='^[a-z0-9_]+/[0-9]{4}/[0-9]{2}/[0-9a-f-]{36}\.dump\.age$'

TMP="" ERR=/dev/null HB_PID="" ABORT=""
CUR_BACKUP="" CUR_STAGE=""
CUR_RESTORE="" R_PHASE="" R_TMPDB="" R_ACL=""

# Journal court (id, code), jamais de secret ni de contenu. Sous systemd, stdout suffit (SyslogIdentifier).
log() {
  echo "$*"
  [ -n "${INVOCATION_ID:-}" ] || logger -t restauspace-backup -- "$*" 2>/dev/null || true
}
die() { log "erreur : $*"; exit 1; }

# ---------- Configuration ----------

load_config() {
  [ -f "$CONFIG_FILE" ] || die "configuration absente ($CONFIG_FILE)"
  # Fichier lu par root : refusé s'il n'est pas à root ou s'il est lisible ou modifiable par d'autres.
  [ "$(stat -c '%u %a' "$CONFIG_FILE")" = "0 600" ] || die "$CONFIG_FILE doit appartenir à root, en 600"
  set -a
  # shellcheck disable=SC1090
  . "$CONFIG_FILE"
  set +a

  DB_NAME="${DB_NAME:-es_restaurant}"
  DB_OWNER="${DB_OWNER:-es_restaurant_user}"
  DB_APP="${DB_APP:-es_restaurant_app}"
  PG_BIN="${PG_BIN:-/usr/lib/postgresql/18/bin}"
  BACKUP_AGE_IDENTITY="${BACKUP_AGE_IDENTITY:-/root/restauspace-backup/age-identity.txt}"
  WORK_DIR="${WORK_DIR:-/var/lib/restauspace-backup}"
  RESTORE_ENABLED="${RESTORE_ENABLED:-false}"
  HEARTBEAT_SECONDS="${HEARTBEAT_SECONDS:-30}"
  JOB_TIMEOUT_MINUTES="${JOB_TIMEOUT_MINUTES:-360}"
  RESTORE_DISK_MARGIN_MB="${RESTORE_DISK_MARGIN_MB:-1024}"
  export RCLONE_CONFIG="${RCLONE_CONFIG:-/dev/null}"   # remote décrit par les variables RCLONE_CONFIG_<NOM>_*

  # Le nom de base sert à construire des noms de base et des clés : lettres minuscules, chiffres, _ ; 40 au plus.
  [[ $DB_NAME =~ ^[a-z0-9_]{1,40}$ ]] || die "DB_NAME invalide"
  [[ $DB_OWNER =~ ^[a-z_][a-z0-9_]{0,62}$ ]] || die "DB_OWNER invalide"
  [[ $DB_APP =~ ^[a-z_][a-z0-9_]{0,62}$ ]] || die "DB_APP invalide"
  [[ ${BACKUP_AGE_RECIPIENT:-} =~ ^age1[0-9a-z]{20,}$ ]] || die "BACKUP_AGE_RECIPIENT invalide"
  [[ ${RCLONE_REMOTE:-} =~ ^[A-Za-z0-9_-]+:[A-Za-z0-9._/-]+$ && $RCLONE_REMOTE != *..* ]] || die "RCLONE_REMOTE invalide"
  [[ $HEARTBEAT_SECONDS =~ ^[0-9]+$ && $JOB_TIMEOUT_MINUTES =~ ^[0-9]+$ && $RESTORE_DISK_MARGIN_MB =~ ^[0-9]+$ ]] \
    || die "valeur numérique invalide"
  [ -z "${BACKUP_PING_URL:-}" ] || [[ $BACKUP_PING_URL =~ ^https:// ]] || die "BACKUP_PING_URL doit être en https"
  local t
  for t in psql pg_dump pg_restore; do [ -x "$PG_BIN/$t" ] || die "PG_BIN introuvable"; done
  for t in age rclone sha256sum flock setpriv timeout; do command -v "$t" >/dev/null || die "$t manquant"; done

  # Outils PostgreSQL en postgres, environnement vide (aucune variable S3 transmise).
  # setpriv plutôt que sudo : fonctionne avec NoNewPrivileges=true (pas de binaire setuid).
  PGRUN=(env -i PATH=/usr/bin:/bin LANG=C.UTF-8 HOME=/var/lib/postgresql)
  [ -z "${PGHOST:-}" ] || PGRUN+=("PGHOST=$PGHOST")
  [ -z "${PGPORT:-}" ] || PGRUN+=("PGPORT=$PGPORT")
  PGRUN+=(setpriv --reuid=postgres --regid=postgres --init-groups --)
  TIMEOUT=(timeout --kill-after=60 "$((JOB_TIMEOUT_MINUTES * 60))")
}

# ---------- Outils ----------

# Requête SQL par stdin (les variables :'x' ne marchent pas avec -c). sql <base> <requête> [-v nom=valeur ...]
sql() {
  local db=$1 q=$2
  shift 2
  printf '%s\n' "$q" | "${PGRUN[@]}" "$PG_BIN/psql" -X -At -q -v ON_ERROR_STOP=1 -d "$db" "$@" 2>>"$ERR"
}

rcl() { rclone -q "$@" 2>>"$ERR"; }

# Taille d'un objet distant (vide s'il n'existe pas).
remote_size() {
  rcl lsjson --stat "$RCLONE_REMOTE/$1" | sed -n 's/^[[:space:]]*"Size":[[:space:]]*\([0-9]*\).*/\1/p' || true
}

ping_monitor() {
  [ -n "${BACKUP_PING_URL:-}" ] || return 0
  curl -fsS -m 10 -o /dev/null "$BACKUP_PING_URL$1" 2>/dev/null || true
}

# Clé de stockage acceptée : regex de la spec, préfixe de NOTRE base et nom de fichier = id de la sauvegarde.
valid_key() {
  local key=$1 id=$2
  [[ $key =~ $KEY_RE ]] || return 1
  [[ $key =~ ^${DB_NAME}/[0-9]{4}/[0-9]{2}/${id}\.dump\.age$ ]]
}

start_heartbeat() {
  local q
  case $1 in
    backups) q="UPDATE backups.backups SET heartbeat_at_utc = now() WHERE id = :'id' AND status IN ('Running','Uploading')" ;;
    restores) q="UPDATE backups.restores SET heartbeat_at_utc = now() WHERE id = :'id' AND status = 'Running'" ;;
  esac
  (
    exec 9>&-
    s=""
    trap '[ -z "$s" ] || kill "$s" 2>/dev/null; exit 0' TERM
    while :; do
      sleep "$HEARTBEAT_SECONDS" & s=$!
      wait "$s" || true
      sql "$DB_NAME" "$q" -v id="$2" >/dev/null || true
    done
  ) &
  HB_PID=$!
}

stop_heartbeat() {
  [ -n "$HB_PID" ] || return 0
  kill "$HB_PID" 2>/dev/null || true
  wait "$HB_PID" 2>/dev/null || true
  HB_PID=""
}

# ---------- Passage ----------

upsert_agent() {
  sql "$DB_NAME" "INSERT INTO backups.agent (id, host, version, last_seen_at_utc, restore_enabled)
    VALUES (1, :'host', :'ver', now(), :'re'::boolean)
    ON CONFLICT (id) DO UPDATE SET host = excluded.host, version = excluded.version,
      last_seen_at_utc = excluded.last_seen_at_utc, restore_enabled = excluded.restore_enabled" \
    -v host="$(hostname -s)" -v ver="$VERSION" -v re="$([ "$RESTORE_ENABLED" = true ] && echo true || echo false)" >/dev/null
}

# Travaux laissés par un agent arrêté (flock : aucun autre passage ne tourne).
recover_orphans() {
  local id db
  for id in $(sql "$DB_NAME" "UPDATE backups.backups SET status = 'Failed', failure_code = 'agent_restarted',
      completed_at_utc = now() WHERE status IN ('Running','Uploading') RETURNING id"); do
    log "sauvegarde $id : agent_restarted"
  done
  for id in $(sql "$DB_NAME" "UPDATE backups.restores SET status = 'Failed', failure_code = 'timeout',
      completed_at_utc = now() WHERE status = 'Running' RETURNING id"); do
    log "restauration $id : timeout (agent arrêté)"
  done
  # Bases temporaires d'une restauration interrompue (jamais les bases _before_).
  for db in $(sql postgres "SELECT datname FROM pg_database WHERE datname ~ ('^' || :'db' || '_restore_[0-9]{8}t[0-9]{6}$')" \
      -v db="$DB_NAME"); do
    [[ $db =~ ^${DB_NAME}_restore_[0-9]{8}t[0-9]{6}$ ]] || continue
    sql postgres "SELECT format('DROP DATABASE IF EXISTS %I WITH (FORCE)', :'d') \gexec" -v d="$db" >/dev/null \
      && log "base temporaire supprimée : $db"
  done
}

fail_backup() {
  sql "$DB_NAME" "UPDATE backups.backups SET status = 'Failed', failure_code = :'code', completed_at_utc = now()
    WHERE id = :'id' AND status IN ('Running','Uploading')" -v id="$1" -v code="$2" >/dev/null || true
  log "sauvegarde $1 : échec $2"
  CUR_BACKUP=""
  stop_heartbeat
  ping_monitor /fail
}

run_backup() {
  local id
  id=$(sql "$DB_NAME" "UPDATE backups.backups SET status = 'Running', started_at_utc = now(), heartbeat_at_utc = now()
    WHERE id = (SELECT id FROM backups.backups WHERE status = 'Pending' ORDER BY created_at_utc
                FOR UPDATE SKIP LOCKED LIMIT 1) AND status = 'Pending'
    RETURNING id")
  [ -n "$id" ] || return 0
  [[ $id =~ $UUID_RE ]] || die "id de sauvegarde inattendu"
  CUR_BACKUP=$id CUR_STAGE=dump_failed
  log "sauvegarde $id : début"
  start_heartbeat backups "$id"

  local key dump="$TMP/$id.dump" enc="$TMP/$id.dump.age" rc sum size
  key="$DB_NAME/$(date -u +%Y/%m)/$id.dump.age"

  # Dump en clair dans le dossier temporaire (700, effacé par trap) : pg_restore --list lit le fichier entier,
  # ce qu'un flux ne permet pas simplement ; il est effacé dès le chiffrement.
  rc=0
  "${TIMEOUT[@]}" "${PGRUN[@]}" "$PG_BIN/pg_dump" -Fc -d "$DB_NAME" >"$dump" 2>>"$ERR" || rc=$?
  if [ "$rc" -ne 0 ] || [ ! -s "$dump" ]; then
    fail_backup "$id" "$([ "$rc" -eq 124 ] && echo timeout || echo dump_failed)"; return 0
  fi
  CUR_STAGE=verify_failed
  if ! "${PGRUN[@]}" "$PG_BIN/pg_restore" --list <"$dump" >/dev/null 2>>"$ERR"; then
    fail_backup "$id" verify_failed; return 0
  fi
  CUR_STAGE=encrypt_failed
  rc=0
  age -r "$BACKUP_AGE_RECIPIENT" -o "$enc" "$dump" 2>>"$ERR" || rc=$?
  rm -f "$dump"
  if [ "$rc" -ne 0 ] || [ ! -s "$enc" ]; then fail_backup "$id" encrypt_failed; return 0; fi
  sum=$(sha256sum "$enc" | cut -c1-64)
  size=$(stat -c %s "$enc")

  CUR_STAGE=upload_failed
  if [ -z "$(sql "$DB_NAME" "UPDATE backups.backups SET status = 'Uploading', storage_key = :'key', heartbeat_at_utc = now()
      WHERE id = :'id' AND status = 'Running' RETURNING id" -v id="$id" -v key="$key")" ]; then
    log "sauvegarde $id : statut changé par l'API, abandon"; CUR_BACKUP=""; stop_heartbeat; return 0
  fi
  if ! rcl copyto "$enc" "$RCLONE_REMOTE/$key" || [ "$(remote_size "$key")" != "$size" ]; then
    rcl deletefile "$RCLONE_REMOTE/$key" || true
    fail_backup "$id" upload_failed; return 0
  fi
  rm -f "$enc"

  stop_heartbeat
  if [ -n "$(sql "$DB_NAME" "UPDATE backups.backups SET status = 'Completed', size_bytes = :'size', checksum_sha256 = :'sum',
      completed_at_utc = now(), heartbeat_at_utc = now(), failure_code = NULL
      WHERE id = :'id' AND status = 'Uploading' RETURNING id" -v id="$id" -v size="$size" -v sum="$sum")" ]; then
    log "sauvegarde $id : terminée ($size octets)"
    ping_monitor ""
  else
    log "sauvegarde $id : statut changé par l'API pendant l'envoi"
  fi
  CUR_BACKUP=""
}

run_deletions() {
  local id key
  for id in $(sql "$DB_NAME" "SELECT id FROM backups.backups WHERE status = 'Deleting' ORDER BY created_at_utc LIMIT 50"); do
    [[ $id =~ $UUID_RE ]] || continue
    key=$(sql "$DB_NAME" "SELECT coalesce(storage_key, '') FROM backups.backups WHERE id = :'id'" -v id="$id")
    if [ -n "$key" ] && valid_key "$key" "$id"; then
      # Objet absent : déjà supprimé. Erreur réseau : on réessaie au prochain passage.
      if ! rcl deletefile "$RCLONE_REMOTE/$key" && [ -n "$(remote_size "$key")" ]; then
        log "sauvegarde $id : suppression distante échouée, nouvel essai plus tard"; continue
      fi
    elif [ -n "$key" ]; then
      log "sauvegarde $id : clé invalide, stockage non touché"
    fi
    sql "$DB_NAME" "UPDATE backups.backups SET status = 'Deleted' WHERE id = :'id' AND status = 'Deleting'" -v id="$id" >/dev/null
    log "sauvegarde $id : supprimée"
  done
}

# Droits CONNECT/TEMPORARY d'une liste ACL (texte aclitem[]) reposés sur une base.
apply_acl() {
  sql postgres "SELECT format('GRANT %s ON DATABASE %I TO %s', a.privilege_type, :'target',
      CASE WHEN a.grantee = 0 THEN 'PUBLIC' ELSE quote_ident(r.rolname) END)
    FROM aclexplode(:'acl'::aclitem[]) a LEFT JOIN pg_roles r ON r.oid = a.grantee
    WHERE a.grantee = 0 OR r.oid IS NOT NULL \gexec" -v target="$1" -v acl="$2" >/dev/null
}

# Copie du schéma backups actuel (structure, données, historique des migrations) dans la base restaurée.
copy_backups_schema() {
  sql "$1" "DROP SCHEMA IF EXISTS backups CASCADE" >/dev/null || return 1
  "${PGRUN[@]}" "$PG_BIN/pg_dump" -Fc -n backups -d "$DB_NAME" 2>>"$ERR" \
    | "${PGRUN[@]}" "$PG_BIN/pg_restore" --exit-on-error -d "$1" 2>>"$ERR"
}

fail_restore() {
  local id=$1 code=$2
  stop_heartbeat
  # Échec avant l'échange : droits de l'ancienne base remis, base temporaire supprimée.
  if [ "$R_PHASE" = revoked ]; then apply_acl "$DB_NAME" "$R_ACL" || log "restauration $id : droits CONNECT à reposer à la main"; fi
  if [ -n "$R_TMPDB" ]; then
    sql postgres "SELECT format('DROP DATABASE IF EXISTS %I WITH (FORCE)', :'d') \gexec" -v d="$R_TMPDB" >/dev/null || true
  fi
  sql "$DB_NAME" "UPDATE backups.restores SET status = 'Failed', failure_code = :'code', completed_at_utc = now()
    WHERE id = :'id' AND status = 'Running'" -v id="$id" -v code="$code" >/dev/null || true
  log "restauration $id : échec $code"
  CUR_RESTORE="" R_PHASE="" R_TMPDB=""
}

run_restore() {
  local due="FROM backups.restores WHERE status = 'Pending' AND not_before_utc <= now()
             ORDER BY created_at_utc FOR UPDATE SKIP LOCKED LIMIT 1"
  local id
  if [ "$RESTORE_ENABLED" != true ]; then
    for id in $(sql "$DB_NAME" "UPDATE backups.restores SET status = 'Failed', failure_code = 'disabled', completed_at_utc = now()
        WHERE id = (SELECT id $due) AND status = 'Pending' RETURNING id"); do
      log "restauration $id : disabled"
    done
    return 0
  fi
  id=$(sql "$DB_NAME" "UPDATE backups.restores SET status = 'Running', started_at_utc = now(), heartbeat_at_utc = now()
    WHERE id = (SELECT id $due) AND status = 'Pending' RETURNING id")
  [ -n "$id" ] || return 0
  [[ $id =~ $UUID_RE ]] || die "id de restauration inattendu"
  CUR_RESTORE=$id R_PHASE="" R_TMPDB=""
  log "restauration $id : début"
  start_heartbeat restores "$id"

  # La ligne n'est pas une source de confiance : statut, clé et empreinte vérifiés.
  local bid bstatus key sum
  bid=$(sql "$DB_NAME" "SELECT backup_id FROM backups.restores WHERE id = :'id'" -v id="$id")
  [[ $bid =~ $UUID_RE ]] || { fail_restore "$id" invalid_key; return 0; }
  bstatus=$(sql "$DB_NAME" "SELECT status FROM backups.backups WHERE id = :'b'" -v b="$bid")
  key=$(sql "$DB_NAME" "SELECT coalesce(storage_key, '') FROM backups.backups WHERE id = :'b'" -v b="$bid")
  sum=$(sql "$DB_NAME" "SELECT coalesce(checksum_sha256, '') FROM backups.backups WHERE id = :'b'" -v b="$bid")
  if [ "$bstatus" != Completed ] || ! valid_key "$key" "$bid"; then fail_restore "$id" invalid_key; return 0; fi
  [[ $sum =~ ^[0-9a-f]{64}$ ]] || { fail_restore "$id" checksum_mismatch; return 0; }

  # Place libre dans le dossier de données : taille de la base actuelle + 20 % + marge.
  local dbsize datadir avail need
  dbsize=$(sql postgres "SELECT pg_database_size(:'db')" -v db="$DB_NAME")
  datadir=$(sql postgres "SHOW data_directory")
  avail=$("${PGRUN[@]}" df -B1 --output=avail "$datadir" 2>>"$ERR" | tail -n 1 | tr -d ' ' || true)
  need=$((dbsize + dbsize / 5 + RESTORE_DISK_MARGIN_MB * 1024 * 1024))
  if ! [[ $avail =~ ^[0-9]+$ ]] || [ "$avail" -lt "$need" ]; then
    log "restauration $id : place insuffisante"; fail_restore "$id" restore_failed; return 0
  fi

  local enc="$TMP/$bid.dump.age"
  rcl copyto "$RCLONE_REMOTE/$key" "$enc" || { fail_restore "$id" download_failed; return 0; }
  [ "$(sha256sum "$enc" | cut -c1-64)" = "$sum" ] || { fail_restore "$id" checksum_mismatch; return 0; }
  if [ ! -s "$BACKUP_AGE_IDENTITY" ] || [ ! -r "$BACKUP_AGE_IDENTITY" ]; then fail_restore "$id" key_missing; return 0; fi

  local stamp before rcs
  stamp=$(date -u +%Y%m%dt%H%M%S)
  R_TMPDB="${DB_NAME}_restore_$stamp"
  before="${DB_NAME}_before_$stamp"
  R_ACL=$(sql postgres "SELECT coalesce(datacl, acldefault('d', datdba))::text FROM pg_database WHERE datname = :'db'" -v db="$DB_NAME")

  if ! sql postgres "SELECT format('CREATE DATABASE %I WITH TEMPLATE template0 OWNER %I ENCODING %L LC_COLLATE %L LC_CTYPE %L',
        :'tmp', :'owner', pg_encoding_to_char(encoding), datcollate, datctype)
      FROM pg_database WHERE datname = :'db' \gexec
      SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', :'tmp') \gexec" \
      -v tmp="$R_TMPDB" -v owner="$DB_OWNER" -v db="$DB_NAME" >/dev/null; then
    fail_restore "$id" restore_failed; return 0
  fi

  # Déchiffrement en flux vers pg_restore (aucun dump en clair sur le disque), en superutilisateur, sans --no-owner.
  set +e
  age -d -i "$BACKUP_AGE_IDENTITY" "$enc" 2>>"$ERR" \
    | "${TIMEOUT[@]}" "${PGRUN[@]}" "$PG_BIN/pg_restore" --exit-on-error -d "$R_TMPDB" 2>>"$ERR"
  rcs=("${PIPESTATUS[@]}")
  set -e
  rm -f "$enc"
  # 141 : age coupé (SIGPIPE) parce que pg_restore s'est arrêté avant.
  if [ "${rcs[0]}" -ne 0 ] && [ "${rcs[0]}" -ne 141 ]; then fail_restore "$id" decrypt_failed; return 0; fi
  if [ "${rcs[1]}" -eq 124 ]; then fail_restore "$id" timeout; return 0; fi
  if [ "${rcs[1]}" -ne 0 ]; then fail_restore "$id" restore_failed; return 0; fi
  copy_backups_schema "$R_TMPDB" || { fail_restore "$id" restore_failed; return 0; }

  # Coupure : plus de connexion à l'ancienne base, dernière copie de l'historique, échange des noms.
  stop_heartbeat
  R_PHASE=revoked
  if ! sql postgres "SELECT format('REVOKE CONNECT, TEMPORARY ON DATABASE %I FROM PUBLIC', :'db') \gexec
      SELECT format('REVOKE CONNECT ON DATABASE %I FROM %I', :'db', r.rolname)
        FROM pg_database d, aclexplode(d.datacl) a JOIN pg_roles r ON r.oid = a.grantee
        WHERE d.datname = :'db' AND a.privilege_type = 'CONNECT' \gexec
      SELECT format('REVOKE CONNECT ON DATABASE %I FROM %I', :'db', r) FROM unnest(ARRAY[:'owner', :'app']) r
        WHERE EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) \gexec
      SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE datname = :'db' AND pid <> pg_backend_pid();" \
      -v db="$DB_NAME" -v owner="$DB_OWNER" -v app="$DB_APP" >/dev/null; then
    fail_restore "$id" swap_failed; return 0
  fi
  sql "$DB_NAME" "UPDATE backups.restores SET heartbeat_at_utc = now() WHERE id = :'id'" -v id="$id" >/dev/null || true
  copy_backups_schema "$R_TMPDB" || { fail_restore "$id" restore_failed; return 0; }

  local ok=""
  for _ in 1 2 3; do
    if sql postgres "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity
          WHERE datname IN (:'db', :'tmp') AND pid <> pg_backend_pid();
        BEGIN;
        SELECT format('ALTER DATABASE %I RENAME TO %I', :'db', :'before') \gexec
        SELECT format('ALTER DATABASE %I RENAME TO %I', :'tmp', :'db') \gexec
        COMMIT;" -v db="$DB_NAME" -v tmp="$R_TMPDB" -v before="$before" >/dev/null; then
      ok=1; break
    fi
    sleep 2
  done
  [ -n "$ok" ] || { fail_restore "$id" swap_failed; return 0; }
  R_PHASE=swapped R_TMPDB=""

  # Nouvelle base : droits de l'ancienne, plus CONNECT aux deux comptes. L'ancienne reste fermée.
  apply_acl "$DB_NAME" "$R_ACL" || log "restauration $id : droits de l'ancienne base non recopiés"
  sql postgres "SELECT format('GRANT CONNECT ON DATABASE %I TO %I, %I', :'db', :'owner', :'app') \gexec" \
    -v db="$DB_NAME" -v owner="$DB_OWNER" -v app="$DB_APP" >/dev/null || log "restauration $id : GRANT CONNECT échoué"
  sql "$DB_NAME" "UPDATE backups.restores SET status = 'Completed', completed_at_utc = now(), heartbeat_at_utc = now(),
      previous_database_name = :'before', failure_code = NULL WHERE id = :'id'" -v id="$id" -v before="$before" >/dev/null \
    || log "restauration $id : statut final non écrit"
  log "restauration $id : terminée, ancienne base $before ; lancer ./deploy.sh <version actuelle> sur le serveur applicatif"
  CUR_RESTORE="" R_PHASE=""
}

on_exit() {
  local rc=$?
  set +e
  stop_heartbeat
  [ -z "$CUR_BACKUP" ] || fail_backup "$CUR_BACKUP" "${ABORT:-$CUR_STAGE}"
  if [ -n "$CUR_RESTORE" ]; then
    if [ "$R_PHASE" = swapped ]; then log "restauration $CUR_RESTORE : interrompue après l'échange, statut à vérifier"
    else fail_restore "$CUR_RESTORE" "$([ -n "$ABORT" ] && echo timeout || echo restore_failed)"; fi
  fi
  [ -z "$TMP" ] || rm -rf "$TMP"
  exit "$rc"
}

run_once() {
  load_config
  install -d -m 700 "$WORK_DIR"
  exec 9>"$WORK_DIR/agent.lock"
  flock -n 9 || { log "passage déjà en cours"; exit 0; }
  # Restes d'un passage tué (SIGKILL) : dossiers temporaires effacés.
  find "$WORK_DIR" -mindepth 1 -maxdepth 1 -type d -name 'run.*' -exec rm -rf {} +
  TMP=$(mktemp -d "$WORK_DIR/run.XXXXXX")
  ERR="$TMP/stderr"
  trap on_exit EXIT
  trap 'ABORT=agent_restarted; exit 143' TERM INT HUP

  if [ "$(sql "$DB_NAME" "SELECT to_regclass('backups.agent') IS NOT NULL")" != t ]; then
    log "schéma backups absent (migrations non appliquées)"; exit 0
  fi
  upsert_agent
  recover_orphans
  run_backup
  run_deletions
  run_restore
}

show_status() {
  load_config
  ERR=/dev/null
  local P=("${PGRUN[@]}" "$PG_BIN/psql" -X -q -v ON_ERROR_STOP=1 -d "$DB_NAME")
  echo "== Agent"
  printf '%s\n' "SELECT host, version, last_seen_at_utc, restore_enabled FROM backups.agent" | "${P[@]}"
  echo "== Dernières sauvegardes"
  printf '%s\n' "SELECT id, trigger, status, size_bytes, failure_code, created_at_utc, completed_at_utc
    FROM backups.backups ORDER BY created_at_utc DESC LIMIT 10" | "${P[@]}"
  echo "== Dernières restaurations"
  printf '%s\n' "SELECT id, backup_id, status, failure_code, created_at_utc, completed_at_utc, previous_database_name
    FROM backups.restores ORDER BY created_at_utc DESC LIMIT 5" | "${P[@]}"
  echo "== Bases gardées"
  printf '%s\n' "SELECT datname, pg_size_pretty(pg_database_size(datname)) FROM pg_database
    WHERE datname LIKE :'p' ORDER BY datname" | "${P[@]}" -v p="${DB_NAME}_before_%"
}

install_agent() {
  local root="${INSTALL_ROOT:-}" src env_existed=1
  src=$(dirname "$SELF")
  [ "$(id -u)" -eq 0 ] || die "--install demande root"
  install -d -m 755 "$root/usr/local/sbin" "$root/etc/systemd/system"
  install -d -m 700 "$root/var/lib/restauspace-backup" "$root/root/restauspace-backup"
  install -m 700 "$SELF" "$root/usr/local/sbin/restauspace-backup-agent"
  if [ ! -e "$root/etc/restauspace-backup.env" ]; then
    env_existed=""
    install -m 600 -o root -g root "$src/backup-agent.env.example" "$root/etc/restauspace-backup.env"
    log "créé : /etc/restauspace-backup.env (à remplir)"
  fi
  cat >"$root/etc/systemd/system/restauspace-backup.service" <<'UNIT'
[Unit]
Description=RestauSpace : sauvegardes et restaurations de la base
After=postgresql.service network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=/usr/local/sbin/restauspace-backup-agent --once
SyslogIdentifier=restauspace-backup
TimeoutStartSec=12h
Nice=10
IOSchedulingClass=best-effort
IOSchedulingPriority=7
# Durcissement : setpriv (pas sudo) reste compatible avec NoNewPrivileges.
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=/var/lib/restauspace-backup
ProtectHome=read-only
PrivateTmp=true
PrivateDevices=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
RestrictSUIDSGID=true
RestrictRealtime=true
LockPersonality=true
UNIT
  cat >"$root/etc/systemd/system/restauspace-backup.timer" <<'UNIT'
[Unit]
Description=RestauSpace : passage de l'agent de sauvegarde chaque minute

[Timer]
OnCalendar=minutely
Persistent=true
AccuracySec=5s

[Install]
WantedBy=timers.target
UNIT
  chmod 644 "$root/etc/systemd/system/restauspace-backup.service" "$root/etc/systemd/system/restauspace-backup.timer"
  [ -z "$root" ] || { log "installé dans $root (systemctl non lancé)"; return 0; }
  systemctl daemon-reload
  if [ -n "$env_existed" ]; then
    systemctl enable --now restauspace-backup.timer
    log "timer actif : systemctl status restauspace-backup.timer"
  else
    log "remplir /etc/restauspace-backup.env puis : systemctl enable --now restauspace-backup.timer"
  fi
}

case "${1:---once}" in
  --once) run_once ;;
  --status) show_status ;;
  --install) install_agent ;;
  *) echo "usage : $0 [--once | --status | --install]" >&2; exit 2 ;;
esac
