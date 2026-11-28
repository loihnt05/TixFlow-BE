BEGIN;
SELECT pg_advisory_xact_lock(20260928, 15);

ALTER TABLE waiting_room_policies
    ADD COLUMN IF NOT EXISTS version integer NOT NULL DEFAULT 0;

COMMIT;
