-- Exécuté une seule fois, à la création de la base. Compte en lecture seule pour l'exporteur de métriques.
-- Son mot de passe est à définir ensuite : ALTER ROLE monitoring PASSWORD '...';
CREATE ROLE monitoring LOGIN;
GRANT pg_monitor TO monitoring;
