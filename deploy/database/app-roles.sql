-- Base déjà installée (sans roles.sh) : retire les droits d'administration du propriétaire et crée le compte de l'API.
-- En superutilisateur, sur le serveur de base (PostgreSQL installé sur le système : sudo -u postgres) :
--   sudo -u postgres psql -d es_restaurant -v owner=es_restaurant_user -v app=es_restaurant_app -v app_pw='...' -f - < app-roles.sql
\set ON_ERROR_STOP on
SELECT length(:'app_pw') < 16 AS weak_password \gset
\if :weak_password
  \echo 'app_pw manquant ou trop court (16 caractères au moins) : rien n a été fait.'
  \quit
\endif
SELECT oid = 10 AS bootstrap FROM pg_roles WHERE rolname = :'owner' \gset
\if :bootstrap
  \echo 'Le propriétaire est le superutilisateur d installation : il ne peut pas perdre ce droit. Voir README (Comptes PostgreSQL).'
  \quit
\endif
ALTER ROLE :"owner" NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
SELECT format('CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD %L', :'app', :'app_pw')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app') \gexec
SELECT format('ALTER DATABASE %I OWNER TO %I', current_database(), :'owner') \gexec
SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', current_database()) \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), :'app') \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO monitoring', current_database()) WHERE EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'monitoring') \gexec
SELECT rolname, rolsuper, rolcreaterole, rolcreatedb FROM pg_roles WHERE rolname IN (:'owner', :'app');
