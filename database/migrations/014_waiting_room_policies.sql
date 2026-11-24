-- Policy is durable; queue entries and grants are short lived in Redis.
-- There is deliberately no management activation route until the Hold gate is installed.
BEGIN;
SELECT pg_advisory_xact_lock(20260928, 14);

CREATE TABLE IF NOT EXISTS waiting_room_policies (
    event_session_id uuid PRIMARY KEY REFERENCES event_sessions(id) ON DELETE CASCADE,
    state varchar(16) NOT NULL DEFAULT 'Disabled',
    max_grants_per_second integer NOT NULL DEFAULT 5,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_waiting_room_policy_state CHECK (state IN ('Disabled', 'Active', 'Paused', 'Closed')),
    CONSTRAINT ck_waiting_room_grant_rate CHECK (max_grants_per_second BETWEEN 1 AND 1000)
);

COMMIT;
