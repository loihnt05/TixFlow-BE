#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
lock_deploy
backup=$(realpath "${1:?Usage: restore-drill.sh /absolute/backup/directory}")
test -f "$backup/completed-at.txt"
(cd "$backup" && sha256sum -c SHA256SUMS)
started=$(date +%s)
database="restore_drill_$(date -u +%Y%m%d%H%M%S)_$$"
created=0
cleanup() {
    if [[ $created == 1 ]]; then dc exec -T "$service" dropdb -U "$owner" "$database"; fi
}
trap cleanup EXIT
for service in postgres keycloak-postgres; do
    owner=tixflow; [[ $service == keycloak-postgres ]] && owner=keycloak
    dc exec -T "$service" createdb -U "$owner" -T template0 "$database"
    created=1
    dc exec -T "$service" pg_restore -U "$owner" -d "$database" --exit-on-error --no-owner < "$backup/$owner.dump"
    if [[ $owner == tixflow ]]; then
        dc exec -T "$service" psql -X -U "$owner" -d "$database" -v ON_ERROR_STOP=1 < deploy/verify-invariants.sql
    else
        dc exec -T "$service" psql -X -U "$owner" -d "$database" -v ON_ERROR_STOP=1 -c "SELECT count(*) FROM realm;"
    fi
    cleanup
    created=0
done
ended=$(date +%s)
python3 - "$backup" "$started" "$ended" <<'PY'
import datetime,json,sys
from pathlib import Path
p=Path(sys.argv[1]); start,end=map(int,sys.argv[2:])
backup_time=datetime.datetime.fromisoformat((p/'completed-at.txt').read_text().strip().replace('Z','+00:00')).timestamp()
(p/'restore-drill.json').write_text(json.dumps({'completedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'databaseRestoreSeconds':end-start,'backupAgeSeconds':max(0,start-int(backup_time)),
 'scope':'both databases restored in isolation; application failover is a separate gate'},indent=2)+'\n')
PY
echo 'Both isolated database restores and invariants passed. No live database was replaced.'
