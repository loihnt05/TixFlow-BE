BEGIN;
SELECT pg_advisory_xact_lock(20260928, 16);

ALTER TABLE waiting_room_policies
    ADD COLUMN IF NOT EXISTS max_concurrent_admissions integer NOT NULL DEFAULT 500;

ALTER TABLE waiting_room_policies
    ADD CONSTRAINT ck_waiting_room_admission_capacity
    CHECK (max_concurrent_admissions BETWEEN 1 AND 100000);

COMMIT;
