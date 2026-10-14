BEGIN;

ALTER TABLE outbox_messages
    ADD COLUMN IF NOT EXISTS lease_id uuid,
    ADD COLUMN IF NOT EXISTS leased_until_utc timestamptz,
    ADD COLUMN IF NOT EXISTS correlation_id varchar(100);

ALTER TABLE tickets
    ADD COLUMN IF NOT EXISTS ordinal integer,
    ADD COLUMN IF NOT EXISTS qr_nonce varchar(64),
    ADD COLUMN IF NOT EXISTS qr_key_id varchar(40);
CREATE UNIQUE INDEX IF NOT EXISTS ux_tickets_item_ordinal ON tickets(order_item_id, ordinal);

CREATE TABLE IF NOT EXISTS message_inbox (
    message_id uuid PRIMARY KEY,
    processed_at_utc timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS mock_notifications (
    id uuid PRIMARY KEY,
    order_id uuid NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    channel varchar(24) NOT NULL,
    correlation_id varchar(100) NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    UNIQUE(order_id, channel)
);
CREATE TABLE IF NOT EXISTS ticket_check_ins (
    ticket_id uuid PRIMARY KEY REFERENCES tickets(id) ON DELETE CASCADE,
    actor_user_id uuid NOT NULL REFERENCES users(id),
    idempotency_key varchar(120) NOT NULL,
    request_hash varchar(64) NOT NULL,
    checked_in_at_utc timestamptz NOT NULL DEFAULT now(),
    UNIQUE(actor_user_id, idempotency_key)
);
ALTER TABLE ticket_check_ins ADD COLUMN IF NOT EXISTS request_hash varchar(64);

-- Recover orders paid before the phase 7 publisher was installed, once per order.
CREATE TABLE IF NOT EXISTS ticket_issuance_requests (
    order_id uuid PRIMARY KEY REFERENCES orders(id) ON DELETE CASCADE
);
WITH missing AS (
    INSERT INTO ticket_issuance_requests(order_id)
    SELECT id FROM orders WHERE status = 'Paid'
    ON CONFLICT DO NOTHING RETURNING order_id
)
INSERT INTO outbox_messages(id, type, payload, occurred_at_utc, correlation_id)
SELECT gen_random_uuid(), 'OrderPaid', jsonb_build_object('orderId', order_id), now(), order_id::text
FROM missing;

COMMIT;
