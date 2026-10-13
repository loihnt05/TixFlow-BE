BEGIN;

ALTER TABLE venues
    ADD COLUMN IF NOT EXISTS organizer_id uuid;

UPDATE venues AS venue
SET organizer_id = source.organizer_id
FROM (
    SELECT DISTINCT ON (event.venue_id) event.venue_id, event.organizer_id
    FROM events AS event
    WHERE event.venue_id IS NOT NULL
    ORDER BY event.venue_id, event.created_at_utc
) AS source
WHERE venue.id = source.venue_id
  AND venue.organizer_id IS NULL;

UPDATE venues
SET organizer_id = (SELECT id FROM organizers ORDER BY created_at_utc LIMIT 1)
WHERE organizer_id IS NULL;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM venues WHERE organizer_id IS NULL) THEN
        RAISE EXCEPTION 'Cannot assign organizer ownership to every existing venue';
    END IF;
END $$;

ALTER TABLE venues
    ALTER COLUMN organizer_id SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_venues_organizers_organizer_id'
    ) THEN
        ALTER TABLE venues
            ADD CONSTRAINT fk_venues_organizers_organizer_id
            FOREIGN KEY (organizer_id) REFERENCES organizers (id) ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_venues_organizer_id_name
    ON venues (organizer_id, name);

ALTER TABLE events
    ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;

ALTER TABLE event_sessions
    ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;

COMMIT;
