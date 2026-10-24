-- Droits du compte de l'API (variable psql « app ») : lecture et écriture des données, rien d'autre (ni schéma, ni
-- table, ni rôle). Lancé par le service db-grants après chaque migration, avec le propriétaire du schéma. Rejouable.
\set QUIET on
SELECT :'app' = '' AS skip \gset
\if :skip
  \echo 'DATABASE_APP_USER vide : l API utilise le propriétaire du schéma.'
  \quit
\endif
SELECT rolsuper AS owner_is_superuser FROM pg_roles WHERE rolname = current_user \gset
\if :owner_is_superuser
  \echo 'ATTENTION : POSTGRES_USER est superutilisateur. Voir deploy/database/app-roles.sql.'
\endif
SELECT set_config('restauspace.app_role', :'app', false) AS app_role \gset

DO $$
DECLARE
    app text := current_setting('restauspace.app_role');
    s text;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = app) THEN
        RAISE EXCEPTION 'Compte % introuvable : le créer sur le serveur de base (deploy/database).', app;
    END IF;
    IF (SELECT rolsuper OR rolcreaterole OR rolcreatedb FROM pg_roles WHERE rolname = app) THEN
        RAISE EXCEPTION 'Le compte % a des droits d administration : il doit être NOSUPERUSER NOCREATEROLE NOCREATEDB.', app;
    END IF;
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), app);
    -- Les schémas des modules appartiennent au propriétaire (migrations).
    FOR s IN SELECT nspname FROM pg_namespace WHERE nspowner = (SELECT oid FROM pg_roles WHERE rolname = current_user) LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', s, app);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', s, app);
        EXECUTE format('GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA %I TO %I', s, app);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I', s, app);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO %I', s, app);
    END LOOP;
END
$$;
\echo 'Droits du compte de l API à jour.'
