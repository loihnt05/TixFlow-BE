BEGIN;

ALTER TABLE holds ADD COLUMN IF NOT EXISTS currency char(3) NOT NULL DEFAULT 'VND';
ALTER TABLE hold_items
    ADD COLUMN IF NOT EXISTS ticket_type_name_snapshot varchar(160),
    ADD COLUMN IF NOT EXISTS service_fee numeric(14, 2) NOT NULL DEFAULT 0;
UPDATE hold_items item SET ticket_type_name_snapshot = type.name
FROM ticket_types type WHERE item.ticket_type_id = type.id AND item.ticket_type_name_snapshot IS NULL;
ALTER TABLE hold_items ALTER COLUMN ticket_type_name_snapshot SET NOT NULL;

ALTER TABLE orders ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;
ALTER TABLE order_items
    ADD COLUMN IF NOT EXISTS ticket_type_name_snapshot varchar(160),
    ADD COLUMN IF NOT EXISTS service_fee numeric(14, 2) NOT NULL DEFAULT 0;
UPDATE order_items item SET ticket_type_name_snapshot = type.name
FROM ticket_types type WHERE item.ticket_type_id = type.id AND item.ticket_type_name_snapshot IS NULL;
ALTER TABLE order_items ALTER COLUMN ticket_type_name_snapshot SET NOT NULL;

ALTER TABLE payments ADD COLUMN IF NOT EXISTS failure_reason varchar(80);
ALTER TABLE payments ADD COLUMN IF NOT EXISTS mock_outcome varchar(24);
ALTER TABLE payment_webhook_events ADD COLUMN IF NOT EXISTS disposition varchar(24),
    ADD COLUMN IF NOT EXISTS reason varchar(80);

CREATE INDEX IF NOT EXISTS ix_payments_order_id_created_at_utc
    ON payments (order_id, created_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_outbox_messages_pending_payment
    ON outbox_messages (occurred_at_utc) WHERE processed_at_utc IS NULL;

COMMIT;
