#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$ROOT"
ENV_FILE=${TIXFLOW_ENV_FILE:-$ROOT/.env.production}
COMPOSE=(docker compose --project-name tixflow-production --env-file "$ENV_FILE")
if [[ -f deploy/rendered/images.env ]]; then COMPOSE+=(--env-file deploy/rendered/images.env); fi
COMPOSE+=(-f docker-compose.production.yml)
dc() { "${COMPOSE[@]}" "$@"; }
value() { python3 deploy/scripts/prepare.py --env "$ENV_FILE" --get "$1"; }
lock_deploy() {
    mkdir -p releases
    exec 9>releases/deploy.lock
    flock -n 9 || { echo 'Another deployment/backup/restore is running.' >&2; exit 1; }
}
