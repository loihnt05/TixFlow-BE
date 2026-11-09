-- Versioned, non-destructive and safe to rerun.
-- Stores the account contact phone used to prefill checkout attendee information.
BEGIN;
SELECT pg_advisory_xact_lock(20260928, 12);

ALTER TABLE users ADD COLUMN IF NOT EXISTS phone_number varchar(32);

COMMENT ON COLUMN users.phone_number IS
    'Optional account contact phone. Seeded from the OIDC phone_number claim when present, editable via PATCH /users/me.';

COMMIT;
