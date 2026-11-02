-- Run only with no in-flight experiments. Scoped cleanup, never TRUNCATE business tables.
BEGIN;
DO $$ BEGIN
    IF current_database() <> 'tixflow_auth_tests'
       OR NOT EXISTS (SELECT FROM baseline_control WHERE dataset = 'baseline-v1') THEN
        RAISE EXCEPTION 'Baseline reset requires the disposable Testcontainers database';
    END IF;
END $$;
SELECT pg_advisory_xact_lock(80000001);
DELETE FROM message_inbox WHERE message_id IN
    (SELECT id FROM outbox_messages WHERE payload->>'orderId' IN
        (SELECT id::text FROM orders WHERE event_session_id = '80000000-0000-0000-0000-000000000002'));
DELETE FROM outbox_messages WHERE payload->>'orderId' IN
    (SELECT id::text FROM orders WHERE event_session_id = '80000000-0000-0000-0000-000000000002');
DELETE FROM outbox_messages WHERE payload->>'paymentId' IN
    (SELECT p.id::text FROM payments p JOIN orders o ON o.id = p.order_id
     WHERE o.event_session_id = '80000000-0000-0000-0000-000000000002');
DELETE FROM payment_webhook_events WHERE payload->>'paymentId' IN
    (SELECT p.id::text FROM payments p JOIN orders o ON o.id = p.order_id
     WHERE o.event_session_id = '80000000-0000-0000-0000-000000000002');
DELETE FROM tickets WHERE order_item_id IN (SELECT i.id FROM order_items i JOIN orders o ON o.id = i.order_id
    WHERE o.event_session_id = '80000000-0000-0000-0000-000000000002');
DELETE FROM orders WHERE event_session_id = '80000000-0000-0000-0000-000000000002';
DELETE FROM holds WHERE event_session_id = '80000000-0000-0000-0000-000000000002';
DELETE FROM idempotency_records WHERE user_id IN
    (SELECT id FROM users WHERE identity_subject ~ '^baseline-v1-[0-9]+$');
UPDATE ticket_types SET price = 250000, max_per_order = 2, capacity = 50,
    sale_starts_at_utc = '2020-01-01', sale_ends_at_utc = '2099-12-20 11:00Z', version = 0, updated_at_utc = '2020-01-01'
    WHERE id = '80000000-0000-0000-0000-000000000003';
UPDATE ticket_inventories SET held_quantity = 0, sold_quantity = 0, total_quantity = 50, version = 0, updated_at_utc = '2020-01-01'
    WHERE ticket_type_id = '80000000-0000-0000-0000-000000000003';
COMMIT;
