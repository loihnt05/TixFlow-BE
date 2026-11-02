ALTER TABLE tickets ADD COLUMN IF NOT EXISTS qr_image bytea;
CREATE TABLE IF NOT EXISTS processing_metrics (
    worker_id text PRIMARY KEY,
    sampled_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    values_json jsonb NOT NULL
);
