BEGIN;

ALTER TABLE holds
    ADD COLUMN IF NOT EXISTS currency char(3) NOT NULL DEFAULT 'VND',
    ADD COLUMN IF NOT EXISTS total_amount numeric(14, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;

ALTER TABLE hold_items
    ADD COLUMN IF NOT EXISTS max_per_order_snapshot integer;

UPDATE hold_items AS item
SET max_per_order_snapshot = type.max_per_order
FROM ticket_types AS type
WHERE type.id = item.ticket_type_id
  AND item.max_per_order_snapshot IS NULL;

ALTER TABLE hold_items
    ALTER COLUMN max_per_order_snapshot SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_holds_total') THEN
        ALTER TABLE holds ADD CONSTRAINT ck_holds_total CHECK (total_amount >= 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_holds_version') THEN
        ALTER TABLE holds ADD CONSTRAINT ck_holds_version CHECK (version >= 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_hold_items_max_per_order') THEN
        ALTER TABLE hold_items ADD CONSTRAINT ck_hold_items_max_per_order CHECK (
            max_per_order_snapshot > 0 AND quantity <= max_per_order_snapshot);
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS ix_hold_items_ticket_type_id
    ON hold_items (ticket_type_id);

COMMIT;
