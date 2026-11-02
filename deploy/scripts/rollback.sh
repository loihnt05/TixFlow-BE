#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
lock_deploy
release=$(realpath "${1:?Usage: rollback.sh /absolute/path/to/known-good-release}")
[[ $release == "$ROOT/releases/"* ]] || { echo 'Select a release inside this checkout.' >&2; exit 1; }
test -f "$release/completed-at.txt"
# Roll back only app images. Never implicitly downgrade PostgreSQL/Keycloak/Grafana data.
python3 - "$release/images.env" deploy/rendered/images.env <<'PY'
import sys
from pathlib import Path
def read(p): return dict(line.split('=',1) for line in Path(p).read_text().splitlines() if line)
old,current=map(read,sys.argv[1:])
for key in ['API_IMAGE','WORKER_IMAGE','FRONTEND_IMAGE']: current[key]=old[key]
p=Path(sys.argv[2]); temporary=p.with_suffix('.tmp')
temporary.write_text(''.join(f'{k}={v}\n' for k,v in current.items())); temporary.replace(p)
PY
source "$ROOT/deploy/scripts/common.sh"
dc up -d --wait --wait-timeout 300 --no-deps api worker frontend
bash deploy/scripts/smoke.sh
echo 'Application image rollback passed. Database schema and infrastructure were retained.'
