BEGIN;

ALTER TABLE ticket_types
    ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;

UPDATE ticket_types SET code = upper(btrim(code));
UPDATE seats
SET section = upper(btrim(section)),
    row_label = upper(btrim(row_label)),
    seat_number = upper(btrim(seat_number));

CREATE INDEX IF NOT EXISTS ix_seats_session_ticket_type_active
    ON seats (event_session_id, ticket_type_id, is_active);

CREATE UNIQUE INDEX IF NOT EXISTS ux_seats_session_code_ci
    ON seats (event_session_id, upper(section), upper(row_label), upper(seat_number));

CREATE INDEX IF NOT EXISTS ix_ticket_inventories_availability
    ON ticket_inventories (ticket_type_id, held_quantity, sold_quantity, total_quantity);

COMMIT;
