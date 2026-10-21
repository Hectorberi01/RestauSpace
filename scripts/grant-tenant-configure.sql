-- Ajoute la permission tenant.configure (paramètres du restaurant) aux rôles système Owner et Manager des restaurants
-- déjà créés. Les restaurants créés ensuite la reçoivent à l'onboarding. Idempotent.
--
-- docker compose exec -T postgres psql -U saasrestaurant -d saasrestaurant < scripts/grant-tenant-configure.sql

BEGIN;

INSERT INTO staff.role_permissions (role_id, code)
SELECT r.id, 'tenant.configure'
FROM staff.roles r
WHERE r.is_system AND r.name IN ('Owner', 'Manager')
ON CONFLICT DO NOTHING;

SELECT r.name AS role, count(p.code) AS permissions
FROM staff.roles r
LEFT JOIN staff.role_permissions p ON p.role_id = r.id
WHERE r.is_system
GROUP BY r.tenant_id, r.name
ORDER BY r.tenant_id, r.name;

COMMIT;
