\set ON_ERROR_STOP on

BEGIN;

INSERT INTO orders.order_payment_settlements (tenant_id, payment_id, order_id, currency_code, settled_amount, version)
SELECT p.tenant_id,
       p.id,
       p.order_id,
       p.captured_amount_currency,
       CASE
           WHEN EXISTS (SELECT 1 FROM payments.payment_attempts a
                        WHERE a.payment_id = p.id AND a.captured_after_cancellation_at_utc IS NOT NULL) THEN 0
           ELSE GREATEST(p.captured_amount - COALESCE((SELECT SUM(r.amount) FROM payments.refunds r
                                                      WHERE r.payment_id = p.id AND r.status IN (1, 2)), 0), 0)
       END,
       0
FROM payments.payments p
WHERE p.captured_amount > 0
ON CONFLICT (tenant_id, payment_id) DO NOTHING;

INSERT INTO floor.session_orders (tenant_id, order_id, table_session_id, is_closed)
SELECT o.tenant_id, o.id, o.table_session_id, o.status IN (6, 7)
FROM orders.orders o
WHERE o.table_session_id IS NOT NULL
ON CONFLICT (tenant_id, order_id) DO NOTHING;

INSERT INTO tenants.priced_catalogs (tenant_id)
SELECT tenant_id FROM catalog.products
UNION
SELECT tenant_id FROM catalog.modifier_groups
ON CONFLICT (tenant_id) DO NOTHING;

COMMIT;
