#!/bin/sh
# Exécuté une seule fois, à la création de la base : propriétaire du schéma (migrations) et compte de l'API, sans
# aucun droit d'administration. Le propriétaire reçoit la base ; l'API reçoit ses droits à chaque déploiement
# (deploy/production/db-grants.sql).
set -eu
: "${APP_OWNER_USER:?APP_OWNER_USER manquant}" "${APP_OWNER_PASSWORD:?APP_OWNER_PASSWORD manquant}"
: "${APP_USER:?APP_USER manquant}" "${APP_PASSWORD:?APP_PASSWORD manquant}"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v owner="$APP_OWNER_USER" -v owner_pw="$APP_OWNER_PASSWORD" -v app="$APP_USER" -v app_pw="$APP_PASSWORD" <<'SQL'
CREATE ROLE :"owner" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'owner_pw';
CREATE ROLE :"app" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'app_pw';
SELECT format('ALTER DATABASE %I OWNER TO %I', current_database(), :'owner') \gexec
SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', current_database()) \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO %I, monitoring', current_database(), :'app') \gexec
SQL
