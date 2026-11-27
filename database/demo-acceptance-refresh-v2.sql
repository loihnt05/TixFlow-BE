-- Refresh the two booking fixtures for a new, isolated acceptance campaign.
-- psql -d tixflow_acceptance -v ON_ERROR_STOP=1 -v anchor_utc=2026-11-27T00:00:00Z -f database/demo-acceptance-refresh-v2.sql
-- Only tixflow_acceptance is allowed. Re-running with the same anchor is safe.
BEGIN;
SELECT set_config('app.demo_anchor_utc', :'anchor_utc', true);

DO $$
DECLARE
    anchor timestamptz;
BEGIN
    IF current_database() <> 'tixflow_acceptance' THEN
        RAISE EXCEPTION 'Acceptance refresh refused on database %', current_database();
    END IF;
    BEGIN
        anchor := current_setting('app.demo_anchor_utc')::timestamptz;
    EXCEPTION WHEN others THEN
        RAISE EXCEPTION 'anchor_utc must be a valid UTC timestamp';
    END;
    IF current_setting('app.demo_anchor_utc') !~ '(Z|z|[+]00:00)$' THEN
        RAISE EXCEPTION 'anchor_utc must have UTC offset zero';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM event_sessions WHERE id = 'a1210000-0000-4000-8000-000000000001')
       OR NOT EXISTS (SELECT 1 FROM event_sessions WHERE id = 'a1210000-0000-4000-8000-000000000002') THEN
        RAISE EXCEPTION 'Apply demo-acceptance-v1.sql before refresh';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS demo_seed_runs (
    seed_version text PRIMARY KEY,
    anchor_utc timestamptz NOT NULL,
    applied_at_utc timestamptz NOT NULL DEFAULT clock_timestamp()
);

DO $$
DECLARE
    old_anchor timestamptz;
BEGIN
    SELECT anchor_utc INTO old_anchor FROM demo_seed_runs
    WHERE seed_version = 'demo-acceptance-refresh-v2';
    IF old_anchor IS NOT NULL AND old_anchor <> current_setting('app.demo_anchor_utc')::timestamptz THEN
        RAISE EXCEPTION 'Seed version already applied with a different anchor';
    END IF;
    IF old_anchor IS NULL AND EXISTS (SELECT 1 FROM holds WHERE event_session_id IN (
        'a1210000-0000-4000-8000-000000000001',
        'a1210000-0000-4000-8000-000000000002')) THEN
        RAISE EXCEPTION 'Acceptance fixture already has Holds; use a fresh database';
    END IF;
END $$;

INSERT INTO demo_seed_runs(seed_version, anchor_utc)
VALUES ('demo-acceptance-refresh-v2', current_setting('app.demo_anchor_utc')::timestamptz)
ON CONFLICT (seed_version) DO NOTHING;

UPDATE events
SET sale_starts_at_utc = current_setting('app.demo_anchor_utc')::timestamptz - interval '1 day',
    sale_ends_at_utc = current_setting('app.demo_anchor_utc')::timestamptz + interval '30 days',
    updated_at_utc = clock_timestamp()
WHERE id IN ('a1200000-0000-4000-8000-000000000001',
             'a1200000-0000-4000-8000-000000000002')
  AND (sale_starts_at_utc, sale_ends_at_utc) IS DISTINCT FROM (
      current_setting('app.demo_anchor_utc')::timestamptz - interval '1 day',
      current_setting('app.demo_anchor_utc')::timestamptz + interval '30 days');

UPDATE event_sessions
SET starts_at_utc = current_setting('app.demo_anchor_utc')::timestamptz + interval '32 days',
    ends_at_utc = current_setting('app.demo_anchor_utc')::timestamptz + interval '32 days 3 hours',
    updated_at_utc = clock_timestamp()
WHERE id IN ('a1210000-0000-4000-8000-000000000001',
             'a1210000-0000-4000-8000-000000000002')
  AND (starts_at_utc, ends_at_utc) IS DISTINCT FROM (
      current_setting('app.demo_anchor_utc')::timestamptz + interval '32 days',
      current_setting('app.demo_anchor_utc')::timestamptz + interval '32 days 3 hours');

COMMIT;
