\set ON_ERROR_STOP on

BEGIN;

REVOKE CREATE ON SCHEMA public FROM PUBLIC;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_identity') THEN
        CREATE ROLE saas_identity LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS identity;
ALTER SCHEMA identity OWNER TO saas_identity;
REVOKE ALL ON SCHEMA identity FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_identity;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'identity' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_identity', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'identity' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_identity', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_identity SET search_path = identity;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_tenants') THEN
        CREATE ROLE saas_tenants LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS tenants;
ALTER SCHEMA tenants OWNER TO saas_tenants;
REVOKE ALL ON SCHEMA tenants FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_tenants;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'tenants' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_tenants', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'tenants' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_tenants', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_tenants SET search_path = tenants;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_locations') THEN
        CREATE ROLE saas_locations LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS locations;
ALTER SCHEMA locations OWNER TO saas_locations;
REVOKE ALL ON SCHEMA locations FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_locations;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'locations' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_locations', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'locations' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_locations', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_locations SET search_path = locations;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_staff') THEN
        CREATE ROLE saas_staff LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS staff;
ALTER SCHEMA staff OWNER TO saas_staff;
REVOKE ALL ON SCHEMA staff FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_staff;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'staff' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_staff', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'staff' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_staff', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_staff SET search_path = staff;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_catalog') THEN
        CREATE ROLE saas_catalog LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS catalog;
ALTER SCHEMA catalog OWNER TO saas_catalog;
REVOKE ALL ON SCHEMA catalog FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_catalog;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'catalog' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_catalog', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'catalog' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_catalog', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_catalog SET search_path = catalog;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_floor') THEN
        CREATE ROLE saas_floor LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS floor;
ALTER SCHEMA floor OWNER TO saas_floor;
REVOKE ALL ON SCHEMA floor FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_floor;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'floor' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_floor', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'floor' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_floor', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_floor SET search_path = floor;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_reservations') THEN
        CREATE ROLE saas_reservations LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS reservations;
ALTER SCHEMA reservations OWNER TO saas_reservations;
REVOKE ALL ON SCHEMA reservations FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_reservations;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'reservations' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_reservations', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'reservations' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_reservations', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_reservations SET search_path = reservations;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_orders') THEN
        CREATE ROLE saas_orders LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS orders;
ALTER SCHEMA orders OWNER TO saas_orders;
REVOKE ALL ON SCHEMA orders FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_orders;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'orders' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_orders', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'orders' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_orders', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_orders SET search_path = orders;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_kitchen') THEN
        CREATE ROLE saas_kitchen LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS kitchen;
ALTER SCHEMA kitchen OWNER TO saas_kitchen;
REVOKE ALL ON SCHEMA kitchen FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_kitchen;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'kitchen' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_kitchen', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'kitchen' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_kitchen', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_kitchen SET search_path = kitchen;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_payments') THEN
        CREATE ROLE saas_payments LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS payments;
ALTER SCHEMA payments OWNER TO saas_payments;
REVOKE ALL ON SCHEMA payments FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_payments;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'payments' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_payments', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'payments' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_payments', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_payments SET search_path = payments;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_inventory') THEN
        CREATE ROLE saas_inventory LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS inventory;
ALTER SCHEMA inventory OWNER TO saas_inventory;
REVOKE ALL ON SCHEMA inventory FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_inventory;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'inventory' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_inventory', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'inventory' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_inventory', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_inventory SET search_path = inventory;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_customers') THEN
        CREATE ROLE saas_customers LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS customers;
ALTER SCHEMA customers OWNER TO saas_customers;
REVOKE ALL ON SCHEMA customers FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_customers;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'customers' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_customers', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'customers' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_customers', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_customers SET search_path = customers;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_notifications') THEN
        CREATE ROLE saas_notifications LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS notifications;
ALTER SCHEMA notifications OWNER TO saas_notifications;
REVOKE ALL ON SCHEMA notifications FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_notifications;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'notifications' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_notifications', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'notifications' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_notifications', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_notifications SET search_path = notifications;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_promotions') THEN
        CREATE ROLE saas_promotions LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS promotions;
ALTER SCHEMA promotions OWNER TO saas_promotions;
REVOKE ALL ON SCHEMA promotions FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_promotions;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'promotions' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_promotions', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'promotions' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_promotions', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_promotions SET search_path = promotions;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_loyalty') THEN
        CREATE ROLE saas_loyalty LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS loyalty;
ALTER SCHEMA loyalty OWNER TO saas_loyalty;
REVOKE ALL ON SCHEMA loyalty FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_loyalty;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'loyalty' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_loyalty', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'loyalty' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_loyalty', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_loyalty SET search_path = loyalty;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_billing') THEN
        CREATE ROLE saas_billing LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS billing;
ALTER SCHEMA billing OWNER TO saas_billing;
REVOKE ALL ON SCHEMA billing FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_billing;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'billing' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_billing', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'billing' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_billing', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_billing SET search_path = billing;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_reporting') THEN
        CREATE ROLE saas_reporting LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS reporting;
ALTER SCHEMA reporting OWNER TO saas_reporting;
REVOKE ALL ON SCHEMA reporting FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_reporting;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'reporting' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_reporting', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'reporting' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_reporting', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_reporting SET search_path = reporting;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_files') THEN
        CREATE ROLE saas_files LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS files;
ALTER SCHEMA files OWNER TO saas_files;
REVOKE ALL ON SCHEMA files FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_files;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'files' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_files', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'files' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_files', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_files SET search_path = files;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_integrations') THEN
        CREATE ROLE saas_integrations LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS integrations;
ALTER SCHEMA integrations OWNER TO saas_integrations;
REVOKE ALL ON SCHEMA integrations FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_integrations;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'integrations' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_integrations', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'integrations' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_integrations', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_integrations SET search_path = integrations;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_audit') THEN
        CREATE ROLE saas_audit LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS audit;
ALTER SCHEMA audit OWNER TO saas_audit;
REVOKE ALL ON SCHEMA audit FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_audit;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'audit' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_audit', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'audit' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_audit', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_audit SET search_path = audit;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_reviews') THEN
        CREATE ROLE saas_reviews LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS reviews;
ALTER SCHEMA reviews OWNER TO saas_reviews;
REVOKE ALL ON SCHEMA reviews FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_reviews;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'reviews' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_reviews', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'reviews' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_reviews', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_reviews SET search_path = reviews;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saas_idempotency') THEN
        CREATE ROLE saas_idempotency LOGIN PASSWORD NULL;
    END IF;
END
$$;
CREATE SCHEMA IF NOT EXISTS idempotency;
ALTER SCHEMA idempotency OWNER TO saas_idempotency;
REVOKE ALL ON SCHEMA idempotency FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO saas_idempotency;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT format('%I.%I', schemaname, tablename) AS name FROM pg_tables WHERE schemaname = 'idempotency' LOOP
        EXECUTE format('ALTER TABLE %s OWNER TO saas_idempotency', item.name);
    END LOOP;
    FOR item IN SELECT format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) AS name
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'idempotency' LOOP
        EXECUTE format('ALTER FUNCTION %s OWNER TO saas_idempotency', item.name);
    END LOOP;
END
$$;
ALTER ROLE saas_idempotency SET search_path = idempotency;

COMMIT;
