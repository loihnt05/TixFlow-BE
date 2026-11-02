-- Only slow-changing catalog metadata invalidates the shared generation.
-- Inventory and allocations are always fetched live; never put a global write lock in booking.
CREATE TABLE IF NOT EXISTS catalog_generation (
    id integer PRIMARY KEY CHECK (id = 1), version bigint NOT NULL DEFAULT 1
);
INSERT INTO catalog_generation(id) VALUES (1) ON CONFLICT DO NOTHING;
CREATE OR REPLACE FUNCTION invalidate_catalog() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    UPDATE catalog_generation SET version = version + 1 WHERE id = 1;
    RETURN NULL;
END $$;
DO $$
DECLARE target text;
BEGIN
    FOREACH target IN ARRAY ARRAY['events','event_sessions','ticket_types','organizers','venues','seats'] LOOP
        EXECUTE format('DROP TRIGGER IF EXISTS catalog_changed ON %I', target);
        EXECUTE format('CREATE TRIGGER catalog_changed AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON %I FOR EACH STATEMENT EXECUTE FUNCTION invalidate_catalog()', target);
    END LOOP;
END $$;
