#!/bin/sh
set -eu
umask 077
plan=$(mktemp)
trap 'rm -f "$plan"' EXIT
cat > "$plan" <<'SQL'
\set ON_ERROR_STOP on
SELECT pg_advisory_lock(120026, 12);
CREATE TABLE IF NOT EXISTS deployment_migrations (
  name text PRIMARY KEY, sha256 text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now()
);
SQL
for migration in /database/init/001_initial_schema.sql /database/migrations/*.sql; do
    name=$(basename "$migration")
    checksum=$(sha256sum "$migration" | cut -d ' ' -f 1)
    cat >> "$plan" <<SQL
SELECT EXISTS(SELECT 1 FROM deployment_migrations WHERE name='$name') AS applied,
       NOT EXISTS(SELECT 1 FROM deployment_migrations WHERE name='$name' AND sha256<>'$checksum') AS unchanged \gset
\if :unchanged
\else
\echo ERROR: checksum changed for $name
SELECT 1/0;
\endif
\if :applied
\echo Already applied $name
\else
\echo Applying $name
BEGIN;
SQL
    # Existing migrations own BEGIN/COMMIT. Move their statements and the ledger
    # into one transaction so a crash cannot commit schema without its checksum.
    sed 's/\r$//; /^BEGIN;$/d; /^COMMIT;$/d' "$migration" >> "$plan"
    cat >> "$plan" <<SQL
INSERT INTO deployment_migrations(name,sha256) VALUES ('$name','$checksum');
COMMIT;
\endif
SQL
done
printf '%s\n' 'SELECT pg_advisory_unlock(120026, 12);' >> "$plan"
psql -X -v ON_ERROR_STOP=1 -f "$plan"
