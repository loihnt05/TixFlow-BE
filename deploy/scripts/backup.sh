#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
if [[ ${TIXFLOW_LOCK_HELD:-0} != 1 ]]; then lock_deploy; fi
stamp=$(date -u +%Y%m%dT%H%M%S)-$$
destination="$ROOT/backups/$stamp"
mkdir -p "$destination"
for service in postgres keycloak-postgres; do
    db=tixflow; [[ $service == keycloak-postgres ]] && db=keycloak
    dc exec -T "$service" pg_dump -U "$db" -d "$db" -Fc > "$destination/$db.dump.partial"
    test -s "$destination/$db.dump.partial"
    dc exec -T "$service" pg_restore --list < "$destination/$db.dump.partial" > /dev/null
    mv "$destination/$db.dump.partial" "$destination/$db.dump"
done
# Read the persistent volumes even when the application is stopped for maintenance.
dc run --rm --no-deps --entrypoint tar api -czf - -C /app ticket-keys assistant-keys > "$destination/keys.tar.gz.partial"
test -s "$destination/keys.tar.gz.partial"
tar -tzf "$destination/keys.tar.gz.partial" > /dev/null
mv "$destination/keys.tar.gz.partial" "$destination/keys.tar.gz"
(cd "$destination" && sha256sum ./*.dump ./*.tar.gz > SHA256SUMS)
date -u +%FT%TZ > "$destination/completed-at.txt"
printf '%s\n' "$destination"
# Retention is intentionally non-destructive: keep all sets until off-host verification.
