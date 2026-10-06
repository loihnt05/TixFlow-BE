\set ON_ERROR_STOP on

-- This is intentionally strict: it verifies the locked Phase 0 baseline.
-- Keep future schema checks in their own versioned verification script.
DO $$
DECLARE
    application_table_count integer;
BEGIN
    SELECT COUNT(*) INTO application_table_count
    FROM information_schema.tables
    WHERE table_schema = 'public'
      AND table_type = 'BASE TABLE'
      AND table_name <> '__EFMigrationsHistory';

    IF application_table_count <> 20 THEN
        RAISE EXCEPTION 'Phase 0 expected 20 application tables but found %', application_table_count;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'users'
          AND column_name = 'identity_subject'
    ) THEN
        RAISE EXCEPTION 'Phase 0 migration is missing users.identity_subject';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'users'
          AND column_name = 'password_hash'
          AND is_nullable <> 'YES'
    ) THEN
        RAISE EXCEPTION 'Phase 0 migration did not make users.password_hash nullable';
    END IF;

    IF (SELECT COUNT(*) FROM events WHERE status = 'Published') < 1 THEN
        RAISE EXCEPTION 'Phase 0 seed data has no published event';
    END IF;
END
$$;

SELECT
    current_database() AS database_name,
    (SELECT COUNT(*) FROM information_schema.tables
     WHERE table_schema = 'public'
       AND table_type = 'BASE TABLE'
       AND table_name <> '__EFMigrationsHistory') AS application_table_count,
    (SELECT COUNT(*) FROM events WHERE status = 'Published') AS published_event_count;
