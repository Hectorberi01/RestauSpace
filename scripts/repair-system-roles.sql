-- Réparation ponctuelle des rôles système Owner créés avant la correction de Role.CreateCore.
-- Les rôles Owner et Manager d'un même tenant partageaient les instances PermissionCode du catalogue : EF Core les a
-- rattachées au Manager, l'Owner ne gardant que staff.manage et roles.manage.
-- Owner = toutes les permissions : on lui rend celles du Manager du même tenant. Idempotent.
--
-- docker compose exec -T postgres psql -U saasrestaurant -d saasrestaurant < scripts/repair-system-roles.sql

BEGIN;

INSERT INTO staff.role_permissions (role_id, code)
SELECT owner.id, granted.code
FROM staff.roles owner
JOIN staff.roles manager
  ON manager.tenant_id = owner.tenant_id AND manager.is_system AND manager.name = 'Manager'
JOIN staff.role_permissions granted ON granted.role_id = manager.id
WHERE owner.is_system AND owner.name = 'Owner'
ON CONFLICT DO NOTHING;

SELECT r.name AS role, count(p.code) AS permissions
FROM staff.roles r
LEFT JOIN staff.role_permissions p ON p.role_id = r.id
WHERE r.is_system
GROUP BY r.tenant_id, r.name
ORDER BY r.tenant_id, r.name;

COMMIT;
